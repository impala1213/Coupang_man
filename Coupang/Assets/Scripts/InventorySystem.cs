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
    public CarrierController carrier; // assign Player's CarrierController

    [Header("State (read-only)")]
    public int activeIndex = 0;

    [Serializable]
    public class Slot
    {
        public ItemDefinition def;
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
    }

    // 式式式式式式式式式式式 Query 式式式式式式式式式式式
    public bool IsEmpty(int i) => i >= 0 && i < slots.Count && slots[i].def == null;
    public ItemDefinition Get(int i) => (i >= 0 && i < slots.Count) ? slots[i].def : null;
    public ItemDefinition ActiveDef() => Get(activeIndex);

    public bool HasCarrierInInventory()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var d = slots[i].def;
            if (d != null && d.isCarrier) return true;
        }
        return false;
    }

    public bool HasSpaceFor(int required)
    {
        // contiguous 1..required (required <= slotCount)
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

    // 式式式式式式式式式式式 Control 式式式式式式式式式式式
    public void SetActiveIndex(int idx)
    {
        activeIndex = Mathf.Clamp(idx, 0, slotCount - 1);
        // auto-equip semantics are handled by PlayerController (use on LMB etc.)
    }

    public bool TryPickupWorldItem(WorldItem worldItem)
    {
        if (!worldItem || !worldItem.definition) return false;
        var def = worldItem.definition;

        // If there is space, always pick into inventory
        int need = Mathf.Clamp(def.slotSize, 1, slotCount);
        int where = FindContiguousSpace(need);

        if (where >= 0)
        {
            // pick into inventory
            for (int k = 0; k < need; k++)
                slots[where + k].def = def;
            worldItem.OnPickedUp();
            // set active to start of the placed block for convenience
            activeIndex = where;
            return true;
        }

        // No space: if we have a carrier item somewhere, mount to carrier (for cargo-like)
        if (HasCarrierInInventory() && carrier != null)
        {
            // do not mount the carrier item itself
            if (!def.isCarrier)
            {
                // treat everything as cargo-like per design
                return carrier.TryMount(worldItem);
            }
        }

        // no space and no carrier to mount
        return false;
    }

    public bool DropActiveItem(Transform dropOrigin, Vector3 forward)
    {
        var def = ActiveDef();
        if (!def) return false;

        Vector3 pos = dropOrigin ? dropOrigin.position + forward * 0.6f + Vector3.up * 0.5f
                                 : transform.position + transform.forward * 0.6f + Vector3.up * 0.5f;

        // If dropping the carrier itself: spill all mounted cargo too
        if (def.isCarrier && carrier != null && carrier.HasAnyMounted())
        {
            carrier.SpillAllOnCarrierDrop(pos, forward);
        }

        // instantiate world prefab for the dropped item
        if (def.worldPrefab)
        {
            var go = GameObject.Instantiate(def.worldPrefab, pos, Quaternion.identity);
            var wi = go.GetComponent<WorldItem>();
            if (wi && wi.definition == null) wi.definition = def;
        }

        // clear occupied slots for that item block
        int need = Mathf.Clamp(def.slotSize, 1, slotCount);
        // wipe consecutive block starting at activeIndex if it matches def
        int start = activeIndex;
        for (int k = 0; k < need; k++)
        {
            int idx = start + k;
            if (idx < 0 || idx >= slots.Count) break;
            if (slots[idx].def == def) slots[idx].def = null;
        }

        // move active to a valid position
        while (activeIndex > 0 && slots[activeIndex].def == null && slots[activeIndex - 1].def == null)
            activeIndex--;

        return true;
    }
}
