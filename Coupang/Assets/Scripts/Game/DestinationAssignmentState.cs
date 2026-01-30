using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Cross-scene runtime state for "multi destination" assignments.
/// Created on Ship monitor confirmation (pre-split), then consumed on Planet spawn.
/// 
/// Split rule (requested):
/// - Expand contract.requiredItems into a unit list (each item counts as 1).
/// - Randomize, then distribute into N destinations such that total value is balanced (greedy by value).
/// - Each destination gets a runtime "partial contract" ScriptableObject instance.
/// 
/// Completion rule:
/// - Each destination can be completed with partial delivery (reward can be 0).
/// - Rewards are recorded and later summarized when returning to ship.
/// </summary>
public static class DestinationAssignmentState
{
    public const int DefaultDestinationCount = 3;

    private static DeliveryContractDefinition _sourceContract;
    private static int _destCount;

    private static readonly List<DeliveryContractDefinition> _partialContracts = new List<DeliveryContractDefinition>(8);
    private static readonly List<Dictionary<ItemDefinition, int>> _quotaByDef = new List<Dictionary<ItemDefinition, int>>(8);

    private static int[] _rewardByDest;
    private static bool[] _completed;

    public static bool HasAssignment => _sourceContract != null && _destCount > 0 && _partialContracts.Count == _destCount;

    public static int DestinationCount => _destCount;

    public static bool Matches(DeliveryContractDefinition contract, int destCount)
    {
        if (!HasAssignment) return false;
        if (destCount != _destCount) return false;

        // If we have no source, only a reference match can work.
        if (_sourceContract == null) return false;
        if (ReferenceEquals(contract, _sourceContract)) return true;

        // Be tolerant across scene loads / asset reloads: if contractId matches, treat as the same contract.
        if (contract != null &&
            !string.IsNullOrEmpty(contract.contractId) &&
            !string.IsNullOrEmpty(_sourceContract.contractId) &&
            contract.contractId == _sourceContract.contractId)
            return true;

        return false;
    }

    /// <summary>
    /// Builds/overwrites the assignment. Intended to be called when the player confirms a destination on the ship monitor.
    /// </summary>
    public static void Prepare(DeliveryContractDefinition contract, int destCount)
    {
        _sourceContract = contract;
        _destCount = Mathf.Max(1, destCount);

        _partialContracts.Clear();
        _quotaByDef.Clear();

        _rewardByDest = new int[_destCount];
        _completed = new bool[_destCount];

        if (contract == null || contract.requiredItems == null || contract.requiredItems.Length == 0)
        {
            // Still create empty partial contracts so planet spawn doesn't break.
            for (int i = 0; i < _destCount; i++)
            {
                _quotaByDef.Add(new Dictionary<ItemDefinition, int>(0));
                _partialContracts.Add(CreatePartialContract(contract, i, _quotaByDef[i]));
            }
            return;
        }

        // 1) Expand to unit pool
        List<ItemDefinition> units = new List<ItemDefinition>(64);
        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var r = contract.requiredItems[i];
            if (r == null || r.item == null) continue;

            int q = Mathf.Max(0, r.requiredQty);
            for (int k = 0; k < q; k++)
                units.Add(r.item);
        }

        // Randomize unit pool (so ties & same-value items distribute differently each run).
        for (int i = units.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (units[i], units[j]) = (units[j], units[i]);
        }

        // 2) Sort high->low value with randomized tiebreak (stable randomness from shuffle above)
        units.Sort((a, b) =>
        {
            int va = a != null ? a.baseValue : 0;
            int vb = b != null ? b.baseValue : 0;
            // descending
            int c = vb.CompareTo(va);
            if (c != 0) return c;
            // keep shuffled order for ties (sort isn't stable, so do a small random tiebreak)
            return Random.Range(-1, 2);
        });

        // 3) Greedy balance by total value
        long[] totalValue = new long[_destCount];
        for (int i = 0; i < _destCount; i++)
            _quotaByDef.Add(new Dictionary<ItemDefinition, int>(16));

        for (int u = 0; u < units.Count; u++)
        {
            var def = units[u];
            if (def == null) continue;

            // pick destination with minimum total value (random among ties)
            long min = long.MaxValue;
            int tieCount = 0;
            int chosen = 0;

            for (int i = 0; i < _destCount; i++)
            {
                long v = totalValue[i];
                if (v < min)
                {
                    min = v;
                    chosen = i;
                    tieCount = 1;
                }
                else if (v == min)
                {
                    tieCount++;
                    if (Random.Range(0, tieCount) == 0)
                        chosen = i;
                }
            }

            var map = _quotaByDef[chosen];
            if (map.ContainsKey(def)) map[def] += 1;
            else map.Add(def, 1);

            totalValue[chosen] += Mathf.Max(0, def.baseValue);
        }

        // 4) Create runtime partial contracts
        for (int i = 0; i < _destCount; i++)
        {
            _partialContracts.Add(CreatePartialContract(contract, i, _quotaByDef[i]));
        }
    }

    public static DeliveryContractDefinition GetPartialContract(int destinationIndex)
    {
        if (!HasAssignment) return null;
        if (destinationIndex < 0 || destinationIndex >= _partialContracts.Count) return null;
        return _partialContracts[destinationIndex];
    }

    public static IReadOnlyDictionary<ItemDefinition, int> GetQuotaByDefinition(int destinationIndex)
    {
        if (!HasAssignment) return null;
        if (destinationIndex < 0 || destinationIndex >= _quotaByDef.Count) return null;
        return _quotaByDef[destinationIndex];
    }

    public static void RecordDestinationResult(int destinationIndex, int reward)
    {
        if (_rewardByDest == null || _completed == null) return;
        if (destinationIndex < 0 || destinationIndex >= _rewardByDest.Length) return;

        _rewardByDest[destinationIndex] = Mathf.Max(0, reward);
        _completed[destinationIndex] = true;
    }

    public static int GetTotalReward()
    {
        if (_rewardByDest == null) return 0;
        int sum = 0;
        for (int i = 0; i < _rewardByDest.Length; i++)
            sum += Mathf.Max(0, _rewardByDest[i]);
        return sum;
    }

    
    public static string BuildAssignmentDebugText()
    {
        if (!HasAssignment)
            return "[DestinationAssignmentState] No assignment.";

        var sb = new System.Text.StringBuilder(512);
        sb.AppendLine("[DestinationAssignmentState] Assignment (pre-split)");
        sb.Append(" Contract: ").Append(_sourceContract != null ? _sourceContract.displayName : "(null)").AppendLine();
        sb.Append(" Destinations: ").Append(_destCount).AppendLine();

        for (int i = 0; i < _destCount; i++)
        {
            long value = 0;
            int units = 0;

            if (i < _quotaByDef.Count && _quotaByDef[i] != null)
            {
                foreach (var kv in _quotaByDef[i])
                {
                    if (kv.Key == null) continue;
                    int q = Mathf.Max(0, kv.Value);
                    units += q;
                    value += (long)Mathf.Max(0, kv.Key.baseValue) * q;
                }
            }

            sb.Append("  - Dest ").Append(i + 1).Append(": units=").Append(units).Append(", value=").Append(value).AppendLine();
        }

        return sb.ToString();
    }

public static string BuildTripSummaryText()
    {
        if (_rewardByDest == null)
            return string.Empty;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        sb.AppendLine("Trip Delivery Summary");

        int total = 0;
        for (int i = 0; i < _rewardByDest.Length; i++)
        {
            int r = Mathf.Max(0, _rewardByDest[i]);
            total += r;
            sb.Append(" - Destination ").Append(i + 1).Append(": ").Append(r).AppendLine();
        }

        sb.Append("TOTAL: ").Append(total);
        return sb.ToString();
    }

    private static DeliveryContractDefinition CreatePartialContract(DeliveryContractDefinition src, int index, Dictionary<ItemDefinition, int> quota)
    {
        var pc = ScriptableObject.CreateInstance<DeliveryContractDefinition>();
        pc.contractId = (src != null && !string.IsNullOrEmpty(src.contractId))
            ? $"{src.contractId}_DEST_{index}"
            : $"PARTIAL_DEST_{index}";

        pc.displayName = (src != null && !string.IsNullOrEmpty(src.displayName))
            ? $"{src.displayName} (Destination {index + 1})"
            : $"Destination {index + 1}";

        // Build requiredItems array from quota
        if (quota == null || quota.Count == 0)
        {
            pc.requiredItems = new DeliveryContractDefinition.RequiredItem[0];
        }
        else
        {
            var list = new List<DeliveryContractDefinition.RequiredItem>(quota.Count);
            foreach (var kv in quota)
            {
                if (kv.Key == null) continue;
                int q = Mathf.Max(0, kv.Value);
                if (q <= 0) continue;

                var ri = new DeliveryContractDefinition.RequiredItem();
                ri.item = kv.Key;
                ri.requiredQty = q;
                list.Add(ri);
            }
            pc.requiredItems = list.ToArray();
        }

        // Partial completion allowed via terminal anyway; keep this 0 so activation buttons can also work.
        pc.minItemsToActivate = 0;

        return pc;
    }
}
