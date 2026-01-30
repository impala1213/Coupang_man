using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Destination terminal under a DropZone.
/// - Player presses E to open a UI panel.
/// - UI is driven by the dynamically assigned "partial contract" for this destination.
/// - "Complete Delivery" is allowed even if only partially fulfilled:
///     - Pays proportionally for items delivered that match this destination's assignment (capped by requiredQty per item).
///     - Spawns shield (radius from drop zone settings).
///     - Locks items inside the shield so the player cannot reclaim them.
///     - Terminal becomes unusable.
/// </summary>
public class DestinationTerminalInteractable : PlayerInteractableBase
{
    [Header("Refs")]
    public DeliveryDropZone dropZone;
    public DestinationTerminalUIController uiController;

    [Header("Assignment")]
    [Tooltip("Assigned destination index (0..N-1). Set by GameSession when spawning drop zones.")]
    public int destinationIndex = -1;

    [System.Serializable]
    public struct ItemQuota
    {
        public ItemDefinition item;
        [Min(0)] public int requiredUnits;
    }

    [SerializeField] private List<ItemQuota> quotas = new List<ItemQuota>();

    [Header("Behavior")]
    public DeliveryPricingTable pricingTable; // optional
    public bool debugLog;

    public bool IsCompleted { get; private set; }

    private readonly Dictionary<ItemDefinition, int> _quotaByDef = new Dictionary<ItemDefinition, int>(32);
    private readonly Dictionary<ItemCategory, int> _quotaByCategory = new Dictionary<ItemCategory, int>(8);

    private void OnEnable()
    {
        DestinationGoalRegistry.Register(this);
    }

    private void OnDisable()
    {
        DestinationGoalRegistry.Unregister(this);
    }

    private void Awake()
    {
        if (!dropZone)
            dropZone = GetComponentInParent<DeliveryDropZone>();

        if (!uiController)
            uiController = FindObjectOfType<DestinationTerminalUIController>(true);

        RebuildQuotaMaps();
    }

    /// <summary>
    /// Assigns this terminal a "partial contract" (per-item-definition quantities).
    /// Typically called by GameSession when spawning destinations.
    /// </summary>
    public void ConfigureAssignment(DeliveryContractDefinition partialContract, int index)
    {
        destinationIndex = index;

        quotas.Clear();
        if (partialContract != null && partialContract.requiredItems != null)
        {
            for (int i = 0; i < partialContract.requiredItems.Length; i++)
            {
                var r = partialContract.requiredItems[i];
                if (r == null || r.item == null) continue;

                int q = Mathf.Max(0, r.requiredQty);
                if (q <= 0) continue;

                quotas.Add(new ItemQuota { item = r.item, requiredUnits = q });
            }
        }

        RebuildQuotaMaps();
    }

    /// <summary>
    /// Assigns this terminal a quota map directly.
    /// </summary>
    public void ConfigureAssignment(IReadOnlyDictionary<ItemDefinition, int> quotaByDefinition, int index)
    {
        destinationIndex = index;

        quotas.Clear();
        if (quotaByDefinition != null)
        {
            foreach (var kv in quotaByDefinition)
            {
                if (kv.Key == null) continue;
                int q = Mathf.Max(0, kv.Value);
                if (q <= 0) continue;

                quotas.Add(new ItemQuota { item = kv.Key, requiredUnits = q });
            }
        }

        RebuildQuotaMaps();
    }

    public IReadOnlyDictionary<ItemDefinition, int> GetQuotaByDefinition() => _quotaByDef;
    public IReadOnlyDictionary<ItemCategory, int> GetQuotaByCategory() => _quotaByCategory;

    public void RebuildQuotaMaps()
    {
        _quotaByDef.Clear();
        _quotaByCategory.Clear();

        for (int i = 0; i < quotas.Count; i++)
        {
            var q = quotas[i];
            if (q.item == null) continue;

            int units = Mathf.Max(0, q.requiredUnits);
            if (units <= 0) continue;

            if (_quotaByDef.ContainsKey(q.item)) _quotaByDef[q.item] += units;
            else _quotaByDef.Add(q.item, units);

            var cat = q.item.category;
            if (_quotaByCategory.ContainsKey(cat)) _quotaByCategory[cat] += units;
            else _quotaByCategory.Add(cat, units);
        }
    }

    public override void OnFocusEnter(PlayerController player)
    {
        InteractionLock.SetTerminalFocus(true);
    }

    public override void OnFocusExit(PlayerController player)
    {
        InteractionLock.SetTerminalFocus(false);
    }

    public override void OnUsePressed(PlayerController player)
    {
        if (IsCompleted)
            return;

        if (!uiController)
        {
            Debug.LogWarning("[DestinationTerminalInteractable] uiController missing. Add DestinationTerminalUIController to your HUD/Canvas and assign it (or let FindObjectOfType find it).");
            return;
        }

        uiController.Toggle(this);
    }

    /// <summary>
    /// Called by UI "Complete Delivery" button.
    /// </summary>
    public void TryCompleteDelivery()
    {
        if (IsCompleted) return;
        if (!dropZone) return;

        // Partial delivery is allowed => minUnitsRequired = 0
        bool ok = dropZone.TryActivateByDefinitionQuotas(_quotaByDef, minUnitsRequired: 0, pricingTable: pricingTable);
        if (!ok)
        {
            if (debugLog)
                Debug.Log("[DestinationTerminalInteractable] CompleteDelivery failed (already activated or not enough items).");
            return;
        }

        IsCompleted = true;

        // Remove this destination from radar goals (completed destinations should disappear).
        RadarGoalRegistry.UnregisterGoal(dropZone.transform);

        // Record result for end-of-trip summary.
        DestinationAssignmentState.RecordDestinationResult(destinationIndex, dropZone.LastReward);

        // Close UI
        if (uiController != null)
            uiController.Close();

        // Disable further interaction (keeps object visible)
        enabled = false;

        // Update radar goal to the next incomplete destination (optional)
        DestinationGoalRegistry.NotifyDestinationCompleted();
    }
}
