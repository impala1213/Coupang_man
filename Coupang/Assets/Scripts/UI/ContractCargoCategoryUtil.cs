using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Utility for summarizing a DeliveryContractDefinition into category -> count.
/// </summary>
public static class ContractCargoCategoryUtil
{
    // Display order for UI.
    public static readonly ItemCategory[] DisplayOrder =
    {
        ItemCategory.General,
        ItemCategory.Explosive,
        ItemCategory.Bio,
        ItemCategory.Toxic,
        ItemCategory.Fragile,
        ItemCategory.None
    };

    public static void BuildCategoryCounts(DeliveryContractDefinition contract, Dictionary<ItemCategory, int> outCounts)
    {
        if (outCounts == null) return;
        outCounts.Clear();

        if (contract == null || contract.requiredItems == null)
            return;

        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var r = contract.requiredItems[i];
            if (r == null || r.item == null) continue;

            ItemCategory cat = r.item.category;
            int qty = Mathf.Max(1, r.requiredQty);

            if (outCounts.ContainsKey(cat))
                outCounts[cat] += qty;
            else
                outCounts.Add(cat, qty);
        }
    }
}
