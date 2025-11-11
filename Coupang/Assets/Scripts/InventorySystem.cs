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

    [Header("State (read-only)")]
    public int activeIndex = 0;

    public event Action OnInventoryChanged;   // ← 추가

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
        if (!carrier) carrier = FindFirstObjectByType<CarrierController>();
    }

    // ── Query
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

    // ── Control
    public void SetActiveIndex(int idx)
    {
        activeIndex = Mathf.Clamp(idx, 0, slotCount - 1);
        OnInventoryChanged?.Invoke();                 // ← 추가
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
            // durability snapshot
            int cur = -1, max = -1;
            if (worldItem.TryGetDurability(out var c, out var m)) { cur = c; max = m; }

            var data = new ItemStackData { def = def, size = need, durCurrent = cur, durMax = max };
            for (int k = 0; k < need; k++)
                slots[where + k].stack = data;

            worldItem.OnPickedUp(true);               // 원본 파괴
            activeIndex = where;
            OnInventoryChanged?.Invoke();             // ← 추가
            return true;
        }

        if (HasCarrierInInventory())
        {
            if (!carrier) carrier = FindFirstObjectByType<CarrierController>();
            if (!carrier) { Debug.LogWarning("[InventorySystem] Carrier ref missing."); return false; }
            if (!def.isCarrier) return carrier.TryMount(worldItem);
        }

        return false;
    }

    public bool DropActiveItem(Transform dropOrigin, Vector3 forward)
    {
        var head = (activeIndex >= 0 && activeIndex < slots.Count) ? slots[activeIndex].stack : null;
        if (head == null || head.def == null) return false;

        var def = head.def;
        Vector3 pos = dropOrigin ? dropOrigin.position + forward * 0.6f + Vector3.up * 0.5f
                                 : transform.position + transform.forward * 0.6f + Vector3.up * 0.5f;

        if (def.isCarrier && carrier != null && carrier.HasAnyMounted())
            carrier.SpillAllOnCarrierDrop(pos, forward);

        if (def.worldPrefab)
        {
            var go = Instantiate(def.worldPrefab, pos, Quaternion.identity);
            go.name = def.worldPrefab.name;

            var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
            wi.definition = def;

            if (head.durCurrent >= 0 || head.durMax > 0)
                wi.ApplyDurability(head.durCurrent, head.durMax, true);
        }

        for (int i = 0; i < slots.Count; i++)
            if (slots[i].stack == head) slots[i].stack = null;

        while (activeIndex > 0 && slots[activeIndex].stack == null && slots[activeIndex - 1].stack == null)
            activeIndex--;

        OnInventoryChanged?.Invoke();                 // ← 추가
        return true;
    }
}
