// Assets/Scripts/Inventory/InventorySystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    [Header("Config")]
    public int slotCount = 5;

    [Header("Refs")]
    public CarrierController carrier;

    [Header("Carrier Mount")]
    [Tooltip("Player pivot where the carrier object is attached when equipped.")]
    public Transform carrierMountPivot;   // e.g., player/CarrierPivot

    [Header("State (read-only)")]
    public int activeIndex = 0;

    public event Action OnInventoryChanged;

    [Serializable]
    public class ItemStackData
    {
        public ItemDefinition def;
        public int size;
        public int durCurrent = -1;
        public int durMax = -1;
    }

    [Serializable]
    public class Slot
    {
        public ItemStackData stack;
    }

    public List<Slot> slots = new List<Slot>();

    void Awake()
    {
        if (slots.Count != slotCount)
        {
            slots.Clear();
            for (int i = 0; i < slotCount; i++) slots.Add(new Slot());
        }

        activeIndex = Mathf.Clamp(activeIndex, 0, slotCount - 1);

        // Auto-wire carrier mount pivot if not set.
        if (!carrierMountPivot)
        {
            var player = GetComponentInParent<PlayerController>();
            if (player != null)
            {
                var pivot = player.transform.Find("CarrierPivot");
                if (pivot != null)
                    carrierMountPivot = pivot;
                else
                    carrierMountPivot = player.transform;
            }
            else
            {
                carrierMountPivot = transform;
            }
        }

        if (!carrier)
            carrier = UnityEngine.Object.FindFirstObjectByType<CarrierController>();
    }

    // 式式 Query 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    public bool IsEmpty(int i) => i >= 0 && i < slots.Count && slots[i].stack == null;
    public ItemDefinition Get(int i) => (i >= 0 && i < slots.Count && slots[i].stack != null) ? slots[i].stack.def : null;
    public ItemDefinition ActiveDef() => Get(activeIndex);

    public bool HasCarrierInInventory()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i].stack;
            if (s != null && s.def != null && s.def.isCarrier) return true;
        }
        return false;
    }

    public bool HasSpaceFor(int required)
    {
        for (int start = 0; start <= slotCount - required; start++)
        {
            bool ok = true;
            for (int k = 0; k < required; k++)
                if (!IsEmpty(start + k)) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    public int FindContiguousSpace(int required)
    {
        for (int start = 0; start <= slotCount - required; start++)
        {
            bool ok = true;
            for (int k = 0; k < required; k++)
                if (!IsEmpty(start + k)) { ok = false; break; }
            if (ok) return start;
        }
        return -1;
    }

    // 式式 Control 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    public void SetActiveIndex(int idx)
    {
        activeIndex = Mathf.Clamp(idx, 0, slotCount - 1);
        OnInventoryChanged?.Invoke();
    }

    public bool TryPickupWorldItem(WorldItem worldItem)
    {
        if (!worldItem || !worldItem.definition)
        {
            Debug.LogWarning("[InventorySystem] WorldItem/definition null.");
            return false;
        }

        var def = worldItem.definition;
        int need = Mathf.Clamp(def.slotSize, 1, slotCount);

        int where = FindContiguousSpace(need);
        if (where >= 0)
        {
            // Durability snapshot
            int cur = -1, max = -1;
            if (worldItem.TryGetDurability(out var c, out var m)) { cur = c; max = m; }

            var data = new ItemStackData { def = def, size = need, durCurrent = cur, durMax = max };
            for (int k = 0; k < need; k++)
                slots[where + k].stack = data;

            if (def.isCarrier)
            {
                // Carrier: reuse this world object and attach to player.
                HandleCarrierPickup(worldItem);
            }
            else
            {
                // Normal items: destroy world instance.
                worldItem.OnPickedUp(true);
            }

            activeIndex = where;
            OnInventoryChanged?.Invoke();
            return true;
        }

        // No contiguous inventory space: try mounting onto carrier if present.
        if (HasCarrierInInventory())
        {
            if (!carrier) carrier = UnityEngine.Object.FindFirstObjectByType<CarrierController>();
            if (!carrier)
            {
                Debug.LogWarning("[InventorySystem] Carrier ref missing.");
                return false;
            }
            if (!def.isCarrier)
                return carrier.TryMount(worldItem);
        }

        return false;
    }

    /// <summary>
    /// Drop active item to world as a new prefab (non-carrier items only).
    /// Carrier items are dropped via PlayerController + CarrierController.
    /// </summary>
    public bool DropActiveItem(Transform dropOrigin, Vector3 forward)
    {
        var head = (activeIndex >= 0 && activeIndex < slots.Count) ? slots[activeIndex].stack : null;
        if (head == null || head.def == null) return false;

        var def = head.def;
        if (def.isCarrier)
        {
            // Carrier dropping is handled separately (hold G).
            Debug.Log("[InventorySystem] DropActiveItem ignored for carrier item.");
        }
        else
        {
            Vector3 pos = dropOrigin
                ? dropOrigin.position + forward * 0.6f + Vector3.up * 0.5f
                : transform.position + transform.forward * 0.6f + Vector3.up * 0.5f;

            if (def.worldPrefab)
            {
                var go = UnityEngine.Object.Instantiate(def.worldPrefab, pos, Quaternion.identity);
                go.name = def.worldPrefab.name;

                var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                wi.definition = def;

                if (head.durCurrent >= 0 || head.durMax > 0)
                    wi.ApplyDurability(head.durCurrent, head.durMax, true);
            }
        }

        for (int i = 0; i < slots.Count; i++)
            if (slots[i].stack == head) slots[i].stack = null;

        while (activeIndex > 0 && slots[activeIndex].stack == null && slots[activeIndex - 1].stack == null)
            activeIndex--;

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Called when the player drops the carrier as a bundle (hold G).
    /// Only removes the carrier item from inventory; the world carrier object
    /// is handled by CarrierController.DropAsBundle.
    /// </summary>
    public bool DropCarrierAsBundle(Transform dropOrigin, Vector3 forward)
    {
        if (activeIndex < 0 || activeIndex >= slots.Count) return false;
        var head = slots[activeIndex].stack;
        if (head == null || head.def == null || !head.def.isCarrier) return false;

        for (int i = 0; i < slots.Count; i++)
            if (slots[i].stack == head) slots[i].stack = null;

        while (activeIndex > 0 && slots[activeIndex].stack == null && slots[activeIndex - 1].stack == null)
            activeIndex--;

        OnInventoryChanged?.Invoke();
        return true;
    }

    // 式式 Internal helpers 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式

    /// <summary>
    /// Attach the picked-up carrier world instance to the player pivot and
    /// configure it for equipped state.
    /// </summary>
    private void HandleCarrierPickup(WorldItem worldItem)
    {
        if (!worldItem) return;

        var cc = worldItem.GetComponent<CarrierController>();
        if (!cc)
        {
            Debug.LogWarning("[InventorySystem] Carrier item picked up but no CarrierController found.");
            return;
        }

        // Mark this carrier as equipped so ContainerAutoParent does not re-parent it.
        worldItem.ignoreContainerAutoParent = true;

        // Store carrier reference on inventory
        carrier = cc;

        // Attach under mount pivot (usually player's back).
        if (carrierMountPivot)
        {
            Transform t = cc.transform;
            t.SetParent(carrierMountPivot, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        // Configure physics while worn
        if (!worldItem.rb) worldItem.rb = worldItem.GetComponent<Rigidbody>();
        if (worldItem.rb)
        {
#if UNITY_6000_0_OR_NEWER
            worldItem.rb.linearVelocity = Vector3.zero;
#else
            worldItem.rb.velocity = Vector3.zero;
#endif
            worldItem.rb.angularVelocity = Vector3.zero;
            worldItem.rb.isKinematic = true;
            worldItem.rb.useGravity = false;
        }

        var cols = worldItem.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols) c.enabled = false;

        var rends = worldItem.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) r.enabled = true;

        // Tell the carrier it is now equipped (prevents instant spill)
        cc.MarkEquipped(0.3f);

        // Push reference into PlayerController if possible
        if (carrierMountPivot)
        {
            var player = carrierMountPivot.GetComponentInParent<PlayerController>();
            if (player != null)
                player.carrier = cc;
        }
    }
}
