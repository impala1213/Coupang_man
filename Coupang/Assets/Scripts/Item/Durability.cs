using UnityEngine;

[DisallowMultipleComponent]
public class Durability : MonoBehaviour
{
    [Header("Durability")]
    public int max = 100;
    public int current = 100;

    public bool IsBroken => current <= 0;

    /// <summary>
    /// Initialize durability directly.
    /// </summary>
    public void Init(int maxValue, int currentValue = -1)
    {
        max = Mathf.Max(1, maxValue);
        current = (currentValue >= 0) ? Mathf.Clamp(currentValue, 0, max) : max;
    }

    /// <summary>
    /// Apply damage (positive value reduces current).
    /// </summary>
    public void ApplyDamage(int amount)
    {
        if (amount == 0) return;
        current = Mathf.Clamp(current - Mathf.Abs(amount), 0, max);
        if (IsBroken) OnBroken();
    }

    /// <summary>
    /// Apply repair (positive value increases current).
    /// </summary>
    public void Repair(int amount)
    {
        if (amount == 0) return;
        current = Mathf.Clamp(current + Mathf.Abs(amount), 0, max);
    }

    /// <summary>
    /// Hook for break behavior (VFX/SFX/disable mesh etc).
    /// Default: destroy this GameObject.
    /// </summary>
    protected virtual void OnBroken()
    {
        Destroy(gameObject);
    }
}
