using UnityEngine;

[RequireComponent(typeof(Health))]
public class BeastAI : MonoBehaviour
{
    private enum CombatState
    {
        Normal,
        Attacking,
        Dead
    }

    [Header("Refs")]
    public CreatureChase chase;
    public Health health;
    public Animator animator;

    [Header("Combat")]
    public float attackRange = 2.5f;
    public float attackCooldown = 1.5f;
    public int attackDamage = 20;

    [Tooltip("Optional hit origin. If null, uses transform.position.")]
    public Transform attackOrigin;

    [Tooltip("Extra range margin.")]
    public float attackExtraRadius = 0.5f;

    [Tooltip("Max attack cone angle (degrees) from forward.")]
    public float attackAngle = 80f;

    [Header("Combat Timing")]
    [Tooltip("Seconds after starting attack to apply hit.")]
    public float attackHitDelay = 0.4f;

    [Tooltip("Seconds to lock movement during attack (set to your anim length).")]
    public float attackLockDuration = 0.9f;

    [Header("Attack Movement Lock")]
    [Tooltip("If true, stop CreatureChase movement during attack.")]
    public bool lockChaseMovementDuringAttack = true;

    [Tooltip("Rotation speed while attacking (only rotates, no translation).")]
    public float attackRotationSpeed = 10f;

    [Header("Drops")]
    public ItemDefinition dropItem;
    public int dropCount = 1;
    public float dropScatterRadius = 1.5f;

    [Header("Animation")]
    public string isDeadParam = "IsDead";
    public string attackTriggerParam = "Attack";

    [Header("Debug")]
    public bool debugCombat;

    private CombatState combatState = CombatState.Normal;

    private float lastAttackTime;

    private bool pendingHit;
    private bool hitApplied;
    private float hitTimer;
    private float lockTimer;

    private Transform attackTargetRoot;

    private float cachedWalkSpeed;
    private float cachedRunSpeed;
    private bool cachedSpeeds;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (chase == null) chase = GetComponent<CreatureChase>();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        if (chase == null)
            Debug.LogError("[BeastAI] CreatureChase component is required on the same GameObject.");

        if (health != null)
            health.OnDeath += OnDeath;

        // Avoid root motion fighting movement
        if (animator != null) animator.applyRootMotion = false;
    }

    void Update()
    {
        if (combatState == CombatState.Dead) return;

        if (health != null && health.IsDead())
        {
            EnterDead();
            return;
        }

        switch (combatState)
        {
            case CombatState.Normal:
                TickNormal();
                break;
            case CombatState.Attacking:
                TickAttacking();
                break;
        }
    }

    private void TickNormal()
    {
        if (chase == null) return;

        // Only attack when target is currently visible (your rule)
        Transform target = chase.VisibleTarget;
        if (target == null) return;

        // Range check (horizontal)
        Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position;
        float dist = HorizontalDistance(origin, target.position);
        float maxDist = attackRange + attackExtraRadius;
        if (dist > maxDist) return;

        // Cooldown
        if (Time.time < lastAttackTime + attackCooldown) return;

        StartAttack(target);
    }

    private void StartAttack(Transform targetRoot)
    {
        lastAttackTime = Time.time;
        combatState = CombatState.Attacking;

        attackTargetRoot = targetRoot;

        pendingHit = true;
        hitApplied = false;
        hitTimer = Mathf.Max(0f, attackHitDelay);

        lockTimer = Mathf.Max(0.01f, attackLockDuration);

        if (debugCombat) Debug.Log("[BeastAI] StartAttack");

        // Trigger anim
        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        // Optional: lock CreatureChase movement
        if (lockChaseMovementDuringAttack && chase != null)
            CacheAndStopChaseSpeeds();
    }

    private void TickAttacking()
    {
        if (chase == null) return;

        // Rotate to visible target (or last target position) while attacking, but do not translate
        Transform vis = chase.VisibleTarget;
        Transform faceTarget = (vis != null) ? vis : attackTargetRoot;

        if (faceTarget != null)
        {
            Vector3 to = faceTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, attackRotationSpeed * Time.deltaTime);
            }
        }

        // Hit timing
        if (pendingHit && !hitApplied)
        {
            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
            {
                pendingHit = false;
                hitApplied = true;

                TryApplyHitAtImpactTime();
            }
        }

        // Attack lock end
        lockTimer -= Time.deltaTime;
        if (lockTimer <= 0f)
        {
            if (lockChaseMovementDuringAttack && chase != null)
                RestoreChaseSpeeds();

            combatState = CombatState.Normal;
        }
    }

    private void TryApplyHitAtImpactTime()
    {
        if (health == null || health.IsDead()) return;
        if (chase == null) return;

        // Must still be visible at hit time (your rule)
        if (chase.VisibleTarget == null) return;

        // Ensure it is the same target root
        if (attackTargetRoot == null) return;
        if (chase.VisibleTarget != attackTargetRoot) return;

        Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position;

        // Distance check (horizontal)
        float dist = HorizontalDistance(origin, attackTargetRoot.position);
        float maxDist = attackRange + attackExtraRadius;
        if (dist > maxDist) return;

        // Angle check (horizontal)
        Vector3 toTarget = attackTargetRoot.position - origin;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        float angle = Vector3.Angle(transform.forward, toTarget.normalized);
        if (angle > attackAngle) return;

        // Damage
        Health targetHealth = attackTargetRoot.GetComponent<Health>();
        if (targetHealth != null && !targetHealth.IsDead())
        {
            targetHealth.ApplyDamage(attackDamage);

            // Knockback (existing pipeline)
            EnemyKnockback knock = GetComponent<EnemyKnockback>();
            if (knock != null)
                knock.ApplyKnockbackTo(attackTargetRoot);

            if (debugCombat) Debug.Log("[BeastAI] Hit applied");
        }
    }

    private void CacheAndStopChaseSpeeds()
    {
        if (chase == null) return;
        if (!cachedSpeeds)
        {
            cachedWalkSpeed = chase.walkSpeed;
            cachedRunSpeed = chase.runSpeed;
            cachedSpeeds = true;
        }

        chase.walkSpeed = 0f;
        chase.runSpeed = 0f;
    }

    private void RestoreChaseSpeeds()
    {
        if (chase == null) return;
        if (!cachedSpeeds) return;

        chase.walkSpeed = cachedWalkSpeed;
        chase.runSpeed = cachedRunSpeed;
    }

    private void OnDeath(Health h)
    {
        EnterDead();
    }

    private void EnterDead()
    {
        if (combatState == CombatState.Dead) return;
        combatState = CombatState.Dead;

        // Stop chase system permanently
        if (chase != null)
            chase.Disable();

        // Stop pending attack
        pendingHit = false;
        hitApplied = true;

        // Animator
        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
        {
            animator.SetBool(isDeadParam, true);
        }

        DropLoot();

        // Optional: disable non-CC collider so corpse won't block (keep CC if you want gravity/falling)
        Collider col = GetComponent<Collider>();
        if (col != null && !(col is CharacterController))
            col.enabled = false;

        if (debugCombat) Debug.Log("[BeastAI] Dead");
    }

    private void DropLoot()
    {
        if (dropItem == null) return;
        if (dropItem.worldPrefab == null) return;

        int count = Mathf.Max(1, dropCount);
        for (int i = 0; i < count; i++)
        {
            Vector2 r = Random.insideUnitCircle * dropScatterRadius;
            Vector3 pos = transform.position + new Vector3(r.x, 0.5f, r.y);
            Instantiate(dropItem.worldPrefab, pos, Quaternion.identity);
        }
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
