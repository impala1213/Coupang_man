using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class DimensionKongAI : MonoBehaviour
{
    public enum State
    {
        Patrol,
        Idle,
        Follow,
        Attack,
        Dead
    }

    [Header("State")]
    public State currentState = State.Patrol;

    [Header("Movement")]
    public float walkSpeed = 2.0f;
    public float runSpeed = 5.5f;
    public float gravity = -19.62f;
    public float rotationSpeed = 8f;

    [Header("Patrol")]
    public float wanderRadius = 25f;
    public float wanderPointTolerance = 1.5f;
    public float idleTimeMin = 1.5f;
    public float idleTimeMax = 4f;

    [Header("Detection (Radius only)")]
    public float detectionRadius = 30f;

    [Header("Keep Distance (Hysteresis)")]
    public float followStartDistance = 12f; // if farther than this => approach
    public float followStopDistance = 10f;  // if within this => stop and stare

    [Header("Teleport Attack Schedule (only while near)")]
    public float teleportIntervalMin = 60f;
    public float teleportIntervalMax = 120f;

    [Header("Attack Animation Timing")]
    [Tooltip("If you don't use animation event, teleport happens after this delay (180 frames @60fps = 3s).")]
    public float attackTeleportDelay = 3.0f;

    [Header("Teleport Destination")]
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

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string isDeadParam = "IsDead";         // bool recommended
    public string attackTriggerParam = "Attack";  // trigger

    [Header("Death (Reusable Object)")]
    public EnemyDeath death = new EnemyDeath();

    [Header("Debug")]
    public bool debugLogs;
    public bool debugGizmos = true;

    private CharacterController controller;
    private Health health;
    private Transform player;
    private CharacterController playerCC;

    private Vector3 homePosition;
    private Vector3? wanderTarget;
    private float idleTimer;

    private float verticalVelocity;
    private Vector3 lastPosition;
    private float animSpeed;

    private bool radarJamming;

    // Attack schedule
    private bool playerInDetection;
    private float nextTeleportTime;

    // Attack execution
    private bool attacking;
    private float attackTeleportTimer;
    private Vector3 pendingTeleportDest;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerCC = playerObj.GetComponent<CharacterController>();
        }

        homePosition = transform.position;
        lastPosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        // Bind EnemyDeath
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        Rigidbody rb = GetComponent<Rigidbody>();
        Collider[] cols = null;

        death.gravity = gravity;
        death.animator = animator;
        death.isDeadParam = isDeadParam;
        death.speedParam = speedParam;
        death.Bind(transform, health, controller, animator, agent, rb, cols);

        death.onDeadFirstTime = () =>
        {
            currentState = State.Dead;
            SetRadarJammed(false);
            TriggerAwakeTarget();
        };

        ResetTeleportSchedule(forceNow: true);
    }

    void Update()
    {
        // DEAD: EnemyDeath handles (AI stop + gravity + XZ lock)
        if (death.Tick())
        {
            currentState = State.Dead;
            return;
        }

        EnsurePlayerRef();

        bool inDetect = IsPlayerWithinDetection();
        if (inDetect && !playerInDetection)
        {
            playerInDetection = true;
            ResetTeleportSchedule(forceNow: true);
        }
        else if (!inDetect && playerInDetection)
        {
            playerInDetection = false;
            ResetTeleportSchedule(forceNow: true);
        }

        // Radar jam
        UpdateRadarJam(inDetect);

        // Attack state handling
        if (attacking)
        {
            UpdateAttackExecution();
            ApplyGravity();
            UpdateAnimatorByPosition();
            lastPosition = transform.position;
            return;
        }

        if (!inDetect)
        {
            UpdatePatrol();
        }
        else
        {
            UpdateKeepDistanceLogic();
            UpdateTeleportScheduleAndMaybeAttack();
        }

        ApplyGravity();
        UpdateAnimatorByPosition();
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (death.IsDeadNow())
            death.LateTick();
    }

    private void EnsurePlayerRef()
    {
        if (player != null) return;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;

        player = playerObj.transform;
        playerCC = playerObj.GetComponent<CharacterController>();
    }

    private bool IsPlayerWithinDetection()
    {
        if (player == null) return false;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        return Vector3.Distance(a, b) <= detectionRadius;
    }

    private void UpdateKeepDistanceLogic()
    {
        if (player == null) return;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        float dist = Vector3.Distance(a, b);

        // If far => approach (FOLLOW) and reset teleport timer (per your request)
        if (dist > followStartDistance)
        {
            currentState = State.Follow;
            ResetTeleportSchedule(forceNow: false); // push attack out while following
            MoveTowards(player.position, runSpeed);
            return;
        }

        // If close enough => stop and stare
        if (dist <= followStopDistance)
        {
            currentState = State.Idle;
            FaceTowards(player.position);
            return;
        }

        // In hysteresis band => keep previous behavior
        if (currentState == State.Follow)
        {
            MoveTowards(player.position, runSpeed);
        }
        else
        {
            currentState = State.Idle;
            FaceTowards(player.position);
        }
    }

    private void UpdateTeleportScheduleAndMaybeAttack()
    {
        if (player == null) return;

        // Only count down while "near" (idle / not following)
        if (currentState == State.Follow || currentState == State.Patrol)
            return;

        if (Time.time < nextTeleportTime)
            return;

        // Start attack (play animation), teleport at the end
        if (TryFindTeleportDestination(out Vector3 dest))
        {
            StartAttack(dest);
        }

        ResetTeleportSchedule(forceNow: false);
    }

    private void StartAttack(Vector3 teleportDest)
    {
        attacking = true;
        currentState = State.Attack;

        pendingTeleportDest = teleportDest;
        attackTeleportTimer = Mathf.Max(0.01f, attackTeleportDelay);

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        if (player != null)
            FaceTowards(player.position);

        if (debugLogs) Debug.Log("[DimensionKong] Attack started (teleport pending).");
    }

    private void UpdateAttackExecution()
    {
        if (player != null)
            FaceTowards(player.position);

        attackTeleportTimer -= Time.deltaTime;
        if (attackTeleportTimer <= 0f)
        {
            attacking = false;
            DoTeleportPlayer(pendingTeleportDest);
            currentState = State.Idle;

            if (debugLogs) Debug.Log("[DimensionKong] Teleport executed (timer fallback).");
        }
    }

    /// <summary>
    /// Animation Event hook: call this at the END of attack animation.
    /// </summary>
    public void AnimEvent_TeleportPlayerNow()
    {
        if (death.IsDeadNow()) return;
        if (!attacking) return;

        attacking = false;
        DoTeleportPlayer(pendingTeleportDest);
        currentState = State.Idle;

        if (debugLogs) Debug.Log("[DimensionKong] Teleport executed (animation event).");
    }

    private void ResetTeleportSchedule(bool forceNow)
    {
        float min = Mathf.Max(1f, teleportIntervalMin);
        float max = Mathf.Max(min, teleportIntervalMax);

        float interval = forceNow ? Random.Range(min, max) : Random.Range(min, max);
        nextTeleportTime = Time.time + interval;
    }

    private bool TryFindTeleportDestination(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (player == null) return false;

        float minR = Mathf.Max(0f, teleportDistanceMin);
        float maxR = Mathf.Max(minR, teleportDistanceMax);

        // Approximate player capsule if we can't read it
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
            Vector2 dir2 = Random.insideUnitCircle;
            if (dir2.sqrMagnitude < 0.0001f) dir2 = Vector2.right;
            dir2.Normalize();

            float r = Random.Range(minR, maxR);
            Vector3 flatOffset = new Vector3(dir2.x, 0f, dir2.y) * r;

            Vector3 candidate = player.position + flatOffset;

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
        float dist = Vector3.Distance(a, b);

        SetRadarJammed(dist <= radarJamRadius);
    }

    private void SetRadarJammed(bool jam)
    {
        if (radarJamming == jam) return;
        radarJamming = jam;

        if (player != null && !string.IsNullOrEmpty(radarJamMessageName))
            player.gameObject.SendMessage(radarJamMessageName, jam, SendMessageOptions.DontRequireReceiver);

        if (debugLogs) Debug.Log($"[DimensionKong] Radar jam = {jam}");
    }

    private void TriggerAwakeTarget()
    {
        if (awakeTarget == null) return;

        IWakeable w = awakeTarget.GetComponent<IWakeable>();
        if (w != null)
        {
            w.WakeUp();
            return;
        }

        awakeTarget.SendMessage(awakeMessageName, SendMessageOptions.DontRequireReceiver);
    }

    private void UpdatePatrol()
    {
        if (currentState != State.Patrol && currentState != State.Idle)
        {
            idleTimer = 0f;
            wanderTarget = null;
        }

        if (currentState == State.Idle)
        {
            if (idleTimer <= 0f) idleTimer = Random.Range(idleTimeMin, idleTimeMax);

            idleTimer -= Time.deltaTime;
            if (idleTimer <= 0f)
            {
                ChooseNewWanderTarget();
                currentState = State.Patrol;
            }
            return;
        }

        currentState = State.Patrol;

        if (!wanderTarget.HasValue)
            ChooseNewWanderTarget();

        Vector3 target = wanderTarget.Value;
        MoveTowards(target, walkSpeed);

        Vector3 flatSelf = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(target.x, 0f, target.z);
        if (Vector3.Distance(flatSelf, flatTarget) <= wanderPointTolerance)
        {
            wanderTarget = null;
            currentState = State.Idle;
        }
    }

    private void ChooseNewWanderTarget()
    {
        Vector2 rand = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePosition + new Vector3(rand.x, 0f, rand.y);
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        if (controller == null || !controller.enabled) return;

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

    private void ApplyGravity()
    {
        if (controller == null || !controller.enabled) return;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    private void UpdateAnimatorByPosition()
    {
        if (animator == null) return;

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

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, followStopDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, followStartDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radarJamRadius);
    }
}

