using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop zone:
/// - Tracks WorldItems inside trigger volume
/// - On activation (via DeliveryActivationButton), pays money based on the active DeliveryContractDefinition:
///     reward = sum(min(deliveredQty, requiredQty) * item.baseValue)
/// - Does NOT destroy delivered items (they will be removed when the planet scene unloads)
/// - Spawns a solid shield that blocks everyone (including player)
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class DeliveryDropZone : MonoBehaviour
{
    [Header("Config")]
    public bool ignoreMountedOnCarrier = true;
    public bool ignoreCarrierItems = true;
    public bool ignoreZeroValueItems = true;
    public LayerMask itemLayers = ~0;

    [Header("Shield")]
    [Tooltip("Optional. If assigned, spawns this prefab on activation.")]
    public GameObject shieldPrefab;

    [Tooltip("Used when shieldPrefab has a DeliveryShield component.")]
    public float shieldRadius = 10f;

    [Tooltip("Optional root where the shield is spawned. If null, uses this.transform.")]
    public Transform shieldSpawnRoot;

    [Header("Runtime")]
    [Tooltip("Active mission contract for this drop zone (optional). If null, falls back to baseValue-based payout.")]
    public DeliveryContractDefinition contract;

    public PlayerWallet wallet;

    [Header("Debug")]
    public bool debugLog;

    public bool IsActivated { get; private set; }
    public int LastReward { get; private set; }

    public event Action<int> OnActivated; // reward

    private Collider _trigger;
    private readonly HashSet<WorldItem> _items = new HashSet<WorldItem>();

    // Cached contract lookup (rebuilt on Configure)
    private Dictionary<ItemDefinition, int> _reqQty;
    private HashSet<ItemDefinition> _reqSet;

    private void Awake()
    {
        _trigger = GetComponent<Collider>();
        if (_trigger && !_trigger.isTrigger)
        {
            // Drop zone must be trigger so it can track items.
            _trigger.isTrigger = true;
        }

        if (!shieldSpawnRoot)
            shieldSpawnRoot = transform;
    }

    /// <summary>
    /// Called by GameSession after spawning the drop zone (new mission rules).
    /// </summary>
    public void Configure(DeliveryContractDefinition contractDef, PlayerWallet playerWallet, GameObject shield, float fallbackRadius)
    {
        contract = contractDef;
        wallet = playerWallet;

        if (shieldPrefab == null)
            shieldPrefab = shield;

        if (shieldRadius <= 0f)
            shieldRadius = Mathf.Max(0.1f, fallbackRadius);

        RebuildContractCache();
    }


    private void RebuildContractCache()
    {
        _reqQty = null;
        _reqSet = null;

        if (contract == null || contract.requiredItems == null || contract.requiredItems.Length == 0)
            return;

        _reqQty = new Dictionary<ItemDefinition, int>(contract.requiredItems.Length);
        _reqSet = new HashSet<ItemDefinition>();

        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var r = contract.requiredItems[i];
            if (r == null || r.item == null) continue;

            int qty = Mathf.Max(1, r.requiredQty);
            if (_reqQty.ContainsKey(r.item))
                _reqQty[r.item] += qty;
            else
                _reqQty.Add(r.item, qty);

            _reqSet.Add(r.item);
        }

        if (_reqQty.Count == 0)
        {
            _reqQty = null;
            _reqSet = null;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsActivated) return;
        if (((1 << other.gameObject.layer) & itemLayers) == 0) return;

        var wi = other.GetComponentInParent<WorldItem>();
        if (!IsValidDeliverItem(wi)) return;
        _items.Add(wi);
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsActivated) return;

        var wi = other.GetComponentInParent<WorldItem>();
        if (wi == null) return;
        _items.Remove(wi);
    }

    private bool IsBroken(WorldItem wi)
    {
        if (wi == null) return true;
        if (!wi.gameObject.activeInHierarchy) return true;

        if (wi.useDurability && wi.currentDurability <= 0)
            return true;

        return false;
    }

    private bool IsValidDeliverItem(WorldItem wi)
    {
        if (wi == null) return false;
        if (wi.definition == null) return false;

        if (ignoreMountedOnCarrier && wi.isOnCarrier)
            return false;

        if (ignoreCarrierItems && wi.definition.isCarrier)
            return false;

        if (ignoreZeroValueItems && wi.definition.baseValue <= 0)
            return false;

        if (IsBroken(wi))
            return false;

        // If we have an active contract list, only accept those items.
        if (_reqSet != null && !_reqSet.Contains(wi.definition))
            return false;

        return true;
    }

    public int CountValidItems()
    {
        CleanupDeadRefs();

        int count = 0;
        foreach (var wi in _items)
        {
            if (IsValidDeliverItem(wi))
                count++;
        }
        return count;
    }

    public int ComputeReward()
    {
        CleanupDeadRefs();

        // New mission payout: contract.requiredItems (capped)
        if (_reqQty != null && _reqQty.Count > 0)
        {
            Dictionary<ItemDefinition, int> delivered = new Dictionary<ItemDefinition, int>(_reqQty.Count);

            foreach (var wi in _items)
            {
                if (!IsValidDeliverItem(wi))
                    continue;

                var def = wi.definition;
                int stack = Mathf.Max(1, wi.stackCount);

                if (!delivered.ContainsKey(def))
                    delivered.Add(def, stack);
                else
                    delivered[def] += stack;
            }

            long reward = 0;
            foreach (var kv in _reqQty)
            {
                var def = kv.Key;
                int need = Mathf.Max(0, kv.Value);
                if (def == null || need <= 0) continue;

                int got = delivered.TryGetValue(def, out int v) ? Mathf.Max(0, v) : 0;
                int payQty = Mathf.Min(got, need);

                int value = Mathf.Max(0, def.baseValue);
                reward += (long)payQty * value;
            }

            if (reward > int.MaxValue) return int.MaxValue;
            return (int)Mathf.Max(0, reward);
        }

        // Fallback payout (no contract list): sum(baseValue * stackCount)
        int total = 0;
        foreach (var wi in _items)
        {
            if (!IsValidDeliverItem(wi))
                continue;

            var def = wi.definition;
            int stack = Mathf.Max(1, wi.stackCount);

            total += Mathf.Max(0, def.baseValue) * stack;
        }
        return Mathf.Max(0, total);
    }

    

    /// <summary>
    /// Computes reward using per-category quotas.
    /// Rule:
    /// - For each category quota, pays for up to that many units (stackCount) present in the zone.
    /// - If more units are present than quota, pays the most expensive items first (by ItemDefinition.baseValue).
    /// - Items must still satisfy IsValidDeliverItem (contract filtering, durability, etc.).
    /// </summary>
    public int ComputeRewardByCategoryQuotas(IReadOnlyDictionary<ItemCategory, int> quotaByCategory, bool mostExpensiveFirst = true, DeliveryPricingTable pricingTable = null)
    {
        CleanupDeadRefs();

        if (quotaByCategory == null || quotaByCategory.Count == 0)
            return 0;

        // Build valid item list snapshot
        List<WorldItem> valid = new List<WorldItem>(_items.Count);
        foreach (var wi in _items)
        {
            if (IsValidDeliverItem(wi))
                valid.Add(wi);
        }

        long reward = 0;

        // Group by category
        Dictionary<ItemCategory, List<WorldItem>> byCat = new Dictionary<ItemCategory, List<WorldItem>>(8);
        for (int i = 0; i < valid.Count; i++)
        {
            var wi = valid[i];
            var def = wi.definition;
            if (def == null) continue;

            var cat = def.category;
            if (!byCat.TryGetValue(cat, out var list))
            {
                list = new List<WorldItem>(8);
                byCat.Add(cat, list);
            }
            list.Add(wi);
        }

        foreach (var kv in quotaByCategory)
        {
            ItemCategory cat = kv.Key;
            int quota = Mathf.Max(0, kv.Value);
            if (quota <= 0) continue;

            if (!byCat.TryGetValue(cat, out var list) || list == null || list.Count == 0)
                continue;

            if (mostExpensiveFirst)
            {
                list.Sort((a, b) =>
                {
                    int av = (a != null && a.definition != null) ? a.definition.baseValue : 0;
                    int bv = (b != null && b.definition != null) ? b.definition.baseValue : 0;
                    return bv.CompareTo(av); // descending
                });
            }

            int remaining = quota;
            for (int i = 0; i < list.Count && remaining > 0; i++)
            {
                var wi = list[i];
                if (!IsValidDeliverItem(wi)) continue;

                var def = wi.definition;
                if (def == null) continue;

                int value = Mathf.Max(0, def.baseValue);
                if (pricingTable != null)
                {
                    float mul = Mathf.Max(0f, pricingTable.GetMultiplier(def.category));
                    value = Mathf.RoundToInt(value * mul);
                }

                int stack = Mathf.Max(1, wi.stackCount);
                int take = Mathf.Min(stack, remaining);

                reward += (long)take * value;
                remaining -= take;
            }
        }

        if (reward > int.MaxValue) return int.MaxValue;
        return (int)Mathf.Max(0, reward);
    }

    /// <summary>
    /// Counts units (stackCount) currently inside the zone per category.
    /// Only counts items that satisfy IsValidDeliverItem.
    /// </summary>
    public void GetDeliveredUnitsByCategory(Dictionary<ItemCategory, int> outCounts)
    {
        if (outCounts == null) return;
        outCounts.Clear();

        CleanupDeadRefs();

        foreach (var wi in _items)
        {
            if (!IsValidDeliverItem(wi))
                continue;

            var def = wi.definition;
            if (def == null) continue;

            int stack = Mathf.Max(1, wi.stackCount);
            if (!outCounts.ContainsKey(def.category))
                outCounts.Add(def.category, stack);
            else
                outCounts[def.category] += stack;
        }
    }

    
    /// <summary>
    /// Writes current delivered unit counts per ItemDefinition into outCounts.
    /// Only counts items that satisfy IsValidDeliverItem.
    /// </summary>
    public void GetDeliveredUnitsByDefinition(Dictionary<ItemDefinition, int> outCounts)
    {
        if (outCounts == null) return;
        outCounts.Clear();

        CleanupDeadRefs();

        foreach (var wi in _items)
        {
            if (!IsValidDeliverItem(wi))
                continue;

            var def = wi.definition;
            if (def == null) continue;

            int stack = Mathf.Max(1, wi.stackCount);

            if (!outCounts.ContainsKey(def))
                outCounts.Add(def, stack);
            else
                outCounts[def] += stack;
        }
    }

    /// <summary>
    /// Activates the zone using per-item-definition quotas and spawns the shield.
    /// This is used by multi-destination terminals with per-destination "partial contracts".
    /// </summary>
    public bool TryActivateByDefinitionQuotas(IReadOnlyDictionary<ItemDefinition, int> quotaByDefinition, int minUnitsRequired = 0, DeliveryPricingTable pricingTable = null)
    {
        if (IsActivated)
            return false;

        CleanupDeadRefs();

        // count valid units (stackCount)
        int units = 0;
        foreach (var wi in _items)
        {
            if (!IsValidDeliverItem(wi))
                continue;

            units += Mathf.Max(1, wi.stackCount);
        }

        if (units < Mathf.Max(0, minUnitsRequired))
        {
            if (debugLog)
                Debug.Log($"[DeliveryDropZone] ActivateByDefinition failed: units={units}, min={minUnitsRequired}");
            return false;
        }

        int reward = ComputeRewardByDefinitionQuotas(quotaByDefinition, pricingTable);

        if (wallet != null && reward != 0)
            wallet.AddMoney(reward);

        // Stop tracking immediately
        IsActivated = true;

        // Snapshot items currently inside before clearing
        var snapshot = new System.Collections.Generic.List<WorldItem>(_items);
        _items.Clear();

        var shieldInstance = SpawnShield(); // (spawned via helper)
        SealItemsAfterDelivery(snapshot, shieldInstance);

        LastReward = reward;
        OnActivated?.Invoke(reward);
        return true;
    }

    private int ComputeRewardByDefinitionQuotas(IReadOnlyDictionary<ItemDefinition, int> quotaByDefinition, DeliveryPricingTable pricingTable)
    {
        if (quotaByDefinition == null || quotaByDefinition.Count == 0)
            return 0;

        // Build delivered counts
        Dictionary<ItemDefinition, int> delivered = new Dictionary<ItemDefinition, int>(quotaByDefinition.Count);
        GetDeliveredUnitsByDefinition(delivered);

        long sum = 0;
        foreach (var kv in quotaByDefinition)
        {
            var def = kv.Key;
            if (def == null) continue;

            int req = Mathf.Max(0, kv.Value);
            if (req <= 0) continue;

            int got = delivered.TryGetValue(def, out int g) ? Mathf.Max(0, g) : 0;
            int payUnits = Mathf.Min(req, got);
            if (payUnits <= 0) continue;

            int unitValue = def.baseValue;
            if (pricingTable != null)
                unitValue = pricingTable.Evaluate(def, 1);

            unitValue = Mathf.Max(0, unitValue);
            sum += (long)payUnits * unitValue;
            if (sum > int.MaxValue) return int.MaxValue;
        }

        return (int)Mathf.Max(0, sum);
    }

/// <summary>
    /// Activates the zone using per-category quotas and spawns the shield.
    /// This is used by multi-destination terminals.
    /// </summary>
    public bool TryActivateByCategoryQuotas(IReadOnlyDictionary<ItemCategory, int> quotaByCategory, int minUnitsRequired = 0, bool mostExpensiveFirst = true, DeliveryPricingTable pricingTable = null)
    {
        if (IsActivated)
            return false;

        CleanupDeadRefs();

        // count valid units (stackCount)
        int units = 0;
        foreach (var wi in _items)
        {
            if (!IsValidDeliverItem(wi))
                continue;

            units += Mathf.Max(1, wi.stackCount);
        }

        if (units < Mathf.Max(0, minUnitsRequired))
        {
            if (debugLog)
                Debug.Log($"[DeliveryDropZone] ActivateByCategory failed: units={units}, min={minUnitsRequired}");
            return false;
        }

        int reward = ComputeRewardByCategoryQuotas(quotaByCategory, mostExpensiveFirst, pricingTable);

        if (wallet != null && reward != 0)
            wallet.AddMoney(reward);

        // Stop tracking immediately
        IsActivated = true;

        // Snapshot items currently inside before clearing
        var snapshot = new System.Collections.Generic.List<WorldItem>(_items);
        _items.Clear();

        var shieldInstance = SpawnShield(); // (spawned via helper)
        SealItemsAfterDelivery(snapshot, shieldInstance);

        LastReward = reward;
        OnActivated?.Invoke(reward);
        return true;
    }

    /// <summary>
    /// After delivery is completed and the shield is spawned, lock items inside the shield so they cannot be picked up again.
    /// This prevents reclaiming delivered items.
    /// </summary>
    private void SealItemsAfterDelivery(System.Collections.Generic.List<WorldItem> snapshot, GameObject shieldInstance)
    {
        if (snapshot == null || snapshot.Count == 0) return;

        Vector3 center = (shieldInstance != null) ? shieldInstance.transform.position : (shieldSpawnRoot != null ? shieldSpawnRoot.position : transform.position);
        float r = Mathf.Max(0.1f, shieldRadius);
        float r2 = (r + 0.15f) * (r + 0.15f);

        for (int i = 0; i < snapshot.Count; i++)
        {
            var wi = snapshot[i];
            if (wi == null) continue;

            Vector3 p = wi.transform.position;
            if ((p - center).sqrMagnitude > r2) continue;

            wi.SetPickupLocked(true);
        }
    }

    public bool TryActivate(int minItemsRequired)
    {
        if (IsActivated)
            return false;

        CleanupDeadRefs();

        int validCount = CountValidItems();
        if (validCount < Mathf.Max(0, minItemsRequired))
        {
            if (debugLog)
                Debug.Log($"[DeliveryDropZone] Activate failed: validCount={validCount}, min={minItemsRequired}");
            return false;
        }

        int reward = ComputeReward();

        if (wallet != null && reward != 0)
            wallet.AddMoney(reward);

        // Stop tracking immediately
        IsActivated = true;

        // Snapshot items currently inside before clearing
        var snapshot = new System.Collections.Generic.List<WorldItem>(_items);
        _items.Clear();

        var shieldInstance = SpawnShield(); // (spawned via helper)
        SealItemsAfterDelivery(snapshot, shieldInstance);

        LastReward = reward;
        OnActivated?.Invoke(reward);
        return true;
    }

    private GameObject SpawnShield()
    {
        if (shieldPrefab == null) return null;

        Transform root = shieldSpawnRoot != null ? shieldSpawnRoot : transform;

        var go = Instantiate(shieldPrefab, root.position, root.rotation);
        go.name = shieldPrefab.name;

        // If the prefab has DeliveryShield, configure it.
        var sh = go.GetComponent<DeliveryShield>();
        if (sh != null)
        {
            sh.Setup(shieldRadius);
        }
        else
        {
            // Fallback: scale the object roughly by radius
            go.transform.localScale = Vector3.one * (shieldRadius * 2f);
        }

        return go;
    }

    private void CleanupDeadRefs()
    {
        if (_items.Count == 0) return;

        // Remove null or destroyed refs (Unity null)
        _items.RemoveWhere(wi => wi == null);
    }
}
