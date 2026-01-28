// Assets/Scripts/UI/InteractionPromptUI.cs
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class InteractionPromptUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player controller used to query interact candidate.")]
    public PlayerController player;

    [Header("UI")]
    [Tooltip("Text label used to display item info.")]
    public TextMeshProUGUI label;
    [Tooltip("Optional canvas group for fade in/out.")]
    public CanvasGroup canvasGroup;
    [Tooltip("Fade speed for alpha changes.")]
    public float fadeSpeed = 12f;

    [Header("Format")]
    [Tooltip("If true, show item category (e.g., [Food]).")]
    public bool showCategory = true;
    [Tooltip("If true, show item display name.")]
    public bool showName = true;
    [Tooltip("If true, show item base value.")]
    public bool showValue = true;
    [Tooltip("Label used before numeric value.")]
    public string valueLabel = "Value";

    void Awake()
    {
        if (!player)
            player = FindFirstObjectByType<PlayerController>();

        if (!label)
            label = GetComponentInChildren<TextMeshProUGUI>(true);

        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        else if (label)
        {
            label.text = string.Empty;
        }
    }

    void Update()
    {
        if (!player || !label)
        {
            Hide();
            return;
        }

        // When a modal UI (lever/terminal) has focus, do not show item info.
        if (InteractionLock.LeverHasFocus || InteractionLock.TerminalHasFocus || InteractionLock.ModalUIOpen)
        {
            Hide();
            return;
        }

        // Use player's existing interact ray to find a world item.
        if (player.FindInteractCandidate(out var world))
        {
            if (BuildItemInfo(world, out string infoText))
            {
                Show(infoText);
            }
            else
            {
                Hide();
            }
        }
        else
        {
            Hide();
        }
    }

    /// <summary>
    /// Builds item info text from a WorldItem:
    /// e.g. "[Food] Apple  (Value: 30)"
    /// Only item data, no control hints.
    /// </summary>
    private bool BuildItemInfo(WorldItem world, out string text)
    {
        text = null;
        if (!world) return false;

        var def = world.definition;
        if (def == null) return false;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // Category
        if (showCategory)
        {
            string categoryStr = def.category.ToString();
            sb.Append('[').Append(categoryStr).Append("] ");
        }

        // Name
        if (showName)
        {
            string nameStr = string.IsNullOrEmpty(def.displayName)
                ? def.name
                : def.displayName;
            sb.Append(nameStr).Append(' ');
        }

        // Value
        if (showValue)
        {
            int value = def.baseValue;
            sb.Append('(').Append(valueLabel).Append(": ").Append(value).Append(')');
        }

        text = sb.ToString().Trim();
        return text.Length > 0;
    }

    private void Show(string s)
    {
        if (label && label.text != s)
            label.text = s;

        if (canvasGroup)
        {
            canvasGroup.alpha = Mathf.MoveTowards(
                canvasGroup.alpha,
                1f,
                fadeSpeed * Time.deltaTime
            );
        }
    }

    private void Hide()
    {
        if (canvasGroup)
        {
            canvasGroup.alpha = Mathf.MoveTowards(
                canvasGroup.alpha,
                0f,
                fadeSpeed * Time.deltaTime
            );
        }
        else if (label && !string.IsNullOrEmpty(label.text))
        {
            label.text = string.Empty;
        }
    }
}
