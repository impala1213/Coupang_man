using UnityEngine;

public class PickupInteractable : MonoBehaviour
{
    public ItemData itemData;
    public bool destroyOnPickup = true;

    public ItemData GetItemData() => itemData;

    public void OnPicked()
    {
        if (destroyOnPickup) Destroy(gameObject);
    }
}
