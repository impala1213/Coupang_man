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
    public LayerMask interactMask;   // items, carriers, levers all use this

    [Header("Refs")]
    public CameraSwitcher cameraSwitcher;
    public InventorySystem inventory;
    public CarrierController carrier;
    public Transform dropOrigin;

    private CharacterController controller;
    private float verticalVel;

    // Lever focus
    private UniversalLever currentLever;

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
        UpdateInteractionFocus();
        HandleActions();

        if (currentLever != null)
        {
            currentLever.Tick(Time.deltaTime);
        }

        if (carrier)
        {
            Vector3 pos = transform.position;
            Vector3 vel = controller.velocity;
            carrier.ReportGroundedState(controller.isGrounded, pos, vel);
        }
    }

    void Move()
    {
        if (!controller || !controller.enabled) return;
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
        // E pressed
        if (Input.GetKeyDown(KeyCode.E))
        {
            // If a lever is focused, start lever hold and skip pickup
            if (currentLever != null)
            {
                currentLever.OnUsePressed();
                return;
            }

            // Safety: if lever focus lock is on, skip item pickup
            if (InteractionLock.LeverHasFocus)
            {
                return;
            }

            Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
            if (cam && Physics.Raycast(
                    cam.transform.position,
                    cam.transform.forward,
                    out RaycastHit hit,
                    interactDistance,
                    interactMask,
                    QueryTriggerInteraction.Collide)) // allow triggers for items/carriers as well
            {
                var worldItem = hit.collider.GetComponentInParent<WorldItem>();
                if (worldItem)
                {
                    inventory.TryPickupWorldItem(worldItem);
                }
            }
        }

        // E released
        if (Input.GetKeyUp(KeyCode.E))
        {
            if (currentLever != null)
            {
                currentLever.OnUseReleased();
            }
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            Vector3 fwd = transform.forward;
            inventory.DropActiveItem(dropOrigin ? dropOrigin : transform, fwd);
        }

        if (Input.GetMouseButtonDown(0))
        {
            // use active item (to be implemented)
        }
    }

    void UpdateInteractionFocus()
    {
        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam)
        {
            ClearLeverFocus();
            return;
        }

        // Same ray / mask as items
        if (Physics.Raycast(
                cam.transform.position,
                cam.transform.forward,
                out RaycastHit hit,
                interactDistance,
                interactMask,
                QueryTriggerInteraction.Collide)) // triggers allowed
        {
            UniversalLever lever = hit.collider.GetComponentInParent<UniversalLever>();
            if (lever != currentLever)
            {
                if (currentLever != null)
                {
                    currentLever.OnFocusExit();
                }

                currentLever = lever;

                if (currentLever != null)
                {
                    currentLever.OnFocusEnter();
                }
            }
        }
        else
        {
            ClearLeverFocus();
        }
    }

    void ClearLeverFocus()
    {
        if (currentLever != null)
        {
            currentLever.OnFocusExit();
            currentLever = null;
        }
    }

    public bool FindInteractCandidate(out WorldItem world)
    {
        world = null;

        // While lever has focus, do not report world items
        if (InteractionLock.LeverHasFocus)
        {
            return false;
        }

        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam) return false;

        if (Physics.Raycast(
            cam.transform.position,
            cam.transform.forward,
            out RaycastHit hit,
            interactDistance,
            interactMask,
            QueryTriggerInteraction.Collide))
        {
            world = hit.collider.GetComponentInParent<WorldItem>();
            return world != null;
        }

        return false;
    }
}
