using UnityEngine;

/// <summary>
/// Attach this to any creature that has Health.
/// Your damage system should call NotifyDamagedBy(attacker) before ApplyDamage.
/// When the creature dies, the last attacker gets a kill credit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class CreatureKillCredit : MonoBehaviour
{
    [Header("Credit Rules")]
    [Tooltip("If no damage from a player within this time before death, no credit is given.")]
    public float creditTimeoutSeconds = 10f;

    private Health health;

    private Transform lastDamager;
    private float lastDamagerTime;

    void Awake()
    {
        health = GetComponent<Health>();
        health.OnDeath += OnDeath;
    }

    void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= OnDeath;
    }

    /// <summary>
    /// Call this when this creature is damaged by someone.
    /// Put attacker as the PLAYER root transform (or any child; we'll root it).
    /// </summary>
    public void NotifyDamagedBy(Transform attacker)
    {
        if (attacker == null) return;

        // Root to avoid child hitboxes being treated as separate actors
        lastDamager = attacker.root;
        lastDamagerTime = Time.time;
    }

    private void OnDeath(Health h)
    {
        if (lastDamager == null) return;

        if (creditTimeoutSeconds > 0f && (Time.time - lastDamagerTime) > creditTimeoutSeconds)
            return;

        // Give kill credit to the killer if they have the mark component
        var mark = lastDamager.GetComponent<WraithVengeanceMark>();
        if (mark != null)
        {
            mark.RegisterCreatureKill();
        }
    }
}
