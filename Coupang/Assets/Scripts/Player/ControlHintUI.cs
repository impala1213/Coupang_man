// Assets/Scripts/UI/ControlHintUI.cs
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class ControlHintUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Player controller used to query state.")]
    public PlayerController player;
    [Tooltip("Optional cached inventory. If null, resolved from player.")]
    public InventorySystem inventory;
    [Tooltip("Optional carrier reference. If null, resolved from player each frame.")]
    public CarrierController carrier;

    [Header("UI")]
    [Tooltip("Text label used to display control hints.")]
    public TextMeshProUGUI label;
    [Tooltip("Optional canvas group for fade in/out.")]
    public CanvasGroup canvasGroup;
    [Tooltip("Fade speed for alpha changes.")]
    public float fadeSpeed = 12f;

    [Header("Texts - General Items")]
    [Tooltip("Shown when aiming at a normal world item.")]
    public string pickUpItemText = "Press E to pick up";
    [Tooltip("Shown when holding a usable item (e.g., tool/consumable).")]
    public string useItemText = "LMB to use";
    [Tooltip("Shown when holding any droppable item.")]
    public string dropItemText = "Press G to drop";

    [Header("Texts - Lever")]
    [Tooltip("Shown when lever has focus.")]
    public string leverText = "Press E to start";

    [Header("Texts - Carrier")]
    [Tooltip("Shown when aiming at a carrier item on the ground.")]
    public string pickUpCarrierText = "Press E to pick up";
    [Tooltip("Shown when aiming at or equipping a carrier, to open its contents.")]
    public string openCarrierText = "Hold E to open";
    [Tooltip("Shown when carrier is equipped on player.")]
    public string dropCarrierEquippedText = "Hold G to drop";

    void Awake()
    {
        if (!player)
            player = FindFirstObjectByType<PlayerController>();

        if (!inventory && player)
            inventory = player.inventory;

        if (!carrier && player)
            carrier = player.carrier;

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

        // Keep carrier reference in sync with player if needed
        if (!carrier && player)
            carrier = player.carrier;

        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        // 1) Lever has focus ⊥ lever prompt
        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        if (InteractionLock.LeverHasFocus)
        {
            Show(leverText);
            return;
        }

        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        // 2) Aiming at a world item
        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        if (player.FindInteractCandidate(out var world))
        {
            if (BuildWorldItemHint(world, out string worldHint))
            {
                Show(worldHint);
                return;
            }
        }

        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        // 3) No world item / lever: check held item
        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        if (BuildHeldItemHint(out string heldHint))
        {
            Show(heldHint);
            return;
        }

        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        // 4) Nothing interesting: hide
        // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
        Hide();
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // World item hints
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    private bool BuildWorldItemHint(WorldItem world, out string text)
    {
        text = null;
        if (!world) return false;

        var def = world.definition;
        if (def == null)
        {
            text = pickUpItemText;
            return true;
        }

        // Carrier item on the ground
        if (def.isCarrier)
        {
            // Press E to pick up
            // Hold E to open
            text = $"{pickUpCarrierText}\n{openCarrierText}";
            return true;
        }

        // Normal item on the ground
        text = pickUpItemText;
        return true;
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // Held item / equipped carrier hints
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    private bool BuildHeldItemHint(out string text)
    {
        text = null;
        if (!inventory) return false;

        var def = inventory.ActiveDef();
        if (def == null)
            return false;

        // 式式 Carrier equipped on player 式式式式式式式式式式式式式式
        if (def.isCarrier && carrier != null)
        {
            // Hold G to drop
            // Hold E to open
            text = $"{dropCarrierEquippedText}\n{openCarrierText}";
            return true;
        }

        // 式式 Normal held item 式式式式式式式式式式式式式式式式式式式式式式式式
        bool isUsable = IsUsableItem(def);

        if (isUsable)
        {
            // LMB to use
            // Press G to drop
            text = $"{useItemText}\n{dropItemText}";
            return true;
        }
        else
        {
            // Non-usable, but droppable
            text = dropItemText;
            return true;
        }
    }

    /// <summary>
    /// Determines whether an item definition should be considered "usable"
    /// for showing "LMB to use" hint.
    /// You can adjust this rule as needed.
    /// </summary>
    private bool IsUsableItem(ItemDefinition def)
    {
        if (def == null) return false;

        // Example rule:
        // - Tools and consumables are usable
        // - Cargo is not
        // - Special can be either; assume usable by default
        switch (def.itemType)
        {
            case ItemType.Tool:
            case ItemType.Consumable:
            case ItemType.Special:
                return true;
            case ItemType.Cargo:
            default:
                return false;
        }
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // UI show/hide helpers
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
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
