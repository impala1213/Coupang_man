using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class MushroomAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Chase,
        Attack,
        Dead
    }

    [Header("Movement")]
    public float chaseSpeed = 3.2f;
    public float gravity = -19.62f;
    public float rotationSpeed = 10f;

    [Header("Detection (FOV)")]
    public float detectionRadius = 18f;
    public float viewAngle = 120f;
    public float loseSightTime = 2.0f;
    public float eyeHeight = 1.2f;
    public LayerMask lineOfSightMask = ~0;

    [Header("Attack")]
    public float attackRange = 7f;
    public float attackCooldown = 2.4f;
    public float attackSpawnDelay = 0.35f;   // fallback if no anim event
    public float attackExitMultiplier = 1.35f;

    [Header("Spore Cloud (Forward Fan + Particles)")]
    public GameObject sporeCloudPrefab;
    public Transform sporeOrigin;
    public float cloudDuration = 2.5f;
    public float cloudRadius = 6f;
    public float cloudConeHalfAngle = 25f;
    public float cloudTickInterval = 0.35f;
    public int cloudDamagePerTick = 4;

    [Tooltip("If sporeOrigin is null, this offset is used.")]
    public float originUpOffset = 1.0f;
    public float originForwardOffset = 0.8f;

    [Header("Death Spore (Radial)")]
    public bool burstOnDeath = true;
    public int deathCloudCount = 10;
    public float deathRingRadius = 1.6f;
    public float deathCloudDuration = 2.2f;
    public float deathCloudRadius = 4.5f;
    public float deathCloudConeHalfAngle = 30f;
    public float deathCloudTickInterval = 0.4f;
    public int deathCloudDamagePerTick = 4;

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string isDeadParam = "IsDead";        // bool recommended
    public string attackTriggerParam = "Attack"; // trigger
    public string hitTriggerParam = "Hit";       // trigger

    [Header("Hit Freeze")]
    public bool freezeWhileHit = true;
    public float hitFreezeDuration = 0.45f;
    public bool cancelAttackOnHit = true;
    public bool rotateWhileHit = true;
    public float hitTriggerMinInterval = 0.15f;

    [Header("Death (Reusable Object)")]
    public EnemyDeath death = new EnemyDeath();

    [Header("Debug")]
    public State currentState = State.Idle;
    public bool debugLogs;

    private CharacterController controller;
    private Health health;
    private Transform player;

    private float verticalVelocity;

    private float lastSeenPlayerTime;
    private float lastAttackTime;

    private Vector3 lastPosition;
    private float animSpeed;

    // Attack spawn fallback
    private bool pendingCloudSpawn;
    private bool spawnedThisAttack;
    private float cloudSpawnTimer;

    // Hit freeze
    private float hitFreezeTimer;
    private float lastHitTriggerTime;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        lastPosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        // Bind EnemyDeath (object helper)
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        Rigidbody rb = GetComponent<Rigidbody>();

        // If you want to disable colliders on death through EnemyDeath, pass colliders.
        // If you prefer to keep them, pass null.
        Collider[] cols = null;

        death.gravity = gravity;
        death.animator = animator;
        death.isDeadParam = isDeadParam;
        death.speedParam = speedParam;
        death.Bind(transform, health, controller, animator, agent, rb, cols);

        death.onDeadFirstTime = () =>
        {
            currentState = State.Dead;
            pendingCloudSpawn = false;
            spawnedThisAttack = true;
            hitFreezeTimer = 0f;
            verticalVelocity = 0f;

            SpawnDeathRingClouds();
        };

        health.OnDamaged += OnDamaged;
    }

    void OnDestroy()
    {
        if (health != null) health.OnDamaged -= OnDamaged;
    }

    void Update()
    {
        // DEAD: EnemyDeath handles (AI stop + gravity + XZ lock)
        if (death.Tick())
        {
            currentState = State.Dead;
            return;
        }

        // Hit freeze: stop movement/attack while timer is active
        if (freezeWhileHit && hitFreezeTimer > 0f)
        {
            hitFreezeTimer -= Time.deltaTime;

            if (rotateWhileHit && player != null)
                FaceTowards(player.position);

            ApplyGravity();
            SetAnimatorSpeedZero();

            lastPosition = transform.position;
            return;
        }

        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Dead:
                // Safety
                return;
        }

        ApplyGravity();
        UpdateAttackSpawnTimer();
        UpdateAnimatorByPosition();

        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (death.IsDeadNow())
            death.LateTick();
    }

    private void UpdateIdle()
    {
        if (CanSeePlayer())
        {
            currentState = State.Chase;
            lastSeenPlayerTime = Time.time;
        }
    }

    private void UpdateChase()
    {
        if (player == null)
        {
            currentState = State.Idle;
            return;
        }

        if (CanSeePlayer())
            lastSeenPlayerTime = Time.time;
        else if (Time.time - lastSeenPlayerTime > loseSightTime)
        {
            currentState = State.Idle;
            return;
        }

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            currentState = State.Attack;
            return;
        }

        MoveTowards(player.position, chaseSpeed);
    }

    private void UpdateAttack()
    {
        if (player == null)
        {
            currentState = State.Idle;
            return;
        }

        FaceTowards(player.position);

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > attackRange * Mathf.Max(1f, attackExitMultiplier))
        {
            currentState = State.Chase;
            return;
        }

        if (Time.time >= lastAttackTime + attackCooldown)
            DoAttack();
    }

    private void DoAttack()
    {
        lastAttackTime = Time.time;

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        pendingCloudSpawn = true;
        spawnedThisAttack = false;
        cloudSpawnTimer = Mathf.Max(0f, attackSpawnDelay);

        if (debugLogs) Debug.Log("[MushroomAI] DoAttack()");
    }

    /// <summary>
    /// Animation Event hook: call this from Attack clip at the exact emission frame.
    /// </summary>
    public void AnimEvent_SpawnSporeCloud()
    {
        if (death.IsDeadNow()) return;
        if (spawnedThisAttack) return;

        SpawnAttackCloud();
        spawnedThisAttack = true;
        pendingCloudSpawn = false;
    }

    private void UpdateAttackSpawnTimer()
    {
        if (!pendingCloudSpawn || spawnedThisAttack) return;

        cloudSpawnTimer -= Time.deltaTime;
        if (cloudSpawnTimer <= 0f)
        {
            pendingCloudSpawn = false;
            spawnedThisAttack = true;
            SpawnAttackCloud();
        }
    }

    private void SpawnAttackCloud()
    {
        if (sporeCloudPrefab == null) return;

        GetCloudSpawnPose(out Vector3 pos, out Quaternion rot);

        GameObject go = Instantiate(sporeCloudPrefab, pos, rot);
        SporeCloudArea area = go.GetComponent<SporeCloudArea>();
        if (area != null)
        {
            area.owner = gameObject;
            area.duration = cloudDuration;
            area.radius = cloudRadius;
            area.useCone = true;
            area.coneHalfAngle = cloudConeHalfAngle;
            area.tickInterval = cloudTickInterval;
            area.damagePerTick = cloudDamagePerTick;
        }
    }

    private void SpawnDeathRingClouds()
    {
        if (!burstOnDeath) return;
        if (sporeCloudPrefab == null) return;

        int count = Mathf.Max(1, deathCloudCount);
        float step = 360f / count;

        Vector3 center = transform.position;
        center.y += 0.05f;

        for (int i = 0; i < count; i++)
        {
            float yaw = step * i;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            Vector3 pos = center + dir * Mathf.Max(0f, deathRingRadius);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            GameObject go = Instantiate(sporeCloudPrefab, pos, rot);
            SporeCloudArea area = go.GetComponent<SporeCloudArea>();
            if (area != null)
            {
                area.owner = gameObject;
                area.duration = deathCloudDuration;
                area.radius = deathCloudRadius;
                area.useCone = true;
                area.coneHalfAngle = deathCloudConeHalfAngle;
                area.tickInterval = deathCloudTickInterval;
                area.damagePerTick = deathCloudDamagePerTick;
            }
        }
    }

    private void GetCloudSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        if (sporeOrigin != null)
        {
            pos = sporeOrigin.position;
            rot = sporeOrigin.rotation;
            return;
        }

        pos = transform.position + Vector3.up * originUpOffset + transform.forward * originForwardOffset;
        rot = transform.rotation;
    }

    private bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        float distance = toPlayer.magnitude;
        if (distance > detectionRadius) return false;

        Vector3 dir = toPlayer.normalized;
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > viewAngle * 0.5f) return false;

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.1f, eyeHeight);
        if (Physics.Raycast(origin, dir, out RaycastHit hit, detectionRadius, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return hit.collider.CompareTag("Player");

        return false;
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

    private void SetAnimatorSpeedZero()
    {
        if (animator == null) return;
        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, 0f);
    }

    private void OnDamaged(Health h, int amount)
    {
        if (death.IsDeadNow()) return;

        if (freezeWhileHit)
            hitFreezeTimer = Mathf.Max(hitFreezeTimer, Mathf.Max(0.01f, hitFreezeDuration));

        if (cancelAttackOnHit)
        {
            pendingCloudSpawn = false;
            spawnedThisAttack = true;
        }

        if (animator == null) return;
        if (string.IsNullOrEmpty(hitTriggerParam)) return;

        if (Time.time < lastHitTriggerTime + Mathf.Max(0f, hitTriggerMinInterval))
            return;

        lastHitTriggerTime = Time.time;

        animator.ResetTrigger(hitTriggerParam);
        animator.SetTrigger(hitTriggerParam);
    }
}
