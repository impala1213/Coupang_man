using UnityEngine;

public enum ItemCategory
{
    General,
    Food,
    Fragile,
    Tech,
    Fuel,
    Quest,
    Carrier // special: equippable carrier
}

[CreateAssetMenu(menuName = "Data/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    public string itemId;
    public string displayName;

    [Header("Visuals")]
    public Sprite icon;
    public GameObject worldPrefab;

    [Header("Economy")]
    public int price = 100;

    [Header("Physics")]
    public float weight = 1f;

    [Header("Inventory")]
    [Tooltip("Number of inventory slots this item occupies (contiguous).")]
    [Range(1, 5)] public int slotSize = 1;

    [Header("Category")]
    public ItemCategory category = ItemCategory.General;

    [Header("Durability")]
    public int durabilityMax = 100;
}
