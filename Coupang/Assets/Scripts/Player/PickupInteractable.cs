using UnityEngine;

[DisallowMultipleComponent]
public class PickupInteractable : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("If null, it will search WorldItem on this object or parents.")]
    public WorldItem worldItem;

    public WorldItem GetWorldItem()
    {
        if (worldItem != null) return worldItem;
        worldItem = GetComponent<WorldItem>();
        if (worldItem != null) return worldItem;
        worldItem = GetComponentInParent<WorldItem>();
        return worldItem;
    }

    public bool TryPick(InventorySystem inventory)
    {
        if (!inventory) return false;

        var wi = GetWorldItem();
        if (!wi) return false;

        return inventory.TryPickupWorldItem(wi);
    }

    // Keep legacy hook if something calls it
    public void OnPicked() { }
}
