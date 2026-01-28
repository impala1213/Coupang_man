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

        // Do NOT destroy delivered items (removed when planet scene unloads).
        _items.Clear();

        SpawnShield();

        IsActivated = true;
        LastReward = reward;
        OnActivated?.Invoke(reward);
        return true;
    }

    private void SpawnShield()
    {
        if (shieldPrefab == null) return;

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
    }

    private void CleanupDeadRefs()
    {
        if (_items.Count == 0) return;

        // Remove null or destroyed refs (Unity null)
        _items.RemoveWhere(wi => wi == null);
    }
}
