using UnityEngine;
using UnityEngine.InputSystem;

public class CursorPickup : MonoBehaviour
{
    [Header("Ray Settings")]
    public Camera cam;
    public float maxDistance = 5f;
    public LayerMask pickupMask = ~0; // set a specific mask in Inspector if needed

    private void Reset()
    {
        cam = Camera.main;
    }

    public bool TryPickupUnderCursor(out PickupInteractable target)
    {
        target = null;
        if (!cam) cam = Camera.main;

        Vector2 pos = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Ray ray = cam.ScreenPointToRay(pos);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, pickupMask, QueryTriggerInteraction.Ignore))
        {
            target = hit.collider.GetComponentInParent<PickupInteractable>();
            if (target) return true;
        }
        return false;
    }
}
