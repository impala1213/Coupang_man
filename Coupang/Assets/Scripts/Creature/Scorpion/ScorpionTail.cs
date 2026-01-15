using System;
using UnityEngine;

/// <summary>
/// Tail weak point with its own HP.
/// - Usually only vulnerable while scorpion is shaking.
/// - When broken, notifies ScorpionAI to release the player.
/// </summary>
[DisallowMultipleComponent]
public class ScorpionTail : MonoBehaviour
{
    [Header("HP")]
    public int maxHp = 40;
    public int currentHp = 40;

    [Header("Damage Gate")]
    public bool vulnerable;

    [Header("Debug")]
    public bool debugLogs;

    public event Action<ScorpionTail> OnBroken;

    void Awake()
    {
        maxHp = Mathf.Max(1, maxHp);
        if (currentHp <= 0) currentHp = maxHp;
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
    }

    public void ResetToFull()
    {
        currentHp = maxHp;
    }

    public void SetVulnerable(bool on)
    {
        vulnerable = on;
    }

    public void ApplyDamage(int amount)
    {
        if (!vulnerable) return;
        if (amount <= 0) return;
        if (currentHp <= 0) return;

        currentHp -= amount;
        if (currentHp < 0) currentHp = 0;

        if (debugLogs) Debug.Log($"[ScorpionTail] -{amount} => {currentHp}/{maxHp}");

        if (currentHp == 0)
        {
            if (debugLogs) Debug.Log("[ScorpionTail] Broken!");
            OnBroken?.Invoke(this);
        }
    }
}
