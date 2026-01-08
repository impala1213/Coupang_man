using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class DimensionKongAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Patrol,
        Chase,
        TeleportAttack,
        Dead
    }

    [Header("State")]
    public State currentState = State.Patrol;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float gravity = -19.62f;
    public float rotationSpeed = 8f;

    [Header("Patrol")]
    public float wanderRadius = 25f;
    public float wanderPointTolerance = 1.5f;
    public float idleTimeMin = 1.5f;
    public float idleTimeMax = 4f;

    [Header("Detection")]
    public float detectionRadius = 30f;

    [Header("Keep Distance")]
    public float followStartDistance = 12f;
    public float followStartConfirmTime = 0.25f;

    [Header("Teleport Attack")]
    public float teleportIntervalMin = 60f;
    public float teleportIntervalMax = 120f;

    [Tooltip("Failsafe: if Animation Event is missing, force-finish the attack after this many seconds.")]
    public float attackTimeoutSeconds = 10f;

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

    [Header("Animation (Optional)")]
    public Animator animator;
    public string speedParam = "Speed";
    public string isDeadParam = "IsDead";
    public string attackTrigger = "Attack";

    [Header("Position Lock (Fix Idle Sliding / RootMotion Snap)")]
    [Tooltip("When facing the player (close zone) or during teleport attack, lock XZ position so only rotation changes.")]
    public bool lockXZWhileFacing = true;

    [Tooltip("If XZ drift is smaller than this, ignore (helps avoid micro jitter).")]
    public float lockXZTolerance = 0.001f;

    [Header("Debug")]
    public bool debugLogs;
    public bool debugGizmos = true;

    private CharacterController controller;
    private Health health;

    private Transform player;
    private PlayerController playerController;
    private CharacterController playerCC;

    private Vector3 homePosition;
    private Vector3? currentWanderTarget;
    private float verticalVelocity;
    private float patrolIdleTimer;

    private bool playerInRange;
    private float nextTeleportTime;

    private bool radarJamming;

    private bool isTeleportAttacking;
    private float teleportAttackDeadline;

    private float farTimer;     // confirms "player is far enough" before walking
    private bool isFollowing;   // true while we are actually walking toward the player

    // XZ lock
    private bool xzLocked;
    private Vector3 lockedXZ;

    private Vector3 lastPosition;
    private float animSpeed;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();

        CachePlayerRefs();

        homePosition = transform.position;
        lastPosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        health.OnDeath += OnDeath;

        ResetTeleportSchedule();
    }

    void Update()
    {
        if (health.IsDead())
            currentState = State.Dead;

        if (currentState == State.Dead)
        {
            UpdateDead();
            ApplyGravity();
            ApplyXZLockIfNeeded();
            UpdateAnimatorByPosition();
            lastPosition = transform.position;
            return;
        }

        if (player == null)
            CachePlayerRefs();

        bool inDetect = IsPlayerWithinDetection();

        if (inDetect)
        {
            if (!playerInRange)
            {
                playerInRange = true;
                farTimer = 0f;
                isFollowing = false;
                UnlockXZ();
                ResetTeleportSchedule();
            }

            UpdateRadarJam(true);

            // Start teleport attack if it's time.
            UpdateTeleportAttackStart();

            if (isTeleportAttacking)
            {
                currentState = State.TeleportAttack;

                // During attack: do NOT move; only face and lock XZ.
                if (player != null) FaceTowards(player.position);
                LockXZNow();

                if (Time.time >= teleportAttackDeadline)
                {
                    if (debugLogs)
                        Debug.LogWarning("[DimensionKong] Attack timeout reached. Finishing teleport attack (failsafe).");

                    FinishTeleportAttack();
                }
            }
            else
            {
                UpdateKeepDistanceBehavior();
            }
        }
        else
        {
            if (playerInRange)
            {
                playerInRange = false;
                farTimer = 0f;
                isFollowing = false;
                UnlockXZ();
                ResetTeleportSchedule();
            }

            UpdateRadarJam(false);

            if (!isTeleportAttacking)
            {
                UnlockXZ(); // allow normal patrol movement
                UpdatePatrol();
            }
        }

        ApplyGravity();
        ApplyXZLockIfNeeded();     // <- this cancels any unwanted XZ drift while facing
        UpdateAnimatorByPosition();
        lastPosition = transform.position;
    }

    private void CachePlayerRefs()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;

        player = playerObj.transform;
        playerController = playerObj.GetComponent<PlayerController>();
        playerCC = playerObj.GetComponent<CharacterController>();
    }

    private bool IsPlayerWithinDetection()
    {
        if (player == null) return false;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;

        return Vector3.Distance(a, b) <= detectionRadius;
    }

    /// <summary>
    /// Behavior:
    /// - If player stays farther than followStartDistance for followStartConfirmTime -> walk closer.
    /// - Else -> NO movement, ONLY rotate to face player (and lock XZ to prevent drift).
    /// - When follow begins, reset teleport schedule.
    /// </summary>
    private void UpdateKeepDistanceBehavior()
    {
        if (player == null)
        {
            currentState = State.Patrol;
            farTimer = 0f;
            isFollowing = false;
            UnlockXZ();
            return;
        }

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        float dist = Vector3.Distance(a, b);

        if (dist > followStartDistance)
        {
            farTimer += Time.deltaTime;
            UnlockXZ(); // allow movement while deciding/doing follow

            if (farTimer >= followStartConfirmTime)
            {
                if (!isFollowing)
                {
                    isFollowing = true;
                    ResetTeleportSchedule(); // follow start resets attack timer

                    if (debugLogs)
                        Debug.Log("[DimensionKong] Follow started -> teleport timer reset.");
                }

                currentState = State.Chase;

                // Chasing: move+rotate together
                MoveTowards(player.position, walkSpeed);
                return;
            }

            // Not confirmed yet -> stand, face only (no movement)
            currentState = State.Idle;
            FaceTowards(player.position);
            LockXZNow();
            return;
        }

        // Close zone -> stand still, face only, and lock XZ (prevents tiny drifts)
        farTimer = 0f;
        isFollowing = false;

        currentState = State.Idle;
        FaceTowards(player.position);
        LockXZNow();
    }

    private void UpdatePatrol()
    {
        if (currentState != State.Patrol && currentState != State.Idle)
        {
            patrolIdleTimer = 0f;
            currentWanderTarget = null;
        }

        if (currentState == State.Idle)
        {
            if (patrolIdleTimer <= 0f)
                patrolIdleTimer = Random.Range(idleTimeMin, idleTimeMax);

            patrolIdleTimer -= Time.deltaTime;
            if (patrolIdleTimer <= 0f)
            {
                ChooseNewWanderTarget();
                currentState = State.Patrol;
            }
            return;
        }

        currentState = State.Patrol;

        if (!currentWanderTarget.HasValue)
            ChooseNewWanderTarget();

        Vector3 target = currentWanderTarget.Value;
        MoveTowards(target, walkSpeed);

        Vector3 flatSelf = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(target.x, 0f, target.z);

        if (Vector3.Distance(flatSelf, flatTarget) <= wanderPointTolerance)
        {
            currentWanderTarget = null;
            currentState = State.Idle;
        }
    }

    private void ChooseNewWanderTarget()
    {
        Vector2 rand = Random.insideUnitCircle * wanderRadius;
        currentWanderTarget = homePosition + new Vector3(rand.x, 0f, rand.y);
    }

    private void UpdateTeleportAttackStart()
    {
        if (player == null) return;
        if (isTeleportAttacking) return;
        if (Time.time < nextTeleportTime) return;

        StartTeleportAttack();
    }

    private void StartTeleportAttack()
    {
        isTeleportAttacking = true;
        currentState = State.TeleportAttack;

        farTimer = 0f;
        isFollowing = false;

        // lock XZ during attack to avoid any drifting/rootmotion shifts
        LockXZNow();

        teleportAttackDeadline = Time.time + Mathf.Max(1f, attackTimeoutSeconds);

        if (player != null) FaceTowards(player.position);

        if (animator != null && !string.IsNullOrEmpty(attackTrigger))
        {
            animator.ResetTrigger(attackTrigger);
            animator.SetTrigger(attackTrigger);

            if (debugLogs)
                Debug.Log("[DimensionKong] Teleport attack started (Attack animation).");
        }
        else
        {
            FinishTeleportAttack();
        }
    }

    /// <summary>
    /// Animation Event hook.
    /// Put this event on the LAST frame of the Attack clip (frame 179/180).
    /// </summary>
    public void AnimEvent_TeleportPlayer()
    {
        if (!isTeleportAttacking) return;

        if (debugLogs)
            Debug.Log("[DimensionKong] Animation Event received (attack end). Teleporting player.");

        FinishTeleportAttack();
    }

    private void FinishTeleportAttack()
    {
        if (player != null && TryFindTeleportDestination(out Vector3 dest))
        {
            DoTeleportPlayer(dest);

            if (debugLogs)
                Debug.Log($"[DimensionKong] Teleported player to {dest}");
        }

        isTeleportAttacking = false;
        UnlockXZ(); // after attack, allow normal behavior
        ResetTeleportSchedule();
    }

    private void ResetTeleportSchedule()
    {
        float min = Mathf.Max(1f, teleportIntervalMin);
        float max = Mathf.Max(min, teleportIntervalMax);
        nextTeleportTime = Time.time + Random.Range(min, max);
    }

    private bool TryFindTeleportDestination(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (player == null) return false;

        float minR = Mathf.Max(0f, teleportDistanceMin);
        float maxR = Mathf.Max(minR, teleportDistanceMax);

        float capsuleRadius = 0.5f;
        float capsuleHeight = 2.0f;
        Vector3 capsuleCenterLocal = new Vector3(0f, capsuleHeight * 0.5f, 0f);

        if (playerCC != null)
        {
            capsuleRadius = playerCC.radius;
            capsuleHeight = playerCC.height;
            capsuleCenterLocal = playerCC.center;
        }

        float half = Mathf.Max(0.0f, capsuleHeight * 0.5f - capsuleRadius);

        for (int i = 0; i < teleportMaxTries; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            if (dir2.sqrMagnitude < 0.0001f) dir2 = Vector2.right;

            float r = Random.Range(minR, maxR);
            Vector3 candidate = player.position + new Vector3(dir2.x, 0f, dir2.y) * r;

            Vector3 rayOrigin = candidate + Vector3.up * teleportRaycastHeight;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, teleportRaycastHeight * 2f, teleportGroundMask, QueryTriggerInteraction.Ignore))
                continue;

            float pivotY = (hit.point.y + teleportGroundOffset) - capsuleCenterLocal.y + (capsuleHeight * 0.5f);
            Vector3 p = new Vector3(hit.point.x, pivotY, hit.point.z);

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

        if (playerController != null)
        {
            playerController.TeleportTo(dest, rot, resetKnockback: true);
            return;
        }

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

    private void UpdateRadarJam(bool playerNear)
    {
        if (player == null)
        {
            SetRadarJammed(false);
            return;
        }

        if (!playerNear)
        {
            SetRadarJammed(false);
            return;
        }

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;

        bool shouldJam = Vector3.Distance(a, b) <= radarJamRadius;
        SetRadarJammed(shouldJam);
    }

    private void SetRadarJammed(bool jam)
    {
        if (radarJamming == jam) return;
        radarJamming = jam;

        if (player != null && !string.IsNullOrEmpty(radarJamMessageName))
        {
            player.gameObject.SendMessage(radarJamMessageName, jam, SendMessageOptions.DontRequireReceiver);
        }

        if (debugLogs)
            Debug.Log($"[DimensionKong] Radar jam = {jam}");
    }

    private void ApplyGravity()
    {
        if (!controller.enabled) return;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        if (!controller.enabled) return;

        Vector3 direction = target - transform.position;
        direction.y = 0f;

        float dist = direction.magnitude;
        if (dist < 0.001f) return;

        Vector3 dirNorm = direction / dist;

        Quaternion targetRot = Quaternion.LookRotation(dirNorm, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

        controller.Move(dirNorm * speed * Time.deltaTime);
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    private void UpdateAnimatorByPosition()
    {
        if (animator == null) return;

        if (health.IsDead())
        {
            if (!string.IsNullOrEmpty(isDeadParam))
                animator.SetBool(isDeadParam, true);
            if (!string.IsNullOrEmpty(speedParam))
                animator.SetFloat(speedParam, 0f);
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;

        float horizontalSpeed = delta.magnitude / dt;
        if (horizontalSpeed < 0.2f) horizontalSpeed = 0f;

        animSpeed = Mathf.Lerp(animSpeed, horizontalSpeed, dt * 10f);

        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, animSpeed);
    }

    private void UpdateDead()
    {
        SetRadarJammed(false);

        if (controller != null && controller.enabled)
            controller.enabled = false;

        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
            animator.SetBool(isDeadParam, true);
    }

    private void OnDeath(Health h)
    {
        currentState = State.Dead;

        if (awakeTarget != null)
        {
            IWakeable wakeable = awakeTarget.GetComponent<IWakeable>();
            if (wakeable != null)
                wakeable.WakeUp();
            else
                awakeTarget.SendMessage(awakeMessageName, SendMessageOptions.DontRequireReceiver);

            if (debugLogs)
                Debug.Log($"[DimensionKong] Awaken trigger fired for '{awakeTarget.name}'");
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (controller != null) controller.enabled = false;

        Destroy(gameObject, 10f);
    }

    // -------------------------
    // XZ Lock helpers
    // -------------------------
    private void LockXZNow()
    {
        if (!lockXZWhileFacing) return;
        if (xzLocked) return;

        lockedXZ = transform.position;
        lockedXZ.y = 0f;
        xzLocked = true;
    }

    private void UnlockXZ()
    {
        xzLocked = false;
    }

    private void ApplyXZLockIfNeeded()
    {
        if (!lockXZWhileFacing) return;
        if (!xzLocked) return;
        if (controller == null || !controller.enabled) return;

        Vector3 p = transform.position;
        Vector3 correction = new Vector3(lockedXZ.x - p.x, 0f, lockedXZ.z - p.z);

        float tol = Mathf.Max(0f, lockXZTolerance);
        if (correction.sqrMagnitude <= tol * tol) return;

        controller.Move(correction);
    }

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radarJamRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, followStartDistance);
    }
}
