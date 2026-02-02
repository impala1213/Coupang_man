using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Implement this on the HELD instance (the in-hand prefab instance) to make an item "usable" with LMB.
/// Some usable items may choose to NOT play the AttackReady/Attack animation.
/// </summary>
public interface IPlayerUsable
{
    /// <summary>
    /// If true, PlayerController will drive AttackReady(bool) + Attack(trigger) for this usable.
    /// If false, no animation is driven (but hold/release timing still works).
    /// </summary>
    bool UsesAttackAnimation { get; }

    void OnPrepare(PlayerController player, PlayerUseActionContext context);
    void OnExecute(PlayerController player, PlayerUseActionContext context);
    void OnCancel(PlayerController player, PlayerUseActionContext context);
}

public enum PlayerUseInput
{
    Primary = 0,
    Throw = 1
}

public struct PlayerUseActionContext
{
    public PlayerUseInput input;
    public float heldTime;
    public float charge01;
    public Vector3 aimForward;
    public bool usesAnimation;
}

public enum PlayerUsableBehavior
{
    None = 0,
    SimpleMelee = 1
}

/// <summary>
/// Convenience component you can attach directly to an item prefab.
/// - If behavior = None: it only marks the item usable (override in derived class if needed).
/// - If behavior = SimpleMelee: applies damage to ALL targets inside the capsule volume (OverlapCapsule)
///   at the execution frame (AttackExecute animation event).
/// </summary>
public class PlayerUsable : MonoBehaviour, IPlayerUsable
{
    [Header("Animation")]
    [SerializeField] private bool usesAttackAnimation = true;
    public bool UsesAttackAnimation => usesAttackAnimation;

    [Header("Behavior")]
    public PlayerUsableBehavior behavior = PlayerUsableBehavior.SimpleMelee;

    [Header("Simple Melee Capsule (behavior=SimpleMelee)")]
    [Tooltip("Capsule length forward from the start point.")]
    public float range = 2.0f;

    [Tooltip("Capsule radius.")]
    public float radius = 0.25f;

    [Tooltip("Max damage at full charge (held >= 1s by default).")]
    public float damage = 15f; // max damage

    [Tooltip("Minimum damage (tap / no charge).")]
    public float minDamage = 5f;

    [Tooltip("If true, scales damage by hold time (charge01 0..1). Only applies to Primary input.")]
    public bool scaleDamageWithHold = true;

    [Tooltip("Hit mask for damage targets.")]
    public LayerMask hitMask = ~0;

    [Tooltip("Ignore trigger colliders when damaging.")]
    public bool ignoreTriggers = true;

    [Header("Aim / Origin")]
    [Tooltip("If assigned, use this as start point when NOT held by a player.")]
    public Transform fallbackStart;

    [Tooltip("If true and held by a PlayerController, uses player.dropOrigin as start (if set).")]
    public bool usePlayerDropOriginAsStart = true;

    [Tooltip("If player.dropOrigin is missing, this height offset is used from player position.")]
    public float fallbackStartHeight = 1.0f;

    [Header("Damage Gizmos (Editor)")]
    public bool drawDamageGizmos = true;

    [Tooltip("Draw gizmos even when not selected (can get noisy).")]
    public bool drawGizmosAlways = false;

    [Tooltip("When held by PlayerController during Play Mode, draw using the player's active camera aim direction (matches actual hit direction).")]
    public bool useCameraAimForGizmosWhenHeld = true;

    [Tooltip("Only draw gizmos when this usable is currently held by a PlayerController (Play Mode only).")]
    public bool drawOnlyWhenHeldByPlayer = true;

    [Tooltip("Show a label hint in Scene view.")]
    public bool drawDamageLabel = true;

    public string damageLabelText = "기즈모 안에 있으면 데미지";

    // Reuse buffers to avoid GC.
    private static readonly Collider[] _overlapBuffer = new Collider[64];
    private static readonly System.Collections.Generic.HashSet<int> _uniqueDamageTargets = new System.Collections.Generic.HashSet<int>();

    public virtual void OnPrepare(PlayerController player, PlayerUseActionContext context) { }
    public virtual void OnCancel(PlayerController player, PlayerUseActionContext context) { }

    public virtual void OnExecute(PlayerController player, PlayerUseActionContext context)
    {
        if (behavior == PlayerUsableBehavior.None) return;
        if (behavior != PlayerUsableBehavior.SimpleMelee) return;

        // Direction always follows context aim (PlayerController uses active camera forward).
        Vector3 dir = context.aimForward.sqrMagnitude > 0.0001f ? context.aimForward.normalized : transform.forward;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

        // Start point: when held by a player, prefer dropOrigin (hand-ish). Otherwise use fallbackStart or this transform.
        Vector3 start = transform.position;

        Transform selfRoot = transform.root;

        if (player != null && Application.isPlaying)
        {
            // If this usable is not part of player's held instance, ignore player start logic.
            bool isHeldByPlayer = (player.inventory != null && player.inventory.HeldInstance != null && transform.IsChildOf(player.inventory.HeldInstance.transform));
            if (isHeldByPlayer)
            {
                selfRoot = player.transform.root;

                if (usePlayerDropOriginAsStart && player.dropOrigin != null)
                    start = player.dropOrigin.position;
                else
                    start = player.transform.position + Vector3.up * fallbackStartHeight;
            }
        }
        else
        {
            if (fallbackStart != null) start = fallbackStart.position;
        }

        float len = Mathf.Max(0f, range);
        float r = Mathf.Max(0f, radius);
        Vector3 end = start + dir * len;

        // Damage scaling (tap -> minDamage, full charge -> damage).
        float maxDmg = Mathf.Max(damage, minDamage);
        float finalDamage = maxDmg;
        if (scaleDamageWithHold && context.input == PlayerUseInput.Primary)
        {
            finalDamage = Mathf.Lerp(minDamage, maxDmg, Mathf.Clamp01(context.charge01));
        }

        int dmgInt = Mathf.RoundToInt(finalDamage);
        if (dmgInt <= 0) return;

        QueryTriggerInteraction qti = ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;

        // OverlapCapsule gives "everyone inside" semantics.
        _uniqueDamageTargets.Clear();

        // Use NonAlloc first; if buffer is too small, fallback to alloc version for correctness.
        int hitCount = Physics.OverlapCapsuleNonAlloc(start, end, r, _overlapBuffer, hitMask, qti);
        if (hitCount == _overlapBuffer.Length)
        {
            // Might be truncated - use allocating version to ensure we hit all.
            var all = Physics.OverlapCapsule(start, end, r, hitMask, qti);
            ApplyDamageToColliders(all, dmgInt, player, selfRoot);
        }
        else
        {
            // Use buffer results
            for (int i = 0; i < hitCount; i++)
            {
                var col = _overlapBuffer[i];
                if (!col) continue;
                ApplyDamageToCollider(col, dmgInt, player, selfRoot);
            }
        }
    }

    private void ApplyDamageToColliders(Collider[] cols, int dmgInt, PlayerController player, Transform selfRoot)
    {
        if (cols == null) return;
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (!col) continue;
            ApplyDamageToCollider(col, dmgInt, player, selfRoot);
        }
    }

    private void ApplyDamageToCollider(Collider col, int dmgInt, PlayerController player, Transform selfRoot)
    {
        // Ignore self (player) colliders when held.
        if (player != null)
        {
            var pc = col.GetComponentInParent<PlayerController>();
            if (pc != null && pc.transform.root == selfRoot) return;
        }

        // Prefer Health component (your project uses Health.ApplyDamage(int)).
        var health = col.GetComponentInParent<Health>();
        if (health != null)
        {
            int id = health.GetInstanceID();
            if (_uniqueDamageTargets.Add(id))
            {
                health.ApplyDamage(dmgInt);
            }
            return;
        }

        // Fallback: try common damage message names (upwards so parent scripts can catch it).
        int rootId = col.transform.root.GetInstanceID();
        if (!_uniqueDamageTargets.Add(rootId)) return;

        col.gameObject.SendMessageUpwards("ApplyDamage", dmgInt, SendMessageOptions.DontRequireReceiver);
        col.gameObject.SendMessageUpwards("TakeDamage", dmgInt, SendMessageOptions.DontRequireReceiver);
        col.gameObject.SendMessageUpwards("TakeDamage", (float)dmgInt, SendMessageOptions.DontRequireReceiver);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (drawGizmosAlways) DrawDamageGizmosInternal();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosAlways) DrawDamageGizmosInternal();
    }

    private bool TryGetHeldPlayerAim(out Vector3 start, out Vector3 dir)
    {
        start = transform.position;
        dir = transform.forward;

        if (!Application.isPlaying) return false;

        PlayerController player = GetComponentInParent<PlayerController>();
        if (player == null) return false;
        if (player.inventory == null) return false;

        GameObject held = player.inventory.HeldInstance;
        if (held == null) return false;

        if (!transform.IsChildOf(held.transform)) return false;

        // Direction = active camera forward
        Camera cam = player.cameraSwitcher ? player.cameraSwitcher.GetActiveCamera() : Camera.main;
        if (cam == null) return false;
        dir = cam.transform.forward;

        // Start = same logic as OnExecute when held
        if (usePlayerDropOriginAsStart && player.dropOrigin != null)
            start = player.dropOrigin.position;
        else
            start = player.transform.position + Vector3.up * fallbackStartHeight;

        return true;
    }

    private void DrawDamageGizmosInternal()
    {
        if (!drawDamageGizmos) return;
        if (behavior != PlayerUsableBehavior.SimpleMelee) return;

        Vector3 start = transform.position;
        Vector3 dir = transform.forward;

        bool gotAim = false;
        if (useCameraAimForGizmosWhenHeld)
        {
            gotAim = TryGetHeldPlayerAim(out start, out dir);
        }

        if (drawOnlyWhenHeldByPlayer && !gotAim)
            return;

        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        dir.Normalize();

        float r = Mathf.Max(0f, radius);
        float len = Mathf.Max(0f, range);
        Vector3 end = start + dir * len;

        // Build an orthonormal basis for cylinder edge lines / arcs.
        Vector3 refUp = (Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f) ? Vector3.forward : Vector3.up;
        Vector3 right = Vector3.Cross(dir, refUp).normalized;
        Vector3 up = Vector3.Cross(right, dir).normalized;

        // === Filled capsule feel ===
        Color wire = new Color(1f, 0.8f, 0.1f, 0.95f);
        Color fill = new Color(1f, 0.8f, 0.1f, 0.12f);

        int steps = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(0.05f, r * 0.5f)), 6, 24);

        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        Handles.color = fill;
        for (int i = 0; i <= steps; i++)
        {
            float t = (steps == 0) ? 0f : (i / (float)steps);
            Vector3 p = Vector3.Lerp(start, end, t);
            Handles.DrawSolidDisc(p, dir, r);
        }

        // === Wire capsule outline ===
        Handles.color = wire;
        Handles.DrawWireDisc(start, dir, r);
        Handles.DrawWireDisc(end, dir, r);
        Handles.DrawWireDisc((start + end) * 0.5f, dir, r);

        Handles.DrawLine(start + right * r, end + right * r);
        Handles.DrawLine(start - right * r, end - right * r);
        Handles.DrawLine(start + up * r, end + up * r);
        Handles.DrawLine(start - up * r, end - up * r);

        Handles.DrawWireArc(start, right, up, 180f, r);
        Handles.DrawWireArc(start, up, right, 180f, r);
        Handles.DrawWireArc(end, right, -up, 180f, r);
        Handles.DrawWireArc(end, up, -right, 180f, r);

        // Direction ray
        Handles.DrawLine(start, start + dir * Mathf.Min(0.5f, Mathf.Max(0.1f, len)));

        if (drawDamageLabel)
        {
            Vector3 labelPos = (start + end) * 0.5f + up * (r + 0.05f);
            Handles.Label(labelPos, damageLabelText);
        }
    }
#endif
}
