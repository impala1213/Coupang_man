// Assets/Scripts/Player/PlayerDeathDropper.cs
using UnityEngine;

[RequireComponent(typeof(Health))]
public class PlayerDeath : MonoBehaviour
{
    [Header("References")]
    public InventorySystem inventory;      // 옵션, 비어 있으면 자동 찾기
    public CarrierController carrier;      // 옵션, 비어 있으면 자동 찾기
    public Transform dropOrigin;           // 옵션, 비어 있으면 this.transform 사용

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();

        if (inventory == null)
            inventory = GetComponent<InventorySystem>();

        if (carrier == null)
            carrier = GetComponent<CarrierController>();

        if (dropOrigin == null)
            dropOrigin = transform;

        if (health != null)
        {
            health.OnDeath += OnPlayerDeath;
        }
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= OnPlayerDeath;
        }
    }

    private void OnPlayerDeath(Health h)
    {
        Transform origin = dropOrigin != null ? dropOrigin : transform;
        Vector3 forward = transform.forward;

        // 1) 캐리어에 적재된 짐 전부 쏟기
        if (carrier != null && carrier.HasAnyMounted())
        {
            carrier.SpillAllOnCarrierDrop(origin.position, forward);
        }

        // 2) 인벤토리 전 슬롯 아이템 드롭
        if (inventory != null)
        {
            DropAllInventory(inventory, origin, forward);
        }
    }

    private void DropAllInventory(InventorySystem inv, Transform origin, Vector3 forward)
    {
        // InventorySystem 기존 API만 사용:
        // slotCount, SetActiveIndex, DropActiveItem
        int slots = inv.slotCount;

        for (int i = 0; i < slots; i++)
        {
            inv.SetActiveIndex(i);
            inv.DropActiveItem(origin, forward);
        }
    }
}
