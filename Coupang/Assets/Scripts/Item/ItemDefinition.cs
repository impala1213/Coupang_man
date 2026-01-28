// Assets/Scripts/Item/ItemDefinition.cs
using UnityEngine;

/// <summary>
/// Item category used by missions and UI.
/// (Requested categories)
/// </summary>
public enum ItemCategory
{
    None = 0,
    General = 1,
    Explosive = 2,
    Bio = 3,
    Toxic = 4,
    Fragile = 5
}

/// <summary>
/// Carry restriction rules (single source of truth).
/// slotSize is inventory space only.
/// </summary>
public enum CarryKind
{
    OneHand = 0,        // free swap, can throw, right-arm enabled
    TwoHand = 1,        // swap locked, pickup locked, right-arm disabled
    TwoPersonCargo = 2  // NOT stored in inventory: handled by coop carry system
}

[CreateAssetMenu(menuName = "Game/Item Definition", fileName = "NewItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    public string itemId;
    public string displayName;
    public Sprite icon;

    [Header("Classification")]
    public ItemCategory category = ItemCategory.None;


    [Tooltip("True if this item IS the carrier itself (backpack).")]
    public bool isCarrier = false;

    [Header("Carry Rules (do NOT use slotSize for this)")]
    public CarryKind carryKind = CarryKind.OneHand;

    [Header("Inventory Space")]
    [Range(1, 5)] public int slotSize = 1;

    [Header("Physics & Carrier Stacking")]
    public float weight = 1f;

    public Vector3 stackSize = new Vector3(0.40f, 0.40f, 0.30f);
    public GameObject stackVisualPrefab;
    public Color stackColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Carrier Mount (optional)")]
    public Vector3 carrierLocalPosition = Vector3.zero;
    public Vector3 carrierLocalEuler = Vector3.zero;
    public Vector3 carrierLocalScale = Vector3.one;

    [Header("World Prefab")]
    public GameObject worldPrefab;

    [Header("Breakability")]
    public bool breakable = false;
    public float breakImpulseThreshold = 15f;
    public GameObject brokenPrefab;

    [Header("Economy (optional)")]
    public int baseValue = 0;

    // Only based on carryKind (NOT slotSize)
    public bool IsSwapLocked => !isCarrier && carryKind != CarryKind.OneHand;
    public bool IsPickupLocked => !isCarrier && carryKind != CarryKind.OneHand;

    // No "cargo-use blocking" here on purpose.
    // If an item has no use/attack component, LMB will naturally do nothing.

    public bool CanThrow => !isCarrier && carryKind == CarryKind.OneHand;

    private void OnValidate()
    {
        if (isCarrier && carryKind == CarryKind.TwoPersonCargo)
            carryKind = CarryKind.OneHand;
    }
}
