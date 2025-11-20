using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class BeastAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 6f;
    public float gravity = -19.62f;
    public float rotationSpeed = 8f;
    public float wanderRadius = 25f;
    public float wanderPointTolerance = 1.5f;
    public float idleTimeMin = 1.5f;
    public float idleTimeMax = 4f;

    [Header("Detection")]
    public float detectionRadius = 30f;
    public float viewAngle = 120f;
    public float loseSightTime = 3f;

    [Header("Combat")]
    public float attackRange = 2.5f;
    public float attackCooldown = 1.5f;
    public int attackDamage = 20;
    public Transform attackOrigin;
    public float attackExtraRadius = 0.5f;

    [Header("Combat Timing")]
    public float attackHitDelay = 0.4f; // 공격 시작 후 이 시간 지나면 데미지+넉백

    [Header("Drops")]
    public ItemDefinition dropItem;
    public int dropCount = 1;
    public float dropScatterRadius = 1.5f;

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string isDeadParam = "IsDead";
    public string attackTriggerParam = "Attack";

    [Header("Debug")]
    public State currentState = State.Idle;
    public bool debugSpeed;
    public bool debugAttack;

    private CharacterController controller;
    private Health health;
    private Transform player;
    private Vector3 homePosition;
    private Vector3? currentWanderTarget;
    private float verticalVelocity;
    private float idleTimer;
    private float lastSeenPlayerTime;
    private float lastAttackTime;

    private float animSpeed;
    private Vector3 lastPosition;

    // 공격 1회당 히트 딜레이용
    private bool pendingAttackHit;
    private bool hasAppliedDamageThisAttack;
    private float attackHitTimer;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }

        homePosition = transform.position;
        lastPosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        health.OnDeath += OnDeath;
    }

    void Update()
    {
        if (health.IsDead())
        {
            currentState = State.Dead;
        }

        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Patrol:
                UpdatePatrol();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Dead:
                UpdateDead();
                break;
        }

        ApplyGravity();
        UpdateAnimatorByPosition();
        UpdateAttackHitTimer(); // ★ 히트 딜레이 처리

        lastPosition = transform.position;
    }

    private void UpdateIdle()
    {
        if (idleTimer <= 0f)
        {
            idleTimer = Random.Range(idleTimeMin, idleTimeMax);
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            ChooseNewWanderTarget();
            currentState = State.Patrol;
        }

        if (CanSeePlayer())
        {
            currentState = State.Chase;
        }
    }

    private void UpdatePatrol()
    {
        if (CanSeePlayer())
        {
            currentState = State.Chase;
            return;
        }

        if (!currentWanderTarget.HasValue)
        {
            ChooseNewWanderTarget();
        }

        Vector3 target = currentWanderTarget.Value;
        MoveTowards(target, walkSpeed);

        Vector3 flatSelf = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(target.x, 0f, target.z);
        float dist = Vector3.Distance(flatSelf, flatTarget);

        if (dist <= wanderPointTolerance)
        {
            currentWanderTarget = null;
            currentState = State.Idle;
        }
    }

    private void UpdateChase()
    {
        if (player == null)
        {
            currentState = State.Patrol;
            return;
        }

        if (CanSeePlayer())
        {
            lastSeenPlayerTime = Time.time;
        }
        else
        {
            if (Time.time - lastSeenPlayerTime > loseSightTime)
            {
                currentState = State.Patrol;
                return;
            }
        }

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            currentState = State.Attack;
            return;
        }

        MoveTowards(player.position, runSpeed);
    }

    private void UpdateAttack()
    {
        if (player == null)
        {
            currentState = State.Patrol;
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > attackRange * 1.4f)
        {
            currentState = State.Chase;
            return;
        }

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            DoAttack();
        }
    }

    private void UpdateDead()
    {
        if (controller.enabled)
        {
            controller.enabled = false;
        }
    }

    private void ApplyGravity()
    {
        if (!controller.enabled) return;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 move = Vector3.up * verticalVelocity * Time.deltaTime;
        controller.Move(move);
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

        Vector3 move = dirNorm * speed * Time.deltaTime;
        controller.Move(move);
    }

    private void ChooseNewWanderTarget()
    {
        Vector2 rand = Random.insideUnitCircle * wanderRadius;
        Vector3 target = homePosition + new Vector3(rand.x, 0f, rand.y);
        currentWanderTarget = target;
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

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, detectionRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.CompareTag("Player"))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateAnimatorByPosition()
    {
        if (animator == null) return;

        if (health.IsDead())
        {
            animator.SetBool(isDeadParam, true);
            animator.SetFloat(speedParam, 0f);
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        float horizontalSpeed = delta.magnitude / dt;

        if (horizontalSpeed < 0.2f)
        {
            horizontalSpeed = 0f;
        }

        animSpeed = Mathf.Lerp(animSpeed, horizontalSpeed, dt * 10f);

        if (debugSpeed)
        {
            Debug.Log($"PolarBear Speed = {animSpeed:F2}");
        }

        animator.SetFloat(speedParam, animSpeed);
    }

    private void DoAttack()
    {
        lastAttackTime = Time.time;

        if (debugAttack)
        {
            Debug.Log("PolarBear DoAttack()");
        }

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        // 히트 딜레이 초기화
        pendingAttackHit = true;
        hasAppliedDamageThisAttack = false;
        attackHitTimer = Mathf.Max(0f, attackHitDelay);
    }

    private void UpdateAttackHitTimer()
    {
        if (!pendingAttackHit || hasAppliedDamageThisAttack)
            return;

        attackHitTimer -= Time.deltaTime;
        if (attackHitTimer <= 0f)
        {
            pendingAttackHit = false;
            hasAppliedDamageThisAttack = true;

            if (!health.IsDead())
            {
                TryApplyDamageToPlayer();
            }
        }
    }

    private void TryApplyDamageToPlayer()
    {
        if (player == null) return;

        Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position;
        float dist = Vector3.Distance(origin, player.position);
        float maxDist = attackRange + attackExtraRadius;

        if (dist > maxDist)
        {
            if (debugAttack)
            {
                Debug.Log($"Attack missed: dist={dist:F2}, maxDist={maxDist:F2}");
            }
            return;
        }

        Vector3 toPlayer = player.position - origin;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f) return;

        Vector3 dir = toPlayer.normalized;
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > 80f)
        {
            if (debugAttack)
            {
                Debug.Log($"Attack missed by angle: {angle:F1} deg");
            }
            return;
        }

        Health targetHealth = player.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.ApplyDamage(attackDamage);

            if (debugAttack)
            {
                Debug.Log($"PolarBear hit player for {attackDamage} damage");
            }
        }
        else
        {
            if (debugAttack)
            {
                Debug.Log("Player has no Health component");
            }
        }

        // 공격 성공 시점에만 넉백
        EnemyKnockback knock = GetComponent<EnemyKnockback>();
        if (knock != null && targetHealth != null)
        {
            knock.ApplyKnockbackTo(player);
        }
    }

    private void OnDeath(Health h)
    {
        currentState = State.Dead;

        if (animator != null)
        {
            animator.SetBool(isDeadParam, true);
        }

        DropLoot();

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        controller.enabled = false;

        Destroy(gameObject, 10f);
    }

    private void DropLoot()
    {
        if (dropItem == null) return;
        if (dropItem.worldPrefab == null) return;

        int count = Mathf.Max(1, dropCount);
        for (int i = 0; i < count; i++)
        {
            Vector2 rand = Random.insideUnitCircle * dropScatterRadius;
            Vector3 pos = transform.position + new Vector3(rand.x, 0.5f, rand.y);
            Quaternion rot = Quaternion.identity;
            Instantiate(dropItem.worldPrefab, pos, rot);
        }
    }
}
