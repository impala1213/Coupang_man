using UnityEngine;

[RequireComponent(typeof(Health))]
public class MushroomAI : MonoBehaviour
{
    private enum CombatState
    {
        Normal,
        Attacking,
        HitStun,
        Dead
    }

    [Header("Refs")]
    public CreatureChase chase;
    public Health health;
    public Animator animator;

    [Header("Attack (Spore Cone + Particle Only)")]
    public float attackRange = 6f;
    [Range(1f, 179f)] public float attackConeAngle = 70f;
    public float attackCooldown = 3.5f;

    [Tooltip("Seconds after Attack trigger when the spore cloud appears (sync with animation).")]
    public float attackStartDelay = 0.25f;

    [Tooltip("How long the spore cloud stays active.")]
    public float sporeDuration = 1.2f;

    [Tooltip("Damage per second while inside spore cone area.")]
    public int damagePerSecond = 10;

    [Tooltip("Optional: Transform for cone origin. If null, uses this.transform.")]
    public Transform attackOrigin;

    [Tooltip("Optional: Particle prefab to show the cloud (no gameplay logic inside).")]
    public ParticleSystem sporeVfxPrefab;

    [Tooltip("Local offset from origin for spawning VFX.")]
    public Vector3 sporeVfxLocalOffset = new Vector3(0f, 1.0f, 0.8f);

    [Header("Death Spore Burst (Radial)")]
    public bool spawnDeathSpores = true;
    public int deathSporeRays = 12;
    public float deathSporeRange = 5f;
    public float deathSporeDuration = 1.0f;
    public int deathDamagePerSecond = 12;

    [Header("Hit Reaction")]
    [Tooltip("Seconds to lock movement during hit animation.")]
    public float hitLockDuration = 0.4f;

    [Header("Attack Lock")]
    [Tooltip("Freeze duration for the whole attack state (includes recovery). If <= 0, auto = attackStartDelay + sporeDuration.")]
    public float attackLockDuration = 0f;

    [Tooltip("Freeze rotation during attack/hit.")]
    public bool freezeRotationWhileBusy = true;

    [Header("Animation Params")]
    public string speedParam = "Speed";
    public string attackTriggerParam = "Attack";
    public string hitTriggerParam = "Hit";
    public string isDeadParam = "IsDead";

    [Header("Movement Lock")]
    [Tooltip("Freeze CreatureChase movement/rotation while Attacking or Hit.")]
    public bool lockChaseWhileBusy = true;

    [Header("Debug")]
    public bool debugLogs;
    public bool debugGizmos = true;

    private CombatState combatState = CombatState.Normal;

    private float lastAttackTime;

    // Attack timing
    private float attackDelayTimer;
    private float sporeActiveTimer;
    private bool sporeActive;

    // NEW: total attack lock timer (covers recovery frames too)
    private float attackLockTimer;

    private ParticleSystem liveSporeVfx;

    // Hit timing
    private float hitTimer;

    // Cache CreatureChase values
    private bool cachedChase;
    private float cachedWalk;
    private float cachedRun;
    private float cachedRot;

    // Damage tick
    private float damageTickTimer;

    // NEW: shared freeze timer for chase (supports stacking)
    private float busyFreezeTimer;

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

        CacheChaseValues();

        if (health != null)
        {
            health.OnDeath += OnDeath;
        }
    }

    void Update()
    {
        if (health != null && health.IsDead())
            combatState = CombatState.Dead;

        TickBusyFreeze();

        switch (combatState)
        {
            case CombatState.Normal:
                TickNormal();
                break;

            case CombatState.Attacking:
                TickAttacking();
                break;

            case CombatState.HitStun:
                TickHitStun();
                break;

            case CombatState.Dead:
                TickDead();
                break;
        }
    }

    // ----------------------------------------------------
    // Normal: decides when to attack
    // ----------------------------------------------------
    private void TickNormal()
    {
        if (chase == null) return;

        Transform target = chase.VisibleTarget;
        if (target == null)
        {
            if (!IsBusyFrozen())
                RestoreChaseValues();
            return;
        }

        float dist = HorizontalDistance(transform.position, target.position);
        if (dist > attackRange)
        {
            if (!IsBusyFrozen())
                RestoreChaseValues();
            return;
        }

        FaceTowards(target.position, chase.rotationSpeed);

        if (Time.time < lastAttackTime + attackCooldown) return;

        BeginAttack();
    }

    private void BeginAttack()
    {
        lastAttackTime = Time.time;
        combatState = CombatState.Attacking;

        attackDelayTimer = Mathf.Max(0f, attackStartDelay);
        sporeActiveTimer = 0f;
        sporeActive = false;
        damageTickTimer = 0f;

        // NEW: lock timer covers whole attack (including recovery)
        float autoLock = Mathf.Max(0.05f, attackStartDelay + sporeDuration);
        attackLockTimer = (attackLockDuration > 0f) ? attackLockDuration : autoLock;

        if (lockChaseWhileBusy)
            BeginBusyFreeze(attackLockTimer);

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        if (debugLogs) Debug.Log($"[{name}] Mushroom BeginAttack (lock={attackLockTimer:0.00}s)");
    }

    // ----------------------------------------------------
    // Attacking: start cloud after delay, then keep applying DPS in cone
    // ----------------------------------------------------
    private void TickAttacking()
    {
        // Keep facing target during attack
        if (chase != null && chase.VisibleTarget != null)
        {
            FaceTowards(chase.VisibleTarget.position, 10f);
        }

        // Total attack lock countdown
        attackLockTimer -= Time.deltaTime;

        if (!sporeActive)
        {
            attackDelayTimer -= Time.deltaTime;
            if (attackDelayTimer <= 0f)
            {
                StartSporeCloud();
            }
        }
        else
        {
            sporeActiveTimer -= Time.deltaTime;

            ApplySporeDamageTick();

            if (sporeActiveTimer <= 0f)
            {
                EndSporeCloud();
                // IMPORTANT: Do NOT immediately return to Normal.
                // Wait until attackLockTimer finishes to cover recovery frames.
            }
        }

        // End attack state only when total lock timer ends
        if (attackLockTimer <= 0f)
        {
            if (sporeActive)
                EndSporeCloud();

            combatState = CombatState.Normal;
            // Restore is handled by TickBusyFreeze() when freeze ends, but safe to restore here too.
            if (!IsBusyFrozen())
                RestoreChaseValues();
        }
    }

    private void StartSporeCloud()
    {
        sporeActive = true;
        sporeActiveTimer = Mathf.Max(0.1f, sporeDuration);

        if (sporeVfxPrefab != null)
        {
            Transform originT = attackOrigin != null ? attackOrigin : transform;
            Vector3 spawnPos = originT.TransformPoint(sporeVfxLocalOffset);
            Quaternion rot = originT.rotation;

            liveSporeVfx = Instantiate(sporeVfxPrefab, spawnPos, rot);
            liveSporeVfx.Play();
            Destroy(liveSporeVfx.gameObject, sporeActiveTimer + 0.5f);
        }

        if (debugLogs) Debug.Log($"[{name}] SporeCloud ON");
    }

    private void EndSporeCloud()
    {
        sporeActive = false;

        // Optional: stop the live VFX immediately (it will also auto-destroy)
        if (liveSporeVfx != null)
        {
            liveSporeVfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            liveSporeVfx = null;
        }

        if (debugLogs) Debug.Log($"[{name}] SporeCloud OFF");
    }

    // DPS tick: tick 4 times per second (0.25s)
    private void ApplySporeDamageTick()
    {
        damageTickTimer -= Time.deltaTime;
        if (damageTickTimer > 0f) return;
        damageTickTimer = 0.25f;

        if (chase == null) return;
        Transform target = chase.VisibleTarget;
        if (target == null) return;

        Vector3 origin = (attackOrigin != null ? attackOrigin.position : transform.position);
        Vector3 to = target.position - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist > attackRange) return;
        if (dist < 0.001f) return;

        float angle = Vector3.Angle(transform.forward, to.normalized);
        if (angle > attackConeAngle * 0.5f) return;

        Health playerHealth = target.GetComponent<Health>();
        if (playerHealth != null && !playerHealth.IsDead())
        {
            float tickSeconds = 0.25f;
            int tickDamage = Mathf.Max(1, Mathf.RoundToInt(damagePerSecond * tickSeconds));
            playerHealth.ApplyDamage(tickDamage);

            if (debugLogs) Debug.Log($"[{name}] Spore tick damage={tickDamage}");
        }
    }

    // ----------------------------------------------------
    // Hit reaction (call this when the mushroom takes damage)
    // ----------------------------------------------------
    public void TriggerHit()
    {
        if (combatState == CombatState.Dead) return;

        combatState = CombatState.HitStun;

        if (sporeActive)
        {
            EndSporeCloud();
            sporeActive = false;
        }

        hitTimer = Mathf.Max(0f, hitLockDuration);

        if (lockChaseWhileBusy)
            BeginBusyFreeze(hitTimer);

        if (animator != null && !string.IsNullOrEmpty(hitTriggerParam))
        {
            animator.ResetTrigger(hitTriggerParam);
            animator.SetTrigger(hitTriggerParam);
        }

        if (debugLogs) Debug.Log($"[{name}] HitStun (lock={hitTimer:0.00}s)");
    }

    private void TickHitStun()
    {
        hitTimer -= Time.deltaTime;
        if (hitTimer <= 0f)
        {
            combatState = CombatState.Normal;
            if (!IsBusyFrozen())
                RestoreChaseValues();
        }
    }

    // ----------------------------------------------------
    // Death
    // ----------------------------------------------------
    private void OnDeath(Health h)
    {
        combatState = CombatState.Dead;

        // Freeze indefinitely
        if (lockChaseWhileBusy)
            BeginBusyFreeze(-1f);

        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
            animator.SetBool(isDeadParam, true);

        if (spawnDeathSpores)
        {
            StartDeathSporeBurst();
        }

        if (debugLogs) Debug.Log($"[{name}] Dead");
    }

    private void TickDead()
    {
        // Do nothing.
    }

    private void StartDeathSporeBurst()
    {
        StartCoroutine(DeathSporeRoutine());
    }

    private System.Collections.IEnumerator DeathSporeRoutine()
    {
        float t = Mathf.Max(0.1f, deathSporeDuration);
        float tick = 0.25f;

        while (t > 0f)
        {
            t -= tick;

            Transform target = (chase != null) ? chase.VisibleTarget : null;
            if (target == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null) target = playerObj.transform;
            }

            if (target != null)
            {
                float d = HorizontalDistance(transform.position, target.position);
                if (d <= deathSporeRange)
                {
                    Health th = target.GetComponent<Health>();
                    if (th != null && !th.IsDead())
                    {
                        int tickDamage = Mathf.Max(1, Mathf.RoundToInt(deathDamagePerSecond * tick));
                        th.ApplyDamage(tickDamage);
                    }
                }
            }

            yield return new WaitForSeconds(tick);
        }
    }

    // ----------------------------------------------------
    // Busy Freeze (local) - does NOT require changes in CreatureChase
    // ----------------------------------------------------
    private void BeginBusyFreeze(float durationSeconds)
    {
        if (chase == null) return;
        if (!cachedChase) CacheChaseValues();

        if (durationSeconds < 0f)
        {
            busyFreezeTimer = float.PositiveInfinity;
        }
        else
        {
            busyFreezeTimer = Mathf.Max(busyFreezeTimer, durationSeconds);
        }

        ApplyFreezeNow();
    }

    private void TickBusyFreeze()
    {
        if (chase == null) return;

        if (busyFreezeTimer == float.PositiveInfinity)
        {
            ApplyFreezeNow();
            return;
        }

        if (busyFreezeTimer > 0f)
        {
            busyFreezeTimer -= Time.deltaTime;
            ApplyFreezeNow();
            return;
        }

        // Freeze ended
        if (IsBusyState())
        {
            // Still in Attacking/HitStun but timer hit 0: keep frozen as safety
            ApplyFreezeNow();
            return;
        }

        RestoreChaseValues();
    }

    private void ApplyFreezeNow()
    {
        if (chase == null) return;

        chase.walkSpeed = 0f;
        chase.runSpeed = 0f;
        if (freezeRotationWhileBusy)
            chase.rotationSpeed = 0f;
    }

    private bool IsBusyFrozen()
    {
        return busyFreezeTimer > 0f || busyFreezeTimer == float.PositiveInfinity;
    }

    private bool IsBusyState()
    {
        return combatState == CombatState.Attacking || combatState == CombatState.HitStun || combatState == CombatState.Dead;
    }

    // ----------------------------------------------------
    // CreatureChase cache helpers
    // ----------------------------------------------------
    private void CacheChaseValues()
    {
        if (chase == null || cachedChase) return;
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

    // ----------------------------------------------------
    // Utilities
    // ----------------------------------------------------
    private void FaceTowards(Vector3 target, float rotSpeed)
    {
        Vector3 to = target - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion q = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, rotSpeed * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ----------------------------------------------------
    // Gizmos
    // ----------------------------------------------------
    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        Vector3 origin = (attackOrigin != null ? attackOrigin.position : transform.position);
        float half = attackConeAngle * 0.5f;

        Gizmos.color = new Color(0f, 1f, 0f, 0.7f);
        Gizmos.DrawWireSphere(origin, attackRange);

        Vector3 leftDir = Quaternion.Euler(0f, -half, 0f) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0f, half, 0f) * transform.forward;

        Gizmos.color = new Color(0f, 1f, 0f, 1f);
        Gizmos.DrawLine(origin, origin + leftDir.normalized * attackRange);
        Gizmos.DrawLine(origin, origin + rightDir.normalized * attackRange);
    }
}
