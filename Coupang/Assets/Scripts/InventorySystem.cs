using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class InventoryItem
{
    public ItemDefinition def;
    public int startIndex; // 첫 칸
    public int size; // 점유 칸 수
}


public interface IUsable
{
    void Use(PlayerController player);
}


public class InventorySystem : MonoBehaviour
{
    public int slotCount = 5;
    public InventoryUI ui;


    private InventoryItem[] slotRefs; // 각 칸이 어떤 아이템(헤드/필러 포함)을 가리키는지
    private List<InventoryItem> items = new List<InventoryItem>();
    private int activeSlot = 0;


    void Awake()
    {
        slotRefs = new InventoryItem[slotCount];
    }
    public void SetActiveSlot(int index)
    {
        activeSlot = Mathf.Clamp(index, 0, slotCount - 1);
        ui?.Refresh(this, activeSlot);
    }


    public InventoryItem GetActiveItem()
    {
        return slotRefs[activeSlot] != null && slotRefs[activeSlot].startIndex == activeSlot ? slotRefs[activeSlot] : null;
    }


    public bool TryPickupWorldItem(WorldItem world)
    {
        if (!world || world.definition == null) return false;
        if (TryAdd(world.definition, out var invItem))
        {
            world.OnPickedUp();
            ui?.Refresh(this, activeSlot);
            return true;
        }
        return false;
    }


    public bool TryAdd(ItemDefinition def, out InventoryItem invItem)
    {
        invItem = null;
        int need = Mathf.Clamp(def.slotSize, 1, slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            if (i + need > slotCount) break;
            bool can = true;
            for (int j = 0; j < need; j++) if (slotRefs[i + j] != null) { can = false; break; }
            if (!can) continue;


            invItem = new InventoryItem { def = def, startIndex = i, size = need };
            items.Add(invItem);
            for (int j = 0; j < need; j++) slotRefs[i + j] = invItem;
            if (activeSlot < i || activeSlot >= i + need) SetActiveSlot(i); // 새로 채운 곳으로 포커스 이동
            return true;
        }
        return false;
    }
    public bool DropActiveItem(Transform origin) => DropActiveItem(origin.position, origin.forward);
    public bool DropActiveItem(Vector3 pos, Vector3 forward)
    {
        var active = GetActiveItem();
        if (active == null) return false;
        DoDrop(active, pos + forward * 1f + Vector3.up * 0.5f, forward * 4f);
        return true;
    }


    void DoDrop(InventoryItem item, Vector3 pos, Vector3 impulse)
    {
        // 월드 프리팹 스폰
        GameObject go = item.def.worldPrefab ? Instantiate(item.def.worldPrefab, pos, Quaternion.identity) : new GameObject(item.def.displayName);
        var world = go.GetComponent<WorldItem>();
        if (!world)
        {
            world = go.AddComponent<WorldItem>();
            world.definition = item.def;
            var rb = go.GetComponent<Rigidbody>();
            if (!rb) rb = go.AddComponent<Rigidbody>();
            world.rb = rb;
            var col = go.GetComponent<Collider>();
            if (!col) col = go.AddComponent<BoxCollider>();
        }
        world.OnDropped(pos, impulse);


        // 인벤토리 비우기
        for (int j = 0; j < item.size; j++) slotRefs[item.startIndex + j] = null;
        items.Remove(item);
        ui?.Refresh(this, activeSlot);
    }
    public void UseActiveItem(PlayerController player)
    {
        var active = GetActiveItem();
        if (active == null) return;
        // 지게 아이템은 사용 시 장착 토글
        if (active.def.isCarrier)
        {
            var carrier = player.carrier;
            if (carrier) carrier.ToggleEquip(player.cameraSwitcher);
            return;
        }
        var usable = GetComponent<IUsable>(); // 확장: 아이템별 Use 핸들러는 별도 컴포넌트로 구현 가능
                                              // 간단 MVP: Consumable은 그냥 버리기
        if (active.def.itemType == ItemType.Consumable)
        {
            // TODO: 체력/스태미나 회복 등
            DropActiveItem(player.transform); // 시연을 위해 소비=버리기
        }
    }
    public void UseByDefinition(ItemDefinition def, PlayerController player)
    {
        // 인벤토리에 동일 정의가 있으면 그 아이템을 활성화 후 사용
        foreach (var it in items)
        {
            if (it.def == def)
            {
                SetActiveSlot(it.startIndex);
                UseActiveItem(player);
                return;
            }
        }
    }


    public IEnumerable<InventoryItem> EnumerateItems() => items;
    public InventoryItem GetSlotRef(int idx) => slotRefs[idx];
}
