using UnityEngine;

/// <summary>
/// Defines a delivery contract (mission) used by Ship monitor + DropZone payout.
/// New rules (project):
/// - Mission cargo is explicitly defined here (requiredItems).
/// - Max payout = sum(requiredQty * item.baseValue).
/// - Partial delivery is allowed (pays proportionally, capped by requiredQty per item).
/// - Broken mission cargo is not accepted by DropZone.
/// </summary>
[CreateAssetMenu(menuName = "Economy/Delivery Contract", fileName = "DeliveryContract")]
public class DeliveryContractDefinition : ScriptableObject
{
    [Header("Identity")]
    public string contractId;
    public string displayName;

    [System.Serializable]
    public class RequiredItem
    {
        public ItemDefinition item;
        [Min(1)] public int requiredQty = 1;
    }

    [Header("Mission Cargo")]
    [Tooltip("List of items required for this mission.")]
    public RequiredItem[] requiredItems;

    [Header("DropZone Activation")]
    [Tooltip("Minimum number of valid items required to allow DropZone activation (hold button).")]
    public int minItemsToActivate = 1;

    public int ComputeMaxReward()
    {
        if (requiredItems == null || requiredItems.Length == 0)
            return 0;

        long sum = 0;
        for (int i = 0; i < requiredItems.Length; i++)
        {
            var r = requiredItems[i];
            if (r == null || r.item == null) continue;

            int qty = Mathf.Max(1, r.requiredQty);
            int value = Mathf.Max(0, r.item.baseValue);
            sum += (long)qty * value;
        }

        if (sum > int.MaxValue) return int.MaxValue;
        return (int)Mathf.Max(0, sum);
    }
}
