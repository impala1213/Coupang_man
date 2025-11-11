using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class InteractionPromptUI : MonoBehaviour
{
    [Header("References")]
    public PlayerController player;           // auto-wire in Awake
    public CarrierController carrier;         // kept for compatibility (not required)
    private InventorySystem inventory;        // resolved from player in Awake

    [Header("UI")]
    public TextMeshProUGUI label;             // TMP text
    public CanvasGroup canvasGroup;           // optional
    public float fadeSpeed = 12f;
    public bool showItemName = true;

    [Header("Texts (English)")]
    public string pickUpText = "Press E to pick up";
    public string loadText = "Press E to load";
    public string pickUpCarrierText = "Press E to pick up carrier";

    void Awake()
    {
        if (!player) player = FindFirstObjectByType<PlayerController>();
        if (!carrier && player) carrier = player.carrier;
        if (player && !inventory) inventory = player.inventory;

        if (!label) label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup) canvasGroup.alpha = 0f;
        else if (label) label.text = string.Empty;
    }

    void Update()
    {
        if (!player || !label) { Hide(); return; }

        if (player.FindInteractCandidate(out var world))
        {
            if (BuildText(world, out var prompt))
                Show(prompt);
            else
                Hide();
        }
        else
        {
            Hide();
        }
    }

    bool BuildText(WorldItem world, out string text)
    {
        text = null;
        if (!world) return false;

        var def = world.definition;
        string baseText = pickUpText;

        // 1) 캐리어 자체면 항상 "pick up carrier"
        if (def != null && def.isCarrier)
        {
            baseText = pickUpCarrierText;
        }
        else
        {
            // 2) 일반 아이템: 인벤토리 연속 슬롯이 없고, 캐리어 아이템을 보유 중이면 "load"
            if (inventory != null && def != null)
            {
                int need = Mathf.Clamp(def.slotSize, 1, inventory.slotCount);
                bool hasSpace = inventory.HasSpaceFor(need);
                bool hasCarrier = inventory.HasCarrierInInventory();

                baseText = (!hasSpace && hasCarrier) ? loadText : pickUpText;
            }
        }

        text = (showItemName && def && !string.IsNullOrEmpty(def.displayName))
             ? $"{baseText} [{def.displayName}]"
             : baseText;

        return true;
    }

    void Show(string s)
    {
        if (label && label.text != s) label.text = s;
        if (canvasGroup) canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, fadeSpeed * Time.deltaTime);
    }

    void Hide()
    {
        if (canvasGroup) canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, fadeSpeed * Time.deltaTime);
        else if (label && !string.IsNullOrEmpty(label.text)) label.text = string.Empty;
    }
}
