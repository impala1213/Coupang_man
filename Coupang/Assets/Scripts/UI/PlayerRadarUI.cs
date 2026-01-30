// Assets/Scripts/UI/PlayerRadarUI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-Up radar with fixed (pre-baked) FOV wedge image.
/// - RadarBG and RotateContainer rotate together.
/// - FovWedge is never modified by this script (must stay visually fixed).
/// - Blips (items/enemies) are shown only if within range AND within viewAngle.
/// - Goals: supports multiple registered goals (e.g., multi DropZones).
///   If registry is empty, falls back to goalTarget.
/// - XZ plane only.
/// </summary>
[DisallowMultipleComponent]
public class PlayerRadarUI : MonoBehaviour
{
    private enum DotKind { Goal, Blip }

    [Header("Binding")]
    public Transform player;

    [Tooltip("Heading source (e.g., camera). If null, uses player.forward.")]
    public Transform headingSource;

    [Header("UI References")]
    public RectTransform radarRect;

    [Tooltip("Radar background rect (your RadarBG). This will rotate.")]
    public RectTransform radarBG;

    [Tooltip("Rotate container (contains IconsRoot). This will rotate.")]
    public RectTransform rotateContainer;

    [Tooltip("Icons root under rotateContainer (dots will be created here).")]
    public RectTransform iconsRoot;

    [Header("Rotation")]
    [Tooltip("If true, rotation direction is inverted (use when it feels reversed).")]
    public bool invertRotation = false;

    [Header("Radar Settings")]
    public float range = 30f;

    [Tooltip("FOV filter for blips. Should match your FovWedge visual (e.g., 120 means +/-60).")]
    [Range(1f, 360f)]
    public float viewAngle = 120f;

    [Tooltip("Padding from the radar edge so icons don't clip.")]
    public float edgePadding = 6f;

    [Header("Targets")]
    [Tooltip("Fallback single goal target when no registry goals exist.")]
    public Transform goalTarget;

    [Tooltip("Monsters: colliders in this LayerMask.")]
    public LayerMask enemyMask;

    [Tooltip("Items: colliders in this LayerMask.")]
    public LayerMask itemMask;

    [Header("Dot Prefab")]
    [Tooltip("Prefab must be a UI object with RectTransform + Image.")]
    public GameObject dotPrefab;

    [Header("Dot Sprites/Colors")]
    [Tooltip("Sprite for items/enemies (same blip). If null, dotPrefab's sprite is used.")]
    public Sprite blipSprite;

    [Tooltip("Sprite for goal dot. If null, dotPrefab's sprite is used.")]
    public Sprite goalSprite;

    [Tooltip("If true, dot Image color is forced to white (sprite keeps its own colors).")]
    public bool useSpriteNativeColor = true;

    public Color blipColor = Color.cyan;
    public Color goalColor = Color.yellow;

    [Header("Icon Size")]
    [Tooltip("Scale multiplier for the goal icon. 1 = prefab's default size.")]
    [Min(0.1f)] public float goalIconScale = 1.6f;

    [Tooltip("Scale multiplier for blip icons. 1 = prefab's default size.")]
    [Min(0.1f)] public float blipIconScale = 1.0f;

    [Tooltip("If true, icon scales are additionally multiplied by (radar diameter / referenceRadarDiameter).")]
    public bool autoScaleIconsWithRadar = true;

    [Tooltip("Radar diameter (in UI pixels) that corresponds to auto scale factor = 1.")]
    [Min(1f)] public float referenceRadarDiameter = 220f;

    [Tooltip("Clamp range for auto scale factor.")]
    public Vector2 autoScaleClamp = new Vector2(0.75f, 2.5f);

    [Header("Goal (Multi)")]
    [Tooltip("Max number of registered goals (DropZones) to show.")]
    [Min(1)] public int maxGoalsToShow = 8;

    [Tooltip("When multiple goals are clamped to the edge, spread them along the edge to avoid perfect overlap (pixels).")]
    [Min(0f)] public float goalEdgeSeparationPx = 10f;

    [Header("Performance")]
    [Min(0.02f)]
    public float scanInterval = 0.2f;

    [Min(1)]
    public int maxBlipsToShow = 48;

    [Header("Optional: Jam")]
    public bool startJammed = false;

    // ---------------- Internals ----------------
    private bool jammed;

    private readonly List<Transform> blipTargets = new List<Transform>(128);
    private readonly List<Image> blipDots = new List<Image>(128);

    private readonly List<Image> goalDots = new List<Image>(16);
    private readonly List<Transform> goalOrder = new List<Transform>(16);

    private readonly Collider[] hitsA = new Collider[128];
    private readonly Collider[] hitsB = new Collider[128];

    private readonly HashSet<int> uniqueIds = new HashSet<int>();
    private float nextScanTime;
    private float lastAutoScaleFactor = -1f;

    void Awake()
    {
        AutoBind();
        jammed = startJammed;
        ForceRefresh();
    }

    void OnEnable()
    {
        AutoBind();
        ForceRefresh();
    }

    void Update()
    {
        if (!player || !radarRect || !rotateContainer || !iconsRoot || !dotPrefab)
            return;

        UpdateIconScalesIfNeeded();

        RotateRadarContent();

        if (Time.unscaledTime >= nextScanTime)
        {
            nextScanTime = Time.unscaledTime + scanInterval;
            ScanTargets();
            // clean up any unloaded goals at low frequency
            RadarGoalRegistry.CleanupNulls();
        }

        UpdateGoalDots();

        if (!jammed)
            UpdateBlips();
        else
            SetDotsActive(blipDots, 0);
    }

    public void ForceRefresh()
    {
        nextScanTime = 0f;
        UpdateIconScalesIfNeeded();
        RotateRadarContent();
        ScanTargets();
        RadarGoalRegistry.CleanupNulls();
        UpdateGoalDots();
        UpdateBlips();
    }

    /// <summary>
    /// Optional hook (e.g., SendMessage from jammer enemy).
    /// When jammed, blips are hidden but goals remain visible.
    /// </summary>
    public void SetRadarJammed(bool value)
    {
        jammed = value;
        if (jammed) SetDotsActive(blipDots, 0);
    }

    // ---------------- Setup ----------------

    private void AutoBind()
    {
        if (!player)
        {
            var pc = FindAnyObject<PlayerController>();
            if (pc) player = pc.transform;
        }

        if (!radarRect)
            radarRect = GetComponent<RectTransform>();

        // rotateContainer/iconsRoot should be assigned in inspector.
        if (!rotateContainer && radarRect)
            rotateContainer = radarRect;

        if (!iconsRoot && rotateContainer)
            iconsRoot = rotateContainer;
    }

    // ---------------- Rotation ----------------

    private void RotateRadarContent()
    {
        Vector3 fwd = GetPlayerForwardXZ();
        if (fwd.sqrMagnitude < 0.0001f) return;

        // World north is +Z. yaw: 0 when facing north, +90 when facing east.
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

        float sign = invertRotation ? -1f : 1f;
        Quaternion rot = Quaternion.Euler(0f, 0f, sign * yaw);

        // Rotate icons container
        if (rotateContainer) rotateContainer.localRotation = rot;

        // Rotate background too (requested)
        if (radarBG) radarBG.localRotation = rot;

        // IMPORTANT: FovWedge is not touched here at all.
    }

    private Vector3 GetPlayerForwardXZ()
    {
        if (!player) return Vector3.forward;

        Vector3 f = (headingSource ? headingSource.forward : player.forward);
        f.y = 0f;

        // If camera looks straight up/down, fallback to player forward
        if (f.sqrMagnitude < 0.0001f)
        {
            f = player.forward;
            f.y = 0f;
        }

        if (f.sqrMagnitude < 0.0001f) return Vector3.forward;
        return f.normalized;
    }

    // ---------------- Scanning ----------------

    private void ScanTargets()
    {
        blipTargets.Clear();
        uniqueIds.Clear();

        if (!player) return;

        Vector3 center = player.position;

        if (enemyMask.value != 0)
            CollectTargets(center, range, enemyMask, hitsA);

        if (itemMask.value != 0)
            CollectTargets(center, range, itemMask, hitsB);

        if (blipTargets.Count > maxBlipsToShow)
            blipTargets.RemoveRange(maxBlipsToShow, blipTargets.Count - maxBlipsToShow);
    }

    private void CollectTargets(Vector3 center, float radius, LayerMask mask, Collider[] buffer)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(center, radius, buffer, mask, QueryTriggerInteraction.Collide);
        if (hitCount <= 0) return;

        for (int i = 0; i < hitCount; i++)
        {
            var c = buffer[i];
            if (!c) continue;

            Transform t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            if (!t) continue;
            if (t == player) continue;

            int id = t.GetInstanceID();
            if (!uniqueIds.Add(id)) continue;

            blipTargets.Add(t);
            if (blipTargets.Count >= maxBlipsToShow) return;
        }
    }

    // ---------------- Goal placement (Multi) ----------------

    private void UpdateGoalDots()
    {
        if (!player) return;

        goalOrder.Clear();

        // Prefer registry goals
        var goals = RadarGoalRegistry.Goals;
        var primary = RadarGoalRegistry.PrimaryGoal;

        if (primary != null)
            goalOrder.Add(primary);

        if (goals != null)
        {
            for (int i = 0; i < goals.Count && goalOrder.Count < maxGoalsToShow; i++)
            {
                var g = goals[i];
                if (!g) continue;
                if (g == primary) continue;
                goalOrder.Add(g);
            }
        }

        // Fallback to inspector goalTarget
        if (goalOrder.Count == 0 && goalTarget != null)
            goalOrder.Add(goalTarget);

        int needed = goalOrder.Count;
        EnsureDotPool(goalDots, needed, DotKind.Goal);

        float radius = GetRadarRadius();
        float half = (needed - 1) * 0.5f;

        for (int i = 0; i < goalDots.Count; i++)
        {
            var img = goalDots[i];
            if (!img)
                continue;

            if (i >= needed)
            {
                img.gameObject.SetActive(false);
                continue;
            }

            var g = goalOrder[i];
            if (!g)
            {
                img.gameObject.SetActive(false);
                continue;
            }

            img.gameObject.SetActive(true);

            Vector3 dir = g.position - player.position;
            dir.y = 0f;

            float dist = dir.magnitude;
            Vector2 local = new Vector2(dir.x, dir.z);
            float mag = local.magnitude;
            Vector2 dirN = (mag > 0.0001f) ? (local / mag) : Vector2.up;

            bool clamped = dist >= range;
            float r = clamped ? radius : (dist / Mathf.Max(0.001f, range)) * radius;

            Vector2 pos = dirN * r;

            // If clamped and there are multiple goals, spread slightly along the edge tangent.
            if (clamped && needed > 1 && goalEdgeSeparationPx > 0f)
            {
                Vector2 tangent = new Vector2(-dirN.y, dirN.x);
                float offset = (i - half) * goalEdgeSeparationPx;
                pos += tangent * offset;

                // keep inside radius
                if (pos.magnitude > radius)
                    pos = pos.normalized * radius;
            }

            img.rectTransform.anchoredPosition3D = new Vector3(pos.x, pos.y, 0f);
        }
    }

    // ---------------- Blip placement ----------------

    private void UpdateBlips()
    {
        int needed = 0;

        for (int i = 0; i < blipTargets.Count && needed < maxBlipsToShow; i++)
        {
            var t = blipTargets[i];
            if (!t) continue;

            Vector3 dir = t.position - player.position;
            dir.y = 0f;

            float dist = dir.magnitude;
            if (dist > range) continue;

            if (!IsWithinFov(dir)) continue;

            needed++;
        }

        EnsureDotPool(blipDots, needed, DotKind.Blip);

        float radius = GetRadarRadius();
        int write = 0;

        for (int i = 0; i < blipTargets.Count && write < needed; i++)
        {
            var t = blipTargets[i];
            if (!t) continue;

            Vector3 dir = t.position - player.position;
            dir.y = 0f;

            float dist = dir.magnitude;
            if (dist > range) continue;

            if (!IsWithinFov(dir)) continue;

            Vector2 local = new Vector2(dir.x, dir.z);
            float mag = local.magnitude;
            Vector2 dirN = (mag > 0.0001f) ? (local / mag) : Vector2.up;

            float r = (dist / Mathf.Max(0.001f, range)) * radius;
            Vector2 pos = dirN * r;

            var img = blipDots[write];
            img.rectTransform.anchoredPosition3D = new Vector3(pos.x, pos.y, 0f);
            img.gameObject.SetActive(true);
            write++;
        }

        for (int i = write; i < blipDots.Count; i++)
            blipDots[i].gameObject.SetActive(false);
    }

    private bool IsWithinFov(Vector3 worldDirXZ)
    {
        if (worldDirXZ.sqrMagnitude < 0.0001f) return true;

        Vector3 fwd = GetPlayerForwardXZ();
        Vector3 dn = worldDirXZ.normalized;

        float ang = Mathf.Abs(Vector3.SignedAngle(fwd, dn, Vector3.up));
        return ang <= (viewAngle * 0.5f);
    }

    private float GetRadarRadius()
    {
        float w = radarRect.rect.width;
        float h = radarRect.rect.height;

        float radius = Mathf.Min(w, h) * 0.5f;
        return Mathf.Max(0f, radius - edgePadding);
    }

    private float GetAutoScaleFactor()
    {
        if (!autoScaleIconsWithRadar || !radarRect) return 1f;

        float d = Mathf.Min(radarRect.rect.width, radarRect.rect.height);
        if (d <= 0.01f) return 1f;

        float f = d / Mathf.Max(1f, referenceRadarDiameter);

        float min = autoScaleClamp.x;
        float max = autoScaleClamp.y;
        if (max < min) { float t = min; min = max; max = t; }

        return Mathf.Clamp(f, min, max);
    }

    private void ApplyDotScale(RectTransform rt, DotKind kind)
    {
        if (!rt) return;

        float f = GetAutoScaleFactor();
        float baseScale = (kind == DotKind.Goal) ? goalIconScale : blipIconScale;
        float s = Mathf.Max(0.001f, baseScale * f);

        rt.localScale = new Vector3(s, s, 1f);
    }

    private void UpdateIconScalesIfNeeded()
    {
        float f = GetAutoScaleFactor();
        if (Mathf.Abs(f - lastAutoScaleFactor) < 0.001f)
            return;

        lastAutoScaleFactor = f;

        for (int i = 0; i < goalDots.Count; i++)
        {
            var img = goalDots[i];
            if (!img) continue;
            ApplyDotScale(img.rectTransform, DotKind.Goal);
        }

        for (int i = 0; i < blipDots.Count; i++)
        {
            var img = blipDots[i];
            if (!img) continue;
            ApplyDotScale(img.rectTransform, DotKind.Blip);
        }
    }

    // ---------------- Pooling ----------------

    private void EnsureDotPool(List<Image> pool, int needed, DotKind kind)
    {
        while (pool.Count < needed)
        {
            var go = Instantiate(dotPrefab, iconsRoot, false);
            go.name = kind.ToString() + "Dot";

            var img = go.GetComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            if (!img || !rt)
            {
                Destroy(go);
                return;
            }

            SetupDotRect(rt);
            ApplyDotVisual(img, kind);
            ApplyDotScale(rt, kind);

            go.SetActive(false);
            pool.Add(img);
        }

        // Make sure pooled objects are under iconsRoot
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] && pool[i].rectTransform.parent != iconsRoot)
                pool[i].rectTransform.SetParent(iconsRoot, false);
        }
    }

    private void SetupDotRect(RectTransform rt)
    {
        rt.SetParent(iconsRoot, false);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        // Force Z=0 to prevent depth popping
        Vector3 p = rt.localPosition;
        rt.localPosition = new Vector3(p.x, p.y, 0f);

        Vector3 ap = rt.anchoredPosition3D;
        rt.anchoredPosition3D = new Vector3(ap.x, ap.y, 0f);
    }

    private void ApplyDotVisual(Image img, DotKind kind)
    {
        if (!img) return;

        if (kind == DotKind.Goal)
        {
            if (goalSprite) img.sprite = goalSprite;
            img.color = useSpriteNativeColor ? Color.white : goalColor;
        }
        else
        {
            if (blipSprite) img.sprite = blipSprite;
            img.color = useSpriteNativeColor ? Color.white : blipColor;
        }

        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private void SetDotsActive(List<Image> pool, int countActive)
    {
        for (int i = 0; i < pool.Count; i++)
            pool[i].gameObject.SetActive(i < countActive);
    }

    private static T FindAnyObject<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
