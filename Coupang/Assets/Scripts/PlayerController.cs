// Assets/Scripts/Player/PlayerController.cs
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpForce = 5f;
    public float gravity = -19.62f;

    [Header("Interaction")]
    public float interactDistance = 3f;
    public LayerMask interactMask;

    [Header("Refs")]
    public CameraSwitcher cameraSwitcher; // still used to get active camera ray origin
    public InventorySystem inventory;
    public CarrierController carrier;     // assign same as InventorySystem.carrier
    public Transform dropOrigin;

    private CharacterController controller;
    private float verticalVel;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Move();
        HandleHotbar();
        HandleActions();

        // report grounded state & kinematics to carrier (for fall/impact spill logic)
        if (carrier)
        {
            Vector3 pos = transform.position;
            Vector3 vel = controller.velocity;
            carrier.ReportGroundedState(controller.isGrounded, pos, vel);
        }
    }

    void Move()
    {
        bool grounded = controller.isGrounded;
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 moveLocal = new Vector3(h, 0f, v).normalized;
        Vector3 moveWorld = transform.TransformDirection(moveLocal);

        float baseSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        controller.Move(moveWorld * baseSpeed * Time.deltaTime);

        if (grounded && verticalVel < 0) verticalVel = -2f;
        if (Input.GetKeyDown(KeyCode.Space) && grounded) verticalVel = jumpForce;
        verticalVel += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    void HandleHotbar()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
            if (cam && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
            {
                var worldItem = hit.collider.GetComponentInParent<WorldItem>();
                if (worldItem) inventory.TryPickupWorldItem(worldItem);
            }
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            Vector3 fwd = transform.forward;
            inventory.DropActiveItem(dropOrigin ? dropOrigin : transform, fwd);
        }

        // LMB: use active item (generic)
        if (Input.GetMouseButtonDown(0))
        {
            // implement item use here if needed (consumable/fire/etc.)
        }
    }
    public bool FindInteractCandidate(out WorldItem world)
    {
        world = null;

        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam) return false;

        if (Physics.Raycast(
            cam.transform.position,
            cam.transform.forward,
            out RaycastHit hit,
            interactDistance,
            interactMask,
            QueryTriggerInteraction.Ignore))
        {
            world = hit.collider.GetComponentInParent<WorldItem>();
            return world != null;
        }

        return false;
    }

}
