using UnityEngine;

/// <summary>
/// Weighted random selection of delivery contracts.
/// </summary>
[CreateAssetMenu(menuName = "Economy/Delivery Contract Directory", fileName = "DeliveryContractDirectory")]
public class DeliveryContractDirectory : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public DeliveryContractDefinition contract;
        public int weight = 1;
    }

    public Entry[] entries;

    public DeliveryContractDefinition GetRandomContract()
    {
        if (entries == null || entries.Length == 0)
            return null;

        int total = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || e.contract == null || e.weight <= 0) continue;
            total += e.weight;
        }

        if (total <= 0)
            return null;

        int pick = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || e.contract == null || e.weight <= 0) continue;
            acc += e.weight;
            if (pick < acc)
                return e.contract;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].contract != null)
                return entries[i].contract;
        }

        return null;
    }
}
