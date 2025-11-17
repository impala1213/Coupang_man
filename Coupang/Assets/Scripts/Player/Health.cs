using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    public event Action<Health> OnDeath;
    public event Action<Health, int> OnDamaged;

    private bool isDead;

    void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0) return;
        if (isDead) return;

        currentHealth -= amount;
        if (currentHealth < 0) currentHealth = 0;

        OnDamaged?.Invoke(this, amount);

        if (currentHealth == 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (isDead) return;

        currentHealth += amount;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
    }

    private void Die()
    {
        if (isDead) return;

        isDead = true;
        OnDeath?.Invoke(this);
    }

    public bool IsDead()
    {
        return isDead;
    }
}
