// Assets/Scripts/Player/CarrierController.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Unity 6+ rename compatibility (PhysicMaterial -> PhysicsMaterial)
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    // ───────────── References ─────────────
    [Header("References")]
    [Tooltip("Pivot that holds the carrier model and all cargo slots.")]
    public Transform carrierPivot;     // unified pivot (model + stack under here)
    [Tooltip("Transform used to sample movement (usually the player root).")]
    public Transform wobbleReference;  // used for velocity sampling

    // ───────────── Stack layout ─────────────
    [Header("Stack Layout")]
    [Tooltip("Small vertical spacing between stacked cargos (meters).")]
    public float stackVerticalSpacing = 0.0f;

    // ───────────── Visual sway (auto-recover) ─────────────
    [Header("Sway (auto-recover)")]
    [Tooltip("Lateral accel (m/s^2) to degrees for visual sway.")]
    public float swayAccToDeg = 8f;
    [Tooltip("How fast sway goes back to zero when stopping (deg/s).")]
    public float swayRecoverSpeed = 80f;
    [Tooltip("Max sway angle (deg).")]
    public float swayMaxDeg = 10f;
    [Tooltip("Tiny lateral slide per degree (meters/deg).")]
    public float swayXOffsetPerDeg = 0.0008f;

    private float _swayDeg;          // current visual sway angle (deg, + = right)

    // ───────────── Elastic bend (bottom→top inertia) ─────────────
    [Header("Elastic Bend (bottom→top)")]
    public bool useElasticBend = true;
    [Tooltip("Spring stiffness for bend (deg/s^2).")]
    public float bendStiffness = 14f;
    [Tooltip("Damping factor for bend.")]
    public float bendDamping = 3.5f;
    [Tooltip("Drive from lateral acceleration into the first layer (deg per (m/s^2)).")]
    public float bendAccToDrive = 4f;
    [Tooltip("How much upper layers amplify the bend.")]
    public float bendUpAmplify = 1.1f;
    [Tooltip("Clamp per-layer bend angle.")]
    public float bendMaxDegPerLayer = 8f;
    [Tooltip("Horizontal offset per degree of bend (meters/deg).")]
    public float bendXOffsetPerDeg = 0.0006f;

    // Bend state per cargo layer (slot pivot)
    private readonly List<float> _bendAngles = new List<float>();
    private readonly List<float> _bendVels = new List<float>();

    // slot pivots (CarrierSlot_*) kept in bottom-to-top order
    private readonly List<Transform> _slotChain = new List<Transform>();
    private int _lastSlotCount = -1;

    // ───────────── Impact spill (auto drop on fall/knock) ─────────────
    [Header("Impact Spill")]
    [Tooltip("Enable automatic spill on hard landing / knock.")]
    public bool enableImpactSpill = true;

    [Tooltip("Vertical drop height (m) to trigger spill on landing.")]
    public float fallHeightTrigger = 3.0f;

    [Tooltip("Downward impact speed (m/s) to trigger spill on landing.")]
    public float impactSpeedTrigger = 8.0f;

    [Tooltip("Sudden horizontal speed (m/s) considered a knock (absolute speed).")]
    public float knockSpeedTrigger = 10.0f;

    [Tooltip("Change of horizontal speed (m/s) between frames considered a sudden move.")]
    public float horizontalSpeedDeltaTrigger = 12.0f;

    [Tooltip("Minimum seconds between automatic spills (impact/knock/velocity spike).")]
    public float minTimeBetweenAutoSpills = 0.4f;

    [Header("Velocity Inherit")]
    [Tooltip("How much of the player's horizontal velocity is inherited by spilled cargo.")]
    public float inheritVelocityFactor = 0.8f;

    // grounded state is reported by PlayerController
    private bool _lastGrounded = true;
    private bool _airborne = false;
    private float _fallStartY;
    private Vector3 _lastPos;
    private Vector3 _lastVel;

    /// <summary>Smoothed velocity sampled from wobbleReference.</summary>
    public Vector3 sampledVelocity { get; private set; }
    /// <summary>Smoothed acceleration sampled from wobbleReference.</summary>
    public Vector3 sampledAcceleration { get; private set; }

    private float _lastSpillTime;
    private Vector3 _lastHorizVel;

    // ───────────── Spill physics (realistic) ─────────────
    [Header("Spill Physics")]
    [Tooltip("Random sideways launch speed range when cargo spills.")]
    public Vector2 cargoSideSpeedRange = new Vector2(0.25f, 0.8f);
    [Tooltip("Forward launch speed when there is no meaningful player velocity.")]
    public float cargoForwardSpeed = 0.2f;
    [Tooltip("Upward bias for dropped cargo (negative = a bit downwards).")]
    public float cargoUpBias = -0.1f;
    [Tooltip("Random angular velocity magnitude for dropped cargo.")]
    public float cargoAngularVel = 1.5f;
    [Tooltip("Friction material to apply on spilled cargo colliders.")]
    public PhysicsMaterial cargoFrictionMaterial;
    [Tooltip("If true, configure rigidbodies on spilled cargo for continuous collision etc.")]
    public bool configureCargoRigidbodies = true;

    // ───────────── State ─────────────
    [Header("State")]
    [Tooltip("True when this carrier is dropped in the world (not worn on player).")]
    public bool isDroppedWorldCarrier = false;  // world bundle state

    // ───────────── Visual variants by cargo amount ─────────────
    [Header("Visual Variants (by cargo count)")]
    [Tooltip("Default visual when no variant is used (optional).")]
    public GameObject baseVisual;
    [Tooltip("Visual when the carrier has no cargo.")]
    public GameObject visualEmpty;
    [Tooltip("Visual when the carrier has a small amount of cargo.")]
    public GameObject visualFew;
    [Tooltip("Visual when the carrier is heavily loaded.")]
    public GameObject visualMany;
    [Tooltip("Max cargo count considered 'few' before switching to 'many'.")]
    public int fewCountThreshold = 2;

    // ───────────── Data ─────────────
    [Header("Derived (read-only)")]
    [Tooltip("Total weight of all mounted cargo items.")]
    public float totalWeight;
    [Tooltip("Total stack height of mounted cargo items.")]
    public float stackTotalHeight;

    // actual loaded WorldItem list
    private readonly List<WorldItem> mounted = new List<WorldItem>();
    public IReadOnlyList<WorldItem> MountedItems => mounted;

    // ───────────── Unity lifecycle ─────────────
    void Awake()
    {
        EnsureCarrierPivot();

        _lastPos = wobbleReference ? wobbleReference.position : transform.position;
        _lastHorizVel = Vector3.zero;
        _lastSpillTime = -999f;

        RebuildSlotChain();
        UpdateVisualVariant();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureCarrierPivot();
    }
#endif

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateTelemetry(dt);
        UpdateDerived();
        UpdateSway(dt);
        UpdateElasticBend(dt);
    }

    // ───────────── Public API ─────────────

    /// <summary>
    /// PlayerController should call this each frame to report grounded and position.
    /// Handles fall/landing detection for impact-based spills.
    /// </summary>
    public void ReportGroundedState(bool grounded, Vector3 worldPos, Vector3 controllerVelocity)
    {
        if (!enableImpactSpill || isDroppedWorldCarrier)
        {
            _lastGrounded = grounded;
            return;
        }

        if (!grounded && _lastGrounded)
        {
            _airborne = true;
            _fallStartY = worldPos.y;
        }
        else if (grounded && !_lastGrounded)
        {
            if (_airborne)
            {
                float fallHeight = _fallStartY - worldPos.y;
                float downSpeed = Mathf.Max(0f, -controllerVelocity.y);

                if ((fallHeight >= fallHeightTrigger || downSpeed >= impactSpeedTrigger) &&
                    Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
                {
                    Vector3 origin = carrierPivot
                        ? carrierPivot.position
                        : transform.position + Vector3.up * 1.0f;

                    Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
                    Vector3 dir = horizVel.sqrMagnitude > 0.01f
                        ? horizVel.normalized
                        : transform.forward;

                    SpillAllAt(origin, dir);
                }
            }

            _airborne = false;
        }

        _lastGrounded = grounded;
    }

    /// <summary>
    /// Called when an external system (e.g., enemy hit) notifies a knock.
    /// If speed is big enough, spill all cargo.
    /// </summary>
    public void NotifyExternalKnock(float speedMagnitude)
    {
        if (!enableImpactSpill || isDroppedWorldCarrier) return;

        if (speedMagnitude >= knockSpeedTrigger &&
            Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
        {
            Vector3 origin = carrierPivot
                ? carrierPivot.position
                : transform.position + Vector3.up * 1.0f;

            Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
            Vector3 dir = horizVel.sqrMagnitude > 0.01f
                ? horizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
        }
    }

    /// <summary>
    /// Try to mount a world item onto the carrier (top stacking).
    /// Items are stacked one above another using ItemDefinition.stackSize.y.
    /// </summary>
    public bool TryMount(WorldItem world)
    {
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        EnsureCarrierPivot();

        // Sum up the total height of existing cargos on the carrier
        float currentHeight = 0f;
        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (w && w.definition)
            {
                currentHeight += Mathf.Max(0.01f, w.definition.stackSize.y);
            }
        }

        // Height of the new cargo
        var def = world.definition;
        float newHeight = Mathf.Max(0.01f, def.stackSize.y);

        // Center of the new slot: top of stack + half of new height + spacing
        float centerY = currentHeight + newHeight * 0.5f + stackVerticalSpacing;

        int slotIndex = mounted.Count;
        GameObject slotGO = new GameObject($"CarrierSlot_{slotIndex}");
        Transform slotPivot = slotGO.transform;
        slotPivot.SetParent(carrierPivot, false);
        slotPivot.localPosition = new Vector3(0f, centerY, -0.1f);
        slotPivot.localRotation = Quaternion.identity;
        slotPivot.localScale = Vector3.one;

        // Mount world item to carrier slot
        world.EnterCarrierMountMode(this, slotIndex, slotPivot);

        mounted.Add(world);
        RebuildSlotChain();
        UpdateVisualVariant();
        return true;
    }

    /// <summary>
    /// Drop the entire carrier (frame + mounted cargo) into the world as a single bundle.
    /// Items remain mounted on the carrier and do not spill.
    /// Carrier itself는 다른 아이템과 동일하게 컨테이너/Ship 부모 관리 대상이 된다.
    /// </summary>
    public void DropAsBundle(Vector3 worldPos, Vector3 forward)
    {
        // Detach the carrier root from its current parent (usually the player spine).
        Transform root = transform;
        root.SetParent(null, true);

        // Compute a flat forward direction based on the given forward.
        Vector3 flatF = new Vector3(forward.x, 0f, forward.z);
        if (flatF.sqrMagnitude < 0.0001f)
            flatF = root.forward;
        flatF.y = 0f;
        if (flatF.sqrMagnitude > 0.0001f)
            flatF.Normalize();
        else
            flatF = Vector3.forward;

        // Place the carrier bundle in the world.
        root.position = worldPos;
        root.rotation = Quaternion.LookRotation(flatF, Vector3.up);

        // Mark as dropped world carrier so sway/bend/impact logic can behave accordingly.
        isDroppedWorldCarrier = true;

        // Re-enable physics and colliders so the dropped carrier behaves as a normal world item.
        var wi = GetComponent<WorldItem>();
        if (wi)
        {
            // World carrier behaves like a normal item again (container system may re-parent it).
            wi.ignoreContainerAutoParent = false;

            if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
            if (!wi.rb) wi.rb = wi.gameObject.AddComponent<Rigidbody>();

            var rb = wi.rb;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
            }

            var cols = wi.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols) c.enabled = true;

            var rends = wi.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) r.enabled = true;
        }

        // Keep mounted list as-is; visual variant will still reflect cargo count.
        RebuildSlotChain();
        UpdateVisualVariant();

        // IMPORTANT:
        // Let container parenting system re-assign this carrier like any other item
        // (parent = containerRoot or outsideParent(Ship)),
        // so that when Ship root is disabled, this carrier also disappears appropriately.
        var zone = UnityEngine.Object.FindFirstObjectByType<ContainerAutoParent>();
        if (zone != null)
        {
            zone.ResyncSceneItems();
        }
    }

    /// <summary>
    /// Called when other systems want to spill all cargo (e.g., forced drop).
    /// </summary>
    public void SpillAllOnCarrierDrop(Vector3 origin, Vector3 forward)
    {
        SpillAllAt(origin, forward);
    }

    /// <summary>
    /// Spill all current mounted cargo to world.
    /// forwardDir is used as fallback direction when no velocity is present.
    /// </summary>
    public void SpillAllAt(Vector3 origin, Vector3 forwardDir)
    {
        var copy = new List<WorldItem>(mounted);
        for (int i = 0; i < copy.Count; i++)
        {
            var wi = copy[i];
            if (!wi) continue;

            Vector3 pos = (carrierPivot ? carrierPivot.position : transform.position)
                          + Vector3.up * 0.1f
                          + UnityEngine.Random.insideUnitSphere * 0.05f;

            ApplyRealisticDrop(wi, pos, forwardDir);
        }

        mounted.Clear();
        ClearVisuals();
        RebuildSlotChain();
        UpdateVisualVariant();

        _lastSpillTime = Time.time;
    }

    /// <summary>True if any items are mounted on this carrier.</summary>
    public bool HasAnyMounted() => mounted.Count > 0;

    /// <summary>
    /// Called when this carrier is equipped on the player (picked up as item).
    /// Resets spill-related state so the stack is not immediately spilled.
    /// </summary>
    public void MarkEquipped(float spillGraceSeconds = 0.3f)
    {
        isDroppedWorldCarrier = false;
        _lastGrounded = true;
        _airborne = false;

        float grace = Mathf.Max(0f, spillGraceSeconds);
        _lastSpillTime = Time.time + grace;
    }

    /// <summary>
    /// Build debug string listing mounted items and slot indices for UI.
    /// </summary>
    public string GetSlotDebugString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Carrier Slots:");

        if (mounted.Count == 0)
        {
            sb.AppendLine("(empty)");
            return sb.ToString();
        }

        for (int i = 0; i < mounted.Count; i++)
        {
            var wi = mounted[i];
            if (!wi || wi.definition == null) continue;

            int slotIdx = wi.carrierSlotIndex >= 0 ? wi.carrierSlotIndex : i;
            sb.Append('[').Append(slotIdx).Append("] ").AppendLine(wi.definition.displayName);
        }

        return sb.ToString();
    }

    // ───────────── Internals ─────────────

    private void UpdateTelemetry(float dt)
    {
        var t = wobbleReference ? wobbleReference : transform;
        Vector3 p = t.position;

        if (dt > 0f)
        {
            Vector3 v = (p - _lastPos) / dt;
            Vector3 a = (v - _lastVel) / dt;

            sampledVelocity = Vector3.Lerp(sampledVelocity, v, 0.2f);
            sampledAcceleration = Vector3.Lerp(sampledAcceleration, a, 0.2f);

            _lastVel = v;
        }

        _lastPos = p;

        if (!enableImpactSpill || isDroppedWorldCarrier)
            return;

        Vector3 curHorizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
        float curSpeed = curHorizVel.magnitude;
        float lastSpeed = _lastHorizVel.magnitude;
        float deltaSpeed = curSpeed - lastSpeed;

        // Sudden speed increase → auto spill (e.g., hit wall violently)
        if (deltaSpeed >= horizontalSpeedDeltaTrigger &&
            Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
        {
            Vector3 origin = carrierPivot
                ? carrierPivot.position
                : transform.position + Vector3.up * 1.0f;

            Vector3 dir = curHorizVel.sqrMagnitude > 0.01f
                ? curHorizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
        }

        _lastHorizVel = curHorizVel;
    }

    private void UpdateDerived()
    {
        totalWeight = 0f;
        stackTotalHeight = 0f;

        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (!w || !w.definition) continue;

            totalWeight += Mathf.Max(0.01f, w.definition.weight);
            stackTotalHeight += Mathf.Max(0.01f, w.definition.stackSize.y);
        }
    }

    private void UpdateSway(float dt)
    {
        if (!carrierPivot) return;
        if (isDroppedWorldCarrier) return;

        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float target = accX * swayAccToDeg;

        Vector2 horizV = new Vector2(sampledVelocity.x, sampledVelocity.z);
        if (horizV.magnitude < 0.2f) target = 0f;

        _swayDeg = Mathf.MoveTowards(_swayDeg, target, swayRecoverSpeed * dt);
        _swayDeg = Mathf.Clamp(_swayDeg, -swayMaxDeg, swayMaxDeg);

        carrierPivot.localRotation = Quaternion.Euler(0f, 0f, -_swayDeg);
        Vector3 lp = carrierPivot.localPosition;
        lp.x = _swayDeg * swayXOffsetPerDeg;
        carrierPivot.localPosition = lp;
    }

    private void UpdateElasticBend(float dt)
    {
        if (!useElasticBend || !carrierPivot) return;
        if (isDroppedWorldCarrier) return;

        _slotChain.RemoveAll(t => t == null);

        int n = _slotChain.Count;
        if (n <= 0)
        {
            _bendAngles.Clear();
            _bendVels.Clear();
            _lastSlotCount = 0;
            return;
        }

        SyncBendArrays(n);

        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float baseDrive = accX * bendAccToDrive;

        for (int i = 0; i < n; i++)
        {
            var t = _slotChain[i];
            if (t == null) continue;

            float target = (i == 0) ? baseDrive : _bendAngles[i - 1];
            float angle = _bendAngles[i];
            float vel = _bendVels[i];

            vel += (target - angle) * Mathf.Max(0.1f, bendStiffness) * dt;
            vel *= 1f / (1f + Mathf.Max(0f, bendDamping) * dt);
            angle += vel * dt;

            angle = Mathf.Clamp(angle, -bendMaxDegPerLayer, bendMaxDegPerLayer);

            _bendAngles[i] = angle;
            _bendVels[i] = vel;

            float amp = Mathf.Pow(Mathf.Max(1f, bendUpAmplify), i);
            float visDeg = Mathf.Clamp(angle * amp, -bendMaxDegPerLayer * 2f, bendMaxDegPerLayer * 2f);

            var e = t.localEulerAngles;
            e.x = 0f;
            e.y = 0f;
            e.z = -visDeg;
            t.localEulerAngles = e;

            Vector3 lp = t.localPosition;
            lp.x = visDeg * bendXOffsetPerDeg;
            t.localPosition = lp;
        }

        _lastSlotCount = n;
    }

    private void SyncBendArrays(int n)
    {
        while (_bendAngles.Count < n) _bendAngles.Add(0f);
        while (_bendVels.Count < n) _bendVels.Add(0f);

        while (_bendAngles.Count > n) _bendAngles.RemoveAt(_bendAngles.Count - 1);
        while (_bendVels.Count > n) _bendVels.RemoveAt(_bendVels.Count - 1);
    }

    private void EnsureCarrierPivot()
    {
        if (carrierPivot)
            return;

        // Try to find an existing child named "CarrierPivot".
        var existing = transform.Find("CarrierPivot");
        if (existing != null)
        {
            carrierPivot = existing;
            return;
        }

        // Create a new pivot and re-parent existing children under it.
        var go = new GameObject("CarrierPivot");
        carrierPivot = go.transform;
        carrierPivot.SetParent(transform, false);
        carrierPivot.localPosition = Vector3.zero;
        carrierPivot.localRotation = Quaternion.identity;
        carrierPivot.localScale = Vector3.one;

        var toReparent = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            var ch = transform.GetChild(i);
            if (ch == carrierPivot) continue;
            toReparent.Add(ch);
        }

        foreach (var ch in toReparent)
        {
            ch.SetParent(carrierPivot, true);
        }
    }

    private void RebuildSlotChain()
    {
        _slotChain.Clear();

        if (!carrierPivot)
        {
            _lastSlotCount = 0;
            _bendAngles.Clear();
            _bendVels.Clear();
            return;
        }

        for (int i = 0; i < carrierPivot.childCount; i++)
        {
            var ch = carrierPivot.GetChild(i);
            if (ch == null) continue;
            if (ch.name.StartsWith("CarrierSlot_", StringComparison.Ordinal))
            {
                _slotChain.Add(ch);
            }
        }

        _slotChain.Sort((a, b) => a.localPosition.y.CompareTo(b.localPosition.y));
        SyncBendArrays(_slotChain.Count);
        _lastSlotCount = _slotChain.Count;
    }

    /// <summary>
    /// Removes all carrier slot visuals (CarrierSlot_* transforms) but keeps the carrier model.
    /// </summary>
    private void ClearVisuals()
    {
        if (!carrierPivot) return;

        for (int i = carrierPivot.childCount - 1; i >= 0; i--)
        {
            var child = carrierPivot.GetChild(i);
            if (child == null) continue;
            if (child.name.StartsWith("CarrierSlot_", StringComparison.Ordinal))
            {
                Destroy(child.gameObject);
            }
        }

        _slotChain.Clear();
        _bendAngles.Clear();
        _bendVels.Clear();
        _lastSlotCount = 0;
    }

    /// <summary>
    /// Actually returns the item to the world and applies realistic velocity
    /// based on sampled player movement.
    /// Also removes the (now empty) slot pivot that used to hold the item.
    /// </summary>
    private void ApplyRealisticDrop(WorldItem wi, Vector3 pos, Vector3 forwardDir)
    {
        if (wi == null) return;

        Transform slotPivot = wi.carrierSlotPivot;

        Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
        float horizSpeed = horizVel.magnitude;

        Vector3 mainDir;
        float mainSpeed;

        if (horizSpeed > 0.1f)
        {
            mainDir = horizVel.normalized;
            mainSpeed = horizSpeed * Mathf.Max(0f, inheritVelocityFactor);
        }
        else
        {
            Vector3 fwd = forwardDir.sqrMagnitude > 0.0001f
                ? forwardDir.normalized
                : transform.forward;

            mainDir = fwd;
            mainSpeed = Mathf.Max(0f, cargoForwardSpeed);
        }

        Vector3 side = Vector3.zero;
        if (cargoSideSpeedRange.y > 0f)
        {
            Vector3 sideBase = Vector3.Cross(mainDir, Vector3.up);
            if (sideBase.sqrMagnitude > 0.0001f)
            {
                sideBase.Normalize();
                if (UnityEngine.Random.value < 0.5f)
                    sideBase = -sideBase;

                float sideSpeed = UnityEngine.Random.Range(cargoSideSpeedRange.x, cargoSideSpeedRange.y);
                side = sideBase * sideSpeed;
            }
        }

        Vector3 v =
            mainDir * mainSpeed +
            side +
            Vector3.up * cargoUpBias;

        wi.OnDropped(pos, v);

        if (slotPivot != null)
        {
            try
            {
                if (slotPivot != null && slotPivot.parent == carrierPivot)
                {
                    Destroy(slotPivot.gameObject);
                }
            }
            catch (Exception)
            {
                // Ignored (object may already be destroyed)
            }
        }

        _slotChain.RemoveAll(t => t == null);
        SyncBendArrays(_slotChain.Count);

        if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
        var rb = wi.rb;
        if (rb != null)
        {
            if (configureCargoRigidbodies)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.angularDamping = Mathf.Max(0.2f, rb.angularDamping);
            }

            if (cargoFrictionMaterial)
            {
                var cols = wi.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                    cols[c].sharedMaterial = cargoFrictionMaterial;
            }

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * cargoAngularVel;
        }
    }

    // ───────────── Visual variants helper ─────────────

    private void SetActiveSafe(GameObject go, bool active)
    {
        if (go && go.activeSelf != active)
            go.SetActive(active);
    }

    /// <summary>
    /// Updates carrier model variant based on mounted cargo count.
    /// </summary>
    private void UpdateVisualVariant()
    {
        int count = mounted.Count;

        if (visualEmpty == null && visualFew == null && visualMany == null)
        {
            return;
        }

        GameObject target = null;

        if (count <= 0)
            target = visualEmpty;
        else if (count <= fewCountThreshold)
            target = visualFew;
        else
            target = visualMany;

        SetActiveSafe(visualEmpty, visualEmpty == target);
        SetActiveSafe(visualFew, visualFew == target);
        SetActiveSafe(visualMany, visualMany == target);

        if (baseVisual != null)
        {
            bool anyVariant = (visualEmpty || visualFew || visualMany);
            if (anyVariant)
                SetActiveSafe(baseVisual, target == null);
        }
    }
}
