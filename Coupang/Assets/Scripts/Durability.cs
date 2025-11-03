using UnityEngine;

public class Durability : MonoBehaviour
{
    public int max = 100;
    public int current = 100;

    public bool IsBroken => current <= 0;

    public void InitFromData(ItemData data)
    {
        max = data ? data.durabilityMax : 100;
        current = max;
    }

    public void ApplyDamage(int amount)
    {
        current = Mathf.Max(0, current - Mathf.Abs(amount));
        if (IsBroken) OnBroken();
    }

    protected virtual void OnBroken()
    {
        // Visual or behavior on break (fx, disable mesh, etc.)
        // Placeholder: destroy
        Destroy(gameObject);
    }
}
