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
    // ── Identity/UI ─────────────────────────────────────────────────────────────
    [Header("Identity")]
    public string itemId;                  // unique key (e.g., "food_crate_a")
    public string displayName;             // UI fallback name (localization can override)
    public Sprite icon;                    // UI icon (optional)

    // ── Classification ─────────────────────────────────────────────────────────
    [Header("Classification")]
    public ItemType itemType = ItemType.Tool;         // gameplay branching (used by inventory/use logic)
    public ItemCategory category = ItemCategory.None; // taxonomy for quests/economy
    public bool isCarrier = false;                    // true if this item is the carrier itself

    // ── Inventory ──────────────────────────────────────────────────────────────
    [Header("Inventory")]
    [Range(1, 5)] public int slotSize = 1;           // contiguous hotbar slots required

    // ── Physics & Carrier Stacking ─────────────────────────────────────────────
    [Header("Physics & Carrier Stacking")]
    [Tooltip("Mass used in balance/difficulty calculations (kg).")]
    public float weight = 1f;

    [Tooltip("Box proxy used WHEN LOADED ON THE CARRIER (meters). Y is stack height increment.")]
    public Vector3 stackSize = new Vector3(0.40f, 0.40f, 0.30f);

    [Tooltip("Optional prefab to visualize this item on the carrier instead of a primitive cube. (지금 구조에서는 사용 안 하지만 남겨 둠)")]
    public GameObject stackVisualPrefab;

    [Tooltip("Fallback color for the primitive cube when no stack prefab is provided.")]
    public Color stackColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    // ── Carrier Mount (optional) ───────────────────────────────────────────────
    [Header("Carrier Mount (optional)")]
    [Tooltip("CarrierSlot 피벗 기준 로컬 위치. (0,0,0)이면 슬롯 중심에 둠.")]
    public Vector3 carrierLocalPosition = Vector3.zero;

    [Tooltip("CarrierSlot 피벗 기준 로컬 회전(Euler).")]
    public Vector3 carrierLocalEuler = Vector3.zero;

    [Tooltip("CarrierSlot 피벗 기준 로컬 스케일. (0,0,0)이면 (1,1,1)로 처리.")]
    public Vector3 carrierLocalScale = Vector3.one;

    // ── World Prefab ───────────────────────────────────────────────────────────
    [Header("World Prefab")]
    [Tooltip("Prefab used when the item exists in the world or is dropped.")]
    public GameObject worldPrefab;

    // ── Breakability ───────────────────────────────────────────────────────────
    [Header("Breakability")]
    public bool breakable = false;
    public float breakImpulseThreshold = 15f;
    public GameObject brokenPrefab;

    // ── Economy (optional) ─────────────────────────────────────────────────────
    [Header("Economy (optional)")]
    public int baseValue = 0;

    // Convenience: by design every item can be loaded on the carrier
    public bool IsCargoLike => true;
}
