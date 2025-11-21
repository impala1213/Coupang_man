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

    // 式式 Physics 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Physics")]
    [Tooltip("Mass used in balance/difficulty calculations (kg).")]
    public float weight = 1f;

    // 式式 Carrier Mount (optional) 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Carrier Mount (optional)")]
    [Tooltip("CarrierSlot pivot local position on carrier. (0,0,0) = slot center.")]
    public Vector3 carrierLocalPosition = Vector3.zero;

    [Tooltip("CarrierSlot pivot local rotation (Euler).")]
    public Vector3 carrierLocalEuler = Vector3.zero;

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
