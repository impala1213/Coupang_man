using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spore cloud AoE that applies damage over time.
/// - No projectile movement.
/// - Uses OverlapSphere + cone angle filter for "fan" damage.
/// - Visuals are handled by ParticleSystem on the same prefab.
/// </summary>
public class SporeCloudArea : MonoBehaviour
{
    [Header("Lifetime")]
    public float duration = 2.5f;

    [Header("Area")]
    public float radius = 6f;
    public bool useCone = true;
    [Tooltip("Half angle of the cone in degrees (e.g., 25 means 50-degree total cone).")]
    public float coneHalfAngle = 25f;

    [Header("Damage Over Time")]
    public float tickInterval = 0.35f;
    public int damagePerTick = 4;
    public LayerMask hitMask = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Target")]
    public string targetTag = "Player";

    [Header("Owner")]
    public GameObject owner;

    // internal
    private float lifeTimer;
    private float tickTimer;

    private readonly Collider[] overlapBuffer = new Collider[32];
    private readonly HashSet<Health> damagedThisTick = new HashSet<Health>();

    private ParticleSystem ps;

    private void Awake()
    {
        lifeTimer = Mathf.Max(0.05f, duration);
        tickTimer = Mathf.Max(0.01f, tickInterval);

        ps = GetComponentInChildren<ParticleSystem>();
        // Optional: Let the particle system auto stop, we destroy by timer anyway.
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = Mathf.Max(0.01f, tickInterval);
            ApplyDamageTick();
        }
    }

    private void ApplyDamageTick()
    {
        if (damagePerTick <= 0) return;

        damagedThisTick.Clear();

        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer, hitMask, triggerInteraction);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == null) continue;

            // Filter by tag first for speed (works if player collider has the Player tag).
            if (!string.IsNullOrEmpty(targetTag) && !c.CompareTag(targetTag))
                continue;

            // Ignore owner
            if (owner != null && c.transform.IsChildOf(owner.transform))
                continue;

            Transform t = c.transform;

            // Cone filter
            if (useCone)
            {
                Vector3 toTarget = t.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) continue;

                float angle = Vector3.Angle(transform.forward, toTarget.normalized);
                if (angle > coneHalfAngle) continue;
            }

            Health hp = c.GetComponentInParent<Health>();
            if (hp == null) continue;
            if (hp.IsDead()) continue;

            // Prevent multi-hit if multiple colliders belong to the same target.
            if (!damagedThisTick.Add(hp))
                continue;

            hp.ApplyDamage(damagePerTick);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);

        if (useCone)
        {
            Vector3 f = transform.forward;
            Quaternion leftRot = Quaternion.AngleAxis(-coneHalfAngle, Vector3.up);
            Quaternion rightRot = Quaternion.AngleAxis(coneHalfAngle, Vector3.up);
            Gizmos.DrawLine(transform.position, transform.position + (leftRot * f) * radius);
            Gizmos.DrawLine(transform.position, transform.position + (rightRot * f) * radius);
        }
    }
}
