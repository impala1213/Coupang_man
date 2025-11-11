// Assets/Scripts/UI/InventoryUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class InventoryUI : MonoBehaviour
{
    [Header("Binding")]
    public InventorySystem inventory;            // 비워두면 자동 바인딩
    public bool autoBind = true;

    [Header("UI Slots (assign in Inspector)")]
    public Image[] slotIcons;                    // 아이콘 N개
    public Slider[] slotDurability;              // 내구도 슬라이더 N개
    public TextMeshProUGUI[] slotDurabilityText; // (선택) 내구도 텍스트 N개

    [Header("Icons")]
    public Sprite emptySlotIcon;
    public Sprite fallbackIcon;

    [Header("Highlight")]
    [Range(0, 255)] public int activeColorA = 255;
    [Range(0, 255)] public int inactiveColorA = 160;

    [Header("Durability UI")]
    public bool showDurabilityText = true;
    public Color durabilityFillColor = new Color(0.2f, 0.85f, 0.2f, 1f);

    private int _lastHash = int.MinValue;

    void Awake()
    {
        if (autoBind && !inventory)
            inventory = FindFirstObjectByType<InventorySystem>();

        InitSlidersStyle();

        // 이벤트 기반 즉시 갱신
        if (inventory) inventory.OnInventoryChanged += Refresh;

        ForceRefresh();
    }

    void OnDestroy()
    {
        if (inventory) inventory.OnInventoryChanged -= Refresh;
    }

    void Update()
    {
        // 이벤트가 있어도 안전하게 폴백
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
        int n = (slotIcons != null) ? slotIcons.Length : 0;
        if (n == 0) return;

        if (!inventory)
        {
            for (int i = 0; i < n; i++)
            {
                SetIcon(i, emptySlotIcon, false);
                SetDurability(i, false, 0f, 0, 0);
            }
            return;
        }

        int activeIdx = Mathf.Clamp(inventory.activeIndex, 0, Mathf.Max(0, n - 1));

        for (int i = 0; i < n; i++)
        {
            var def = inventory.Get(i);
            bool hasItem = (def != null);

            // 아이콘 처리
            Sprite sp = hasItem
                ? (def.icon ? def.icon : (fallbackIcon ? fallbackIcon : emptySlotIcon))
                : emptySlotIcon;
            SetIcon(i, sp, i == activeIdx);

            // 내구도 처리: 아이템이 있을 때만 스냅샷 조회
            int cur = -1, max = -1;
            bool hasDurSnapshot = hasItem && TryGetDurabilitySnapshotAt(i, out cur, out max);
            float ratio = (hasDurSnapshot && max > 0) ? Mathf.Clamp01((float)cur / max) : 0f;

            // 아이템이 있고 + 내구도도 있을 때만 바/텍스트 활성화
            SetDurability(i, hasDurSnapshot, ratio, cur, max);
        }
    }

    // ── helpers
    private void SetIcon(int index, Sprite sprite, bool isActive)
    {
        if (slotIcons == null || index < 0 || index >= slotIcons.Length) return;
        var img = slotIcons[index];
        if (!img) return;

        img.enabled = true;
        img.sprite = sprite;

        var c = img.color;
        c.a = isActive ? (activeColorA / 255f) : (inactiveColorA / 255f);
        img.color = c;
    }

    private void SetDurability(int index, bool hasDur, float ratio, int cur, int max)
    {
        // Slider
        if (slotDurability != null && index >= 0 && index < slotDurability.Length)
        {
            var s = slotDurability[index];
            if (s)
            {
                s.interactable = false;
                s.minValue = 0f; s.maxValue = 1f; s.wholeNumbers = false;
                s.value = hasDur ? ratio : 0f;
                s.gameObject.SetActive(hasDur); // ← 핵심: 내구도 없으면 숨김

                if (s.fillRect)
                {
                    var fillImg = s.fillRect.GetComponent<Image>();
                    if (fillImg) fillImg.color = durabilityFillColor;
                }
            }
        }

        // Text
        if (slotDurabilityText != null && index >= 0 && index < slotDurabilityText.Length)
        {
            var t = slotDurabilityText[index];
            if (t)
            {
                if (hasDur && showDurabilityText)
                {
                    t.text = $"{cur}/{max}";
                    t.gameObject.SetActive(true);
                }
                else
                {
                    t.text = "";
                    t.gameObject.SetActive(false);
                }
            }
        }
    }


    private void InitSlidersStyle()
    {
        if (slotDurability == null) return;
        for (int i = 0; i < slotDurability.Length; i++)
        {
            var s = slotDurability[i];
            if (!s) continue;
            s.interactable = false;
            s.minValue = 0f; s.maxValue = 1f; s.wholeNumbers = false;
            s.transition = Selectable.Transition.None;
            s.navigation = new Navigation { mode = Navigation.Mode.None };

            if (s.fillRect)
            {
                var fillImg = s.fillRect.GetComponent<Image>();
                if (fillImg) fillImg.color = durabilityFillColor;
            }
        }
    }

    private bool TryGetDurabilitySnapshotAt(int index, out int cur, out int max)
    {
        cur = 0; max = 0;
        if (!inventory || inventory.slots == null) return false;
        if (index < 0 || index >= inventory.slots.Count) return false;

        var stack = inventory.slots[index].stack;
        if (stack == null) return false;

        cur = stack.durCurrent;
        max = stack.durMax;
        return (max > 0 && cur >= 0);
    }

    private int ComputeInventoryHash()
    {
        if (!inventory || inventory.slots == null) return 0x24681357;

        unchecked
        {
            int h = 17;
            h = h * 31 + inventory.activeIndex;

            int n = Mathf.Min(inventory.slots.Count, (slotIcons != null ? slotIcons.Length : inventory.slots.Count));
            for (int i = 0; i < n; i++)
            {
                var stack = inventory.slots[i].stack;
                int defId = (stack != null && stack.def) ? stack.def.GetInstanceID() : 0;
                int durSig = (stack != null) ? ((stack.durMax << 16) ^ (stack.durCurrent & 0xFFFF)) : 0;

                h = h * 31 + defId;
                h = h * 31 + durSig;
            }
            return h;
        }
    }
}
