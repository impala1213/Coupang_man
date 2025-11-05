// Assets/Scripts/Item/ItemDefinition.cs
using UnityEngine;
public enum ItemType
{
    Tool,
    Consumable,
    Cargo,
    Special
}

public enum ItemCategory
{
    None = 0,
    Food,
    Weapon,
    Electronics,
    Luxury,
    Energy,
    Medicine,
    Antique,
    Ore,
    Carrier
}

[CreateAssetMenu(menuName = "Game/Item Definition", fileName = "NewItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    // 式式 Identity/UI 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Identity")]
    public string itemId;                  // unique key (e.g., "food_crate_a")
    public string displayName;             // UI fallback name (localization can override)
    public Sprite icon;                    // UI icon (optional)

    // 式式 Classification 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Classification")]
    public ItemType itemType = ItemType.Tool;         // gameplay branching (used by inventory/use logic)
    public ItemCategory category = ItemCategory.None; // taxonomy for quests/economy
    public bool isCarrier = false;                    // true if this item is the carrier itself

    // 式式 Inventory 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Inventory")]
    [Range(1, 5)] public int slotSize = 1;           // contiguous hotbar slots required

    // 式式 Physics & Carrier Stacking 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Physics & Carrier Stacking")]
    [Tooltip("Mass used in balance/difficulty calculations (kg).")]
    public float weight = 1f;

    [Tooltip("Box proxy used WHEN LOADED ON THE CARRIER (meters). Y is stack height increment.")]
    public Vector3 stackSize = new Vector3(0.40f, 0.40f, 0.30f);

    [Tooltip("Optional prefab to visualize this item on the carrier instead of a primitive cube.")]
    public GameObject stackVisualPrefab;

    [Tooltip("Fallback color for the primitive cube when no stack prefab is provided.")]
    public Color stackColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    // 式式 World Prefab 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("World Prefab")]
    [Tooltip("Prefab used when the item exists in the world or is dropped.")]
    public GameObject worldPrefab;
    // 式式 Breakability 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Breakability")]
    public bool breakable = false;
    public float breakImpulseThreshold = 15f;
    public GameObject brokenPrefab;

    // 式式 Economy (optional) 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Economy (optional)")]
    public int baseValue = 0;

    // Convenience: by design every item can be loaded on the carrier
    public bool IsCargoLike => true;
}
