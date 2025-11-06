using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class InteractionPromptUI : MonoBehaviour
{
    [Header("References")]
    public PlayerController player;           // auto-wire in Awake
    public CarrierController carrier;         // auto-wire in Awake

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
        if (!label) label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup) canvasGroup.alpha = 0f;
        else if (label) label.text = string.Empty;
    }

    void Update()
    {
        if (!player || !label) { Hide(); return; }

        if (player.FindInteractCandidate(out var world) && BuildText(world, out string text))
        {
            Show(text);
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
        string baseText;

        bool carrierActive = carrier && carrier.IsActive;

        if (def != null && def.isCarrier) baseText = pickUpCarrierText;
        else baseText = carrierActive ? loadText : pickUpText;

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
