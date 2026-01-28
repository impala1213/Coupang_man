using UnityEngine;

/// <summary>
/// Defines payout multipliers per ItemCategory.
/// Reward per item = baseValue * multiplier.
/// </summary>
[CreateAssetMenu(menuName = "Economy/Delivery Pricing Table", fileName = "DeliveryPricingTable")]
public class DeliveryPricingTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public ItemCategory category = ItemCategory.None;
        [Min(0f)] public float multiplier = 1f;
    }

    [Tooltip("Multiplier used when a category does not appear in entries.")]
    [Min(0f)]
    public float defaultMultiplier = 1f;

    public Entry[] entries;

    public float GetMultiplier(ItemCategory category)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                if (e.category == category)
                    return Mathf.Max(0f, e.multiplier);
            }
        }

        return Mathf.Max(0f, defaultMultiplier);
    }

    public int Evaluate(ItemDefinition def, int stackCount = 1)
    {
        if (def == null) return 0;
        int count = Mathf.Max(1, stackCount);

        float mul = GetMultiplier(def.category);
        float value = def.baseValue * mul;
        int per = Mathf.RoundToInt(value);
        return Mathf.Max(0, per * count);
    }
}
