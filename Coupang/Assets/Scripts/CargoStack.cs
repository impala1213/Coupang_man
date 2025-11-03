using UnityEngine;

public class CargoStack : MonoBehaviour
{
    [Header("References")]
    public PlayerController playerController;
    public GameObject cargoPrefab;

    [Header("Stacking Settings")]
    public float stackHeightOffset = 0.5f; // Vertical distance between stacked items

    private void Start()
    {
        if (playerController == null)
        {
            Debug.LogError("Player Controller is not assigned to CargoStack.");
        }
    }

    // --- Public Functions ---

    public void ToggleCarrier()
    {
        if (playerController.isCarrierEquipped)
        {
            UnequipCarrier();
        }
        else
        {
            EquipCarrier();
        }
    }

    public void EquipCarrier()
    {
        playerController.isCarrierEquipped = true;
        Debug.Log("Carrier Equipped: Switched to 3rd Person and Balance System is ON.");
        // PlayerController.cs will handle the camera view switch.
    }

    public void UnequipCarrier()
    {
        // Remove all cargo first
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
        playerController.isCarrierEquipped = false;
        Debug.Log("Carrier Unequipped: Switched to 1st Person and Balance System is OFF.");
        // PlayerController.cs will handle the camera view switch.
    }

    public void AddCargo()
    {
        if (!playerController.isCarrierEquipped)
        {
            Debug.LogWarning("Cannot add cargo. Carrier is not equipped.");
            return;
        }

        Vector3 newCargoPosition = Vector3.zero;
        int currentCount = transform.childCount;

        // Calculate the position for the new cargo item
        newCargoPosition.y = stackHeightOffset * (currentCount + 0.5f);

        if (cargoPrefab != null)
        {
            GameObject newCargo = Instantiate(cargoPrefab, transform);
            newCargo.transform.localPosition = newCargoPosition;

            // Set cargo tag or layer if needed for visual differentiation
            // newCargo.layer = LayerMask.NameToLayer("Cargo"); 
        }
        else
        {
            Debug.LogError("Cargo Prefab is not assigned.");
        }
    }

    public void RemoveCargo()
    {
        if (transform.childCount > 0)
        {
            Destroy(transform.GetChild(transform.childCount - 1).gameObject);
        }

        if (transform.childCount == 1) // If only one left after removal, maybe unequip?
        {
            // You can add logic here to automatically unequip if the stack is empty.
        }
    }
}