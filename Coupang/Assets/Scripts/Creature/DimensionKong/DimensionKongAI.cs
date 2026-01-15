// Assets/Scripts/Creature/DimensionKongAI.cs
using UnityEngine;

[RequireComponent(typeof(Health))]
public class DimensionKongAI : MonoBehaviour
{
    public enum DKState
    {
        Normal,
        Attacking,
        Dead
    }

    [Header("Refs")]
    public CreatureChase chase;
    public Health health;
    public Animator animator;

    [Header("Detection Override")]
    [Tooltip("DimensionKong uses radius-only detection. This forces CreatureChase.viewAngle = 360.")]
    public bool forceOmniDetection = true;

    [Header("Keep Distance")]
    [Tooltip("If target farther than this, allow chase movement.")]
    public float followStartDistance = 12f;

    [Tooltip("If target within this, stop moving but keep facing.")]
    public float followStopDistance = 10f;

    [Tooltip("Rotation speed while holding distance.")]
    public float faceRotationSpeed = 10f;

    [Header("Teleport Attack Timer")]
    [Tooltip("Teleport interval range while target is visible (seconds).")]
    public float teleportIntervalMin = 60f;
    public float teleportIntervalMax = 120f;

    [Tooltip("Reset teleport timer when follow (movement) starts.")]
    public bool resetTeleportOnFollowStart = true;

    [Header("Attack Animation")]
    public string attackTriggerParam = "Attack";
    public string isDeadParam = "IsDead";

    [Tooltip("Failsafe: if animation event is missing, teleport after this seconds from attack start.")]
    public float attackEventFailsafeSeconds = 3.5f;

    [Header("Teleport Destination Search")]
    public int teleportMaxTries = 24;
    public float teleportDistanceMin = 12f;
    public float teleportDistanceMax = 28f;
    public float teleportRaycastHeight = 25f;
    public float teleportGroundOffset = 0.05f;
    public LayerMask teleportGroundMask = ~0;

    [Header("Radar Jam (Optional)")]
    public float radarJamRadius = 18f;
    public string radarJamMessageName = "SetRadarJammed";

    [Header("Awaken Trigger (On Death)")]
    public GameObject awakeTarget;
    public string awakeMessageName = "WakeUp";

    [Header("Debug")]
    public bool debugGizmos = true;
    public bool debugLogs;

    private DKState dkState = DKState.Normal;

    private Transform player;
    private PlayerController playerController;
    private CharacterController playerCC;

    private float nextTeleportTime;
    private bool holdingDistance;
    private bool wasFollowingLastFrame;
    private bool radarJamming;

    // Attack state
    private bool attackTeleportPending;
    private float attackStartTime;

    // Cache CreatureChase values so we can restore after holding/attack/death
    private bool cachedChase;
    private float cachedWalk;
    private float cachedRun;
    private float cachedRot;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (chase == null) chase = GetComponent<CreatureChase>();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        if (animator != null) animator.applyRootMotion = false;

        if (chase == null)
            Debug.LogError("[DimensionKongAI] CreatureChase is required on the same GameObject.");

        CacheChaseValues();

        FindPlayerRefs();

        if (forceOmniDetection && chase != null)
            chase.viewAngle = 360f;

        if (health != null)
            health.OnDeath += OnDeath;

        ResetTeleportSchedule();
    }

    void Update()
    {
        if (health != null && health.IsDead())
            dkState = DKState.Dead;

        switch (dkState)
        {
            case DKState.Normal:
                TickNormal();
                break;

            case DKState.Attacking:
                TickAttacking();
                break;

            case DKState.Dead:
                TickDead();
                break;
        }
    }

    // ----------------------------------------------------
    // Normal (uses CreatureChase movement/search logic)
    // ----------------------------------------------------
    private void TickNormal()
    {
        FindPlayerRefsIfNeeded();

        // VisibleTarget is the only "true detection" signal we use (includes radius + LOS from CreatureChase)
        Transform visible = (chase != null) ? chase.VisibleTarget : null;

        if (visible != null)
        {
            UpdateRadarJam(true);

            // Keep-distance control (no need to touch CreatureChase internals)
            ApplyKeepDistance(visible);

            // Teleport attack timer
            if (Time.time >= nextTeleportTime)
            {
                BeginTeleportAttack();
                return;
            }
        }
        else
        {
            UpdateRadarJam(false);

            // Not visible => release keep-distance lock (restore speeds)
            holdingDistance = false;
            wasFollowingLastFrame = false;
            RestoreChaseValues();
        }
    }

    private void ApplyKeepDistance(Transform target)
    {
        float dist = HorizontalDistance(transform.position, target.position);

        bool shouldFollowMove = dist > followStartDistance;

        // Reset teleport timer when follow starts (movement begins)
        if (resetTeleportOnFollowStart && shouldFollowMove && !wasFollowingLastFrame)
        {
            ResetTeleportSchedule();
            if (debugLogs) Debug.Log("[DimensionKongAI] ResetTeleportSchedule on follow start.");
        }
        wasFollowingLastFrame = shouldFollowMove;

        // Hysteresis band to avoid jitter
        if (holdingDistance)
        {
            if (dist > followStartDistance) holdingDistance = false;
        }
        else
        {
            if (dist <= followStopDistance) holdingDistance = true;
        }

        if (holdingDistance)
        {
            // Stop CreatureChase translation, keep rotation (we rotate ourselves for consistency)
            ForceChaseSpeed(0f, 0f, 0f);
            FaceTowards(target.position, faceRotationSpeed);
        }
        else
        {
            // Allow CreatureChase to move/rotate normally
            RestoreChaseValues();
        }
    }

    // ----------------------------------------------------
    // Attack (stop CreatureChase entirely; teleport on anim end)
    // ----------------------------------------------------
    private void BeginTeleportAttack()
    {
        FindPlayerRefsIfNeeded();

        dkState = DKState.Attacking;
        attackStartTime = Time.time;
        attackTeleportPending = true;

        // Freeze CreatureChase movement/rotation while attacking (so DK stays planted)
        ForceChaseSpeed(0f, 0f, 0f);

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        if (debugLogs) Debug.Log("[DimensionKongAI] BeginTeleportAttack (waiting for anim event)");
    }

    private void TickAttacking()
    {
        // Keep facing the last visible target if any
        if (chase != null && chase.VisibleTarget != null)
            FaceTowards(chase.VisibleTarget.position, faceRotationSpeed);

        // Failsafe if Animation Event is missing
        if (attackTeleportPending && attackEventFailsafeSeconds > 0f)
        {
            if (Time.time - attackStartTime >= attackEventFailsafeSeconds)
            {
                if (debugLogs) Debug.LogWarning("[DimensionKongAI] Attack event missing -> forcing teleport now.");
                PerformTeleportNow();
            }
        }
    }

    /// <summary>
    /// Animation Event hook:
    /// Add an Animation Event on the LAST frame of Attack clip, call this function.
    /// </summary>
    public void AnimEvent_AttackEndTeleport()
    {
        if (dkState != DKState.Attacking) return;
        if (!attackTeleportPending) return;

        PerformTeleportNow();
    }

    private void PerformTeleportNow()
    {
        if (!attackTeleportPending) return;
        attackTeleportPending = false;

        FindPlayerRefsIfNeeded();

        if (player != null && TryFindTeleportDestination(out Vector3 dest))
        {
            DoTeleportPlayer(dest);
            if (debugLogs) Debug.Log($"[DimensionKongAI] Teleported player to {dest}");
        }
        else
        {
            if (debugLogs) Debug.LogWarning("[DimensionKongAI] Failed to teleport (no player or no destination).");
        }

        ResetTeleportSchedule();

        // Exit attack -> return to normal; CreatureChase will resume if we restore speeds
        dkState = DKState.Normal;
        holdingDistance = false;
        RestoreChaseValues();
    }

    private void ResetTeleportSchedule()
    {
        float min = Mathf.Max(1f, teleportIntervalMin);
        float max = Mathf.Max(min, teleportIntervalMax);
        nextTeleportTime = Time.time + Random.Range(min, max);
    }

    // ----------------------------------------------------
    // Death
    // ----------------------------------------------------
    private void OnDeath(Health h)
    {
        dkState = DKState.Dead;

        // Wake up strong monster by SendMessage (avoids interface ambiguity errors)
        if (awakeTarget != null && !string.IsNullOrEmpty(awakeMessageName))
        {
            awakeTarget.SendMessage(awakeMessageName, SendMessageOptions.DontRequireReceiver);
        }

        // Stop radar jam
        SetRadarJammed(false);

        // Freeze CreatureChase completely BUT keep it enabled so gravity inside CreatureChase can keep working (if you rely on it)
        if (chase != null)
        {
            // Prevent further detection/rotation/movement
            chase.detectionRadius = 0f;
            chase.playerMask = 0;
            ForceChaseSpeed(0f, 0f, 0f);
        }

        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
            animator.SetBool(isDeadParam, true);
    }

    private void TickDead()
    {
        // Nothing else. Leave the dead pose.
        // (CreatureChase is frozen by zero speeds and detection radius = 0)
    }

    // ----------------------------------------------------
    // Radar Jam
    // ----------------------------------------------------
    private void UpdateRadarJam(bool playerVisible)
    {
        if (player == null)
        {
            SetRadarJammed(false);
            return;
        }

        if (!playerVisible)
        {
            SetRadarJammed(false);
            return;
        }

        float dist = HorizontalDistance(transform.position, player.position);
        SetRadarJammed(dist <= radarJamRadius);
    }

    private void SetRadarJammed(bool jam)
    {
        if (radarJamming == jam) return;
        radarJamming = jam;

        if (player != null && !string.IsNullOrEmpty(radarJamMessageName))
            player.gameObject.SendMessage(radarJamMessageName, jam, SendMessageOptions.DontRequireReceiver);
    }

    // ----------------------------------------------------
    // Teleport destination search + application
    // ----------------------------------------------------
    private bool TryFindTeleportDestination(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (player == null) return false;

        float minR = Mathf.Max(0f, teleportDistanceMin);
        float maxR = Mathf.Max(minR, teleportDistanceMax);

        // Use player's capsule to avoid spawning inside colliders
        float capsuleRadius = 0.5f;
        float capsuleHeight = 2.0f;
        Vector3 capsuleCenterLocal = new Vector3(0f, capsuleHeight * 0.5f, 0f);

        if (playerCC != null)
        {
            capsuleRadius = playerCC.radius;
            capsuleHeight = playerCC.height;
            capsuleCenterLocal = playerCC.center;
        }

        float half = Mathf.Max(0f, capsuleHeight * 0.5f - capsuleRadius);

        for (int i = 0; i < teleportMaxTries; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            if (dir2.sqrMagnitude < 0.0001f) dir2 = Vector2.right;

            float r = Random.Range(minR, maxR);
            Vector3 candidate = player.position + new Vector3(dir2.x, 0f, dir2.y) * r;

            // Ground raycast
            Vector3 rayOrigin = candidate + Vector3.up * teleportRaycastHeight;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, teleportRaycastHeight * 2f, teleportGroundMask, QueryTriggerInteraction.Ignore))
                continue;

            float pivotY = (hit.point.y + teleportGroundOffset) - capsuleCenterLocal.y + (capsuleHeight * 0.5f);
            Vector3 p = new Vector3(hit.point.x, pivotY, hit.point.z);

            // Capsule overlap check
            Vector3 worldCenter = p + capsuleCenterLocal;
            Vector3 bottom = worldCenter + Vector3.down * half;
            Vector3 top = worldCenter + Vector3.up * half;

            if (Physics.CheckCapsule(bottom, top, capsuleRadius, ~0, QueryTriggerInteraction.Ignore))
                continue;

            worldPosition = p;
            return true;
        }

        return false;
    }

    private void DoTeleportPlayer(Vector3 dest)
    {
        if (player == null) return;

        Quaternion rot = player.rotation;

        // Prefer PlayerController API if it exists
        if (playerController != null)
        {
            playerController.TeleportTo(dest, rot, resetKnockback: true);
            return;
        }

        // Safe CharacterController teleport fallback
        if (playerCC != null)
        {
            bool wasEnabled = playerCC.enabled;
            playerCC.enabled = false;
            player.SetPositionAndRotation(dest, rot);
            playerCC.enabled = wasEnabled;
        }
        else
        {
            player.SetPositionAndRotation(dest, rot);
        }
    }

    // ----------------------------------------------------
    // Helpers
    // ----------------------------------------------------
    private void CacheChaseValues()
    {
        if (chase == null) return;
        if (cachedChase) return;

        cachedWalk = chase.walkSpeed;
        cachedRun = chase.runSpeed;
        cachedRot = chase.rotationSpeed;
        cachedChase = true;
    }

    private void RestoreChaseValues()
    {
        if (chase == null) return;
        if (!cachedChase) CacheChaseValues();

        chase.walkSpeed = cachedWalk;
        chase.runSpeed = cachedRun;
        chase.rotationSpeed = cachedRot;
    }

    private void ForceChaseSpeed(float walk, float run, float rot)
    {
        if (chase == null) return;
        if (!cachedChase) CacheChaseValues();

        chase.walkSpeed = walk;
        chase.runSpeed = run;
        chase.rotationSpeed = rot;
    }

    private void FaceTowards(Vector3 target, float rotSpeed)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion q = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, rotSpeed * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void FindPlayerRefsIfNeeded()
    {
        if (player != null) return;
        FindPlayerRefs();
    }

    private void FindPlayerRefs()
    {
        // Prefer tag
        GameObject p = GameObject.FindGameObjectWithTag("Player");

        // Fallback: find any PlayerController
        if (p == null)
        {
            PlayerController pc = FindObjectOfType<PlayerController>();
            if (pc != null) p = pc.gameObject;
        }

        if (p == null) return;

        player = p.transform;
        playerController = p.GetComponent<PlayerController>();
        playerCC = p.GetComponent<CharacterController>();
    }

    // ----------------------------------------------------
    // Gizmos
    // ----------------------------------------------------
    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        // Use CreatureChase radius if available
        float radius = (chase != null) ? chase.detectionRadius : 0f;

        // Color rule:
        // - Yellow: player out of radius or no refs
        // - Green : in radius and currently visible (CreatureChase.VisibleTarget != null)
        // - Red   : in radius but NOT visible
        Color c = Color.yellow;

        Transform p = null;
        if (Application.isPlaying)
        {
            p = player;
        }
        else
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) p = go.transform;
        }

        if (p != null && radius > 0f)
        {
            float d = HorizontalDistance(transform.position, p.position);
            if (d <= radius)
                c = (chase != null && chase.VisibleTarget != null) ? Color.green : Color.red;
        }

        Gizmos.color = c;
        if (radius > 0f) Gizmos.DrawWireSphere(transform.position, radius);

        // Keep-distance bands
        Gizmos.color = new Color(1f, 0f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, followStopDistance);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);
        Gizmos.DrawWireSphere(transform.position, followStartDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radarJamRadius);
    }
}
