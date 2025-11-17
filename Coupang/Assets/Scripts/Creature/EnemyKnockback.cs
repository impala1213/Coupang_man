// Assets/Scripts/Combat/EnemyKnockback.cs
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyKnockback : MonoBehaviour
{
    [Header("Knockback")]
    public bool enableKnockback = true;
    public float knockbackForce = 10f;
    public bool spillCargo = true;

    public void ApplyKnockbackTo(Transform target)
    {
        if (!enableKnockback || target == null) return;

        PlayerController pc = target.GetComponent<PlayerController>();
        if (pc == null) return;

        pc.ApplyKnockback(transform.position, knockbackForce, spillCargo);
    }
}
