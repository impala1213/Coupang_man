// Assets/Scripts/UI/InventoryUI.cs
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class InventoryUI : MonoBehaviour
{
    [Header("Binding")]
    public InventorySystem inventory;       // optional; empty = auto-bind
    public bool autoBind = true;

    [Header("UI Slots (assign in Inspector)")]
    public Image[] slotIcons;               // link N images in order (e.g., 5)

    [Header("Icons")]
    public Sprite emptySlotIcon;            // shown when slot is empty (optional)
    public Sprite fallbackIcon;             // used when item has no icon (optional)

    [Header("Highlight")]
    [Range(0, 255)] public int activeColorA = 255;
    [Range(0, 255)] public int inactiveColorA = 160;

    // change detection
    private int _lastHash = int.MinValue;

    void Awake()
    {
        if (autoBind && !inventory)
            inventory = FindFirstObjectByType<InventorySystem>();

        ForceRefresh();
    }

    void Update()
    {
        int h = ComputeInventoryHash();
        if (h != _lastHash)
        {
            _lastHash = h;
            Refresh();
        }
    }

    public void ForceRefresh()
    {
        _lastHash = int.MinValue;
        Refresh();
    }

    public void Refresh()
    {
        if (slotIcons == null || slotIcons.Length == 0) return;

        // No inventory bound: show empty icons for all
        if (!inventory)
        {
            for (int i = 0; i < slotIcons.Length; i++)
            {
                var img = slotIcons[i];
                if (!img) continue;
                img.enabled = true;
                img.sprite = emptySlotIcon;
                var c = img.color; c.a = inactiveColorA / 255f; img.color = c;
            }
            return;
        }

        int activeIdx = Mathf.Clamp(inventory.activeIndex, 0, slotIcons.Length - 1);

        for (int i = 0; i < slotIcons.Length; i++)
        {
            var img = slotIcons[i];
            if (!img) continue;

            // always enabled so 모든 칸이 보임
            img.enabled = true;

            // pick sprite
            Sprite sp = emptySlotIcon; // default for empty
            var def = inventory.Get(i);
            if (def != null)
            {
                sp = def.icon ? def.icon : (fallbackIcon ? fallbackIcon : emptySlotIcon);
            }

            img.sprite = sp;

            // alpha highlight
            var c = img.color;
            c.a = (i == activeIdx) ? (activeColorA / 255f) : (inactiveColorA / 255f);
            img.color = c;
        }
    }

    private int ComputeInventoryHash()
    {
        if (!inventory || inventory.slots == null) return 0x24681357;

        unchecked
        {
            int h = 17;
            h = h * 31 + inventory.activeIndex;

            int n = Mathf.Min(inventory.slots.Count, slotIcons != null ? slotIcons.Length : inventory.slots.Count);
            for (int i = 0; i < n; i++)
            {
                var def = inventory.slots[i].def;
                int id = def ? def.GetInstanceID() : 0;
                h = h * 31 + id;
            }
            return h;
        }
    }
}
