// Assets/Scripts/Systems/InventorySystem.cs
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    [Header("Config")]
    public int slotCount = 5;

    [Header("UI (optional)")]
    public InventoryUI ui;

    [Header("Player refs (selection effects)")]
    public PlayerController player; // assign in Inspector (or auto-wire in Awake)

    public int ActiveIndex { get; private set; } = 0;
    public event Action OnInventoryChanged;

    [Serializable]
    private class Entry
    {
        public ItemDefinition def;
        public WorldItem worldRef;
        public int startIndex;
        public int size;
    }

    private Entry[] slots;

    void Awake()
    {
        slots = new Entry[Mathf.Max(1, slotCount)];
        if (!player) player = GetComponent<PlayerController>();
        if (ui) ui.Bind(this);
        RaiseChanged();
        ApplySelectionEffects();
    }

    void Update()
    {
        for (int i = 0; i < Mathf.Min(slotCount, 9); i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetActiveIndex(i);
    }

    public bool TryPickupWorldItem(WorldItem world)
    {
        if (!world || !world.definition) return false;

        int need = Mathf.Clamp(world.definition.slotSize, 1, slotCount);
        int start = FindContiguousFree(need);
        if (start < 0) { Debug.Log("Inventory full or no contiguous space."); return false; }

        var e = new Entry { def = world.definition, worldRef = world, startIndex = start, size = need };
        for (int i = 0; i < need; i++) slots[start + i] = e;

        world.OnPickedUp();

        ActiveIndex = start;   // auto-select new item
        RaiseChanged();
        ApplySelectionEffects();
        return true;
    }

    public bool DropActiveItem(Transform dropOrigin, Vector3 forward)
    {
        var e = GetEntryAt(ActiveIndex);
        if (e == null) return false;

        for (int i = 0; i < e.size; i++) slots[e.startIndex + i] = null;

        if (e.worldRef != null)
        {
            Vector3 basePos = dropOrigin ? dropOrigin.position : transform.position;
            Vector3 pos = basePos + forward.normalized * 0.8f + Vector3.up * 0.5f;
            e.worldRef.OnDropped(pos, forward.normalized * 3f + Vector3.up * 0.5f);
        }
        else if (e.def.worldPrefab)
        {
            var go = Instantiate(e.def.worldPrefab, transform.position + transform.forward * 0.8f + Vector3.up * 0.5f, Quaternion.identity);
            var wi = go.GetComponent<WorldItem>();
            if (wi && wi.definition == null) wi.definition = e.def;
        }

        RaiseChanged();
        ApplySelectionEffects();
        return true;
    }

    public void UseActiveItem(PlayerController playerRef)
    {
        var e = GetEntryAt(ActiveIndex);
        if (e == null) return;

        // When active item is carrier, LMB/RMB are reserved for balance. Do nothing here.
        if (e.def.isCarrier) return;

        // TODO: implement item use behavior for non-carrier items.
    }

    public void SetActiveIndex(int index)
    {
        index = Mathf.Clamp(index, 0, slotCount - 1);
        if (ActiveIndex != index)
        {
            ActiveIndex = index;
            RaiseChanged();
            ApplySelectionEffects();
        }
        else
        {
            RaiseChanged();
            ApplySelectionEffects();
        }
    }

    public Sprite GetIconAt(int index)
    {
        var e = GetEntryAt(index);
        return e != null ? e.def.icon : null;
    }

    public ItemDefinition GetActiveDefinition()
    {
        var e = GetEntryAt(ActiveIndex);
        return e != null ? e.def : null;
    }

    // 式式式式式式式式式式式式式 internals 式式式式式式式式式式式式式
    private void RaiseChanged()
    {
        OnInventoryChanged?.Invoke();
        if (ui) ui.Refresh();
    }
    private bool lastActiveCarrier = false;
    private void ApplySelectionEffects()
    {
        if (!player || !player.carrier) return;

        var def = GetActiveDefinition();
        bool activeCarrier = (def != null && def.isCarrier);

        if (activeCarrier != lastActiveCarrier)
        {
            player.carrier.SetActiveMode(activeCarrier);
            lastActiveCarrier = activeCarrier;
        }
    }


    private Entry GetEntryAt(int index)
    {
        if (index < 0 || index >= slotCount) return null;
        return slots[index];
    }

    private int FindContiguousFree(int need)
    {
        for (int start = 0; start <= slotCount - need; start++)
        {
            bool ok = true;
            for (int i = 0; i < need; i++)
                if (slots[start + i] != null) { ok = false; break; }
            if (ok) return start;
        }
        return -1;
    }
    public int FindFirstCarrierIndex()
    {
        for (int i = 0; i < slotCount; i++)
        {
            var e = GetEntryAt(i);
            if (e != null && e.def != null && e.def.isCarrier) return i;
        }
        return -1;
    }

    public bool SelectFirstNonCarrierSlot()
    {
        for (int i = 0; i < slotCount; i++)
        {
            var e = GetEntryAt(i);
            if (e == null || (e.def != null && !e.def.isCarrier))
            {
                SetActiveIndex(i);
                return true;
            }
        }
        return false;
    }


}
