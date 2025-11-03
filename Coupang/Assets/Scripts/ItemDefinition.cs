using UnityEngine;


public enum ItemType { Tool, Consumable, Cargo }


[CreateAssetMenu(menuName = "Game/ItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    public string itemId;
    public string displayName;
    public Sprite icon;
    [Range(1, 5)] public int slotSize = 1; // 연속 칸 수
    public ItemType itemType = ItemType.Tool;
    public bool isCarrier = false; // 지게 아이템 여부


    [Header("Physics & Balance")]
    public float weight = 1f; // kg 가정
    public float height = 0.3f; // 적재 시 유효 높이(m)


    [Header("World Prefab")]
    public GameObject worldPrefab;


    [Header("Breakability")]
    public bool breakable = false;
    public float breakImpulseThreshold = 15f;
    public GameObject brokenPrefab;
}