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

    private ParticleSystem liveSporeVfx;

    // Hit timing
    private float hitTimer;

    // Cache CreatureChase values
    private bool cachedChase;
    private float cachedWalk;
    private float cachedRun;
    private float cachedRot;

    // Track damage tick
    private float damageTickTimer;

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
            // If your Health has OnDamaged/OnHit event, hook here.
            // Otherwise, you can call TriggerHit() manually from wherever damage is applied.
        }
    }

    void Update()
    {
        if (health != null && health.IsDead())
            combatState = CombatState.Dead;

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

        // Only attack when player is visible (CreatureChase already handles LOS + investigate/search)
        Transform target = chase.VisibleTarget;
        if (target == null)
        {
            RestoreChaseValues();
            return;
        }

        // Check distance
        float dist = HorizontalDistance(transform.position, target.position);
        if (dist > attackRange)
        {
            RestoreChaseValues();
            return;
        }

        // Face target while about to attack (optional)
        FaceTowards(target.position, chase.rotationSpeed);

        // Cooldown
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

        if (lockChaseWhileBusy)
            FreezeChase();

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        if (debugLogs) Debug.Log($"[{name}] Mushroom BeginAttack");
    }

    // ----------------------------------------------------
    // Attacking: start cloud after delay, then keep applying DPS in cone
    // ----------------------------------------------------
    private void TickAttacking()
    {
        if (chase != null && chase.VisibleTarget != null)
        {
            // During attack, allow only facing, no translation (we froze chase)
            FaceTowards(chase.VisibleTarget.position, 10f);
        }

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
                combatState = CombatState.Normal;
                RestoreChaseValues();
            }
        }
    }

    private void StartSporeCloud()
    {
        sporeActive = true;
        sporeActiveTimer = Mathf.Max(0.1f, sporeDuration);

        // VFX only
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
        if (debugLogs) Debug.Log($"[{name}] SporeCloud OFF");
    }

    // DPS tick: to avoid per-frame damage spam, tick 4 times per second (0.25s)
    private void ApplySporeDamageTick()
    {
        damageTickTimer -= Time.deltaTime;
        if (damageTickTimer > 0f) return;
        damageTickTimer = 0.25f;

        if (chase == null) return;
        Transform target = chase.VisibleTarget;
        if (target == null) return;

        // Cone check (range + angle)
        Vector3 origin = (attackOrigin != null ? attackOrigin.position : transform.position);
        Vector3 to = target.position - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist > attackRange) return;
        if (dist < 0.001f) return;

        float angle = Vector3.Angle(transform.forward, to.normalized);
        if (angle > attackConeAngle * 0.5f) return;

        // Apply damage (Health on player)
        Health playerHealth = target.GetComponent<Health>();
        if (playerHealth != null && !playerHealth.IsDead())
        {
            // Convert DPS to tick damage
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

        // If you don't want hit to interrupt attack, you can early return when Attacking.
        // Here: hit interrupts attack.
        combatState = CombatState.HitStun;

        // Stop cloud immediately
        if (sporeActive)
        {
            EndSporeCloud();
            sporeActive = false;
        }

        hitTimer = Mathf.Max(0f, hitLockDuration);

        if (lockChaseWhileBusy)
            FreezeChase();

        if (animator != null && !string.IsNullOrEmpty(hitTriggerParam))
        {
            animator.ResetTrigger(hitTriggerParam);
            animator.SetTrigger(hitTriggerParam);
        }

        if (debugLogs) Debug.Log($"[{name}] HitStun");
    }

    private void TickHitStun()
    {
        hitTimer -= Time.deltaTime;
        if (hitTimer <= 0f)
        {
            combatState = CombatState.Normal;
            RestoreChaseValues();
        }
    }

    // ----------------------------------------------------
    // Death
    // ----------------------------------------------------
    private void OnDeath(Health h)
    {
        combatState = CombatState.Dead;
        FreezeChaseHard();

        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
            animator.SetBool(isDeadParam, true);

        if (spawnDeathSpores)
        {
            // VFX-only radial burst (optional) + gameplay radial cone slices
            StartDeathSporeBurst();
        }

        if (debugLogs) Debug.Log($"[{name}] Dead");
    }

    private void TickDead()
    {
        // Stay dead pose. Do nothing.
    }

    // ----------------------------------------------------
    // Death spore burst: applies DPS around in radial directions for a short time
    // (No projectiles. We approximate "around" by checking targets in radius & applying damage.)
    // ----------------------------------------------------
    private void StartDeathSporeBurst()
    {
        // Simple implementation: for the duration, apply radial tick damage to any player in range.
        // If you have multiple players, you can change this to OverlapSphere for all players.
        StartCoroutine(DeathSporeRoutine());
    }

    private System.Collections.IEnumerator DeathSporeRoutine()
    {
        float t = Mathf.Max(0.1f, deathSporeDuration);
        float tick = 0.25f;

        while (t > 0f)
        {
            t -= tick;

            // Damage any visible target in radius (or any player in radius if you prefer)
            Transform target = (chase != null) ? chase.VisibleTarget : null;

            // If not currently visible, still allow death spores to hit if player is near:
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
    // CreatureChase speed lock helpers
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

    private void FreezeChase()
    {
        if (chase == null) return;
        if (!cachedChase) CacheChaseValues();

        chase.walkSpeed = 0f;
        chase.runSpeed = 0f;
        chase.rotationSpeed = 0f;
    }

    private void FreezeChaseHard()
    {
        if (chase == null) return;

        // Make sure it never resumes detection/movement
        chase.detectionRadius = 0f;
        chase.playerMask = 0;
        chase.walkSpeed = 0f;
        chase.runSpeed = 0f;
        chase.rotationSpeed = 0f;
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

        // Attack cone gizmo
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
