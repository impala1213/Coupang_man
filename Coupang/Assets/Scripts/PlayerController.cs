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
    public LayerMask interactMask;   // Interactable layer

    [Header("Pickup Tuning")]
    [Tooltip("Forgiving sphere pickup radius used when selecting items (meters).")]
    [Range(0.2f, 0.8f)] public float pickupRadius = 0.45f;

    // If true, forgiving sphere pickup is only used in third-person.
    // In first-person we fall back to a thin ray (precision aim).
    public bool forgivingPickupOnlyInThird = true;


    [Header("Refs")]
    public CameraSwitcher cameraSwitcher;
    public InventorySystem inventory;
    public CarrierController carrier;
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

        if (carrier && carrier.IsActive)
        {
            float left = Input.GetMouseButton(0) ? 1f : 0f;
            float right = Input.GetMouseButton(1) ? 1f : 0f;
            carrier.ApplyBalanceInput(left, right);
        }
    }

    void Move()
    {
        bool grounded = controller.isGrounded;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 move = (transform.right * h + transform.forward * v).normalized;

        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);

        if (grounded && verticalVel < 0f) verticalVel = -2f;
        if (Input.GetKeyDown(KeyCode.Space) && grounded) verticalVel = jumpForce;

        verticalVel += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    void HandleHotbar()
    {
        if (!inventory) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        // E: interact (pickup or mount)
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (FindInteractCandidate(out var worldItem))
            {
                if (carrier && carrier.IsActive)
                {
                    if (worldItem.definition && worldItem.definition.isCarrier) return;
                    carrier.TryMount(worldItem);
                }
                else
                {
                    inventory?.TryPickupWorldItem(worldItem);
                }
            }
        }

        // G: spill cargo, then drop the carrier straight down, then exit 3rd-person
        if (Input.GetKeyDown(KeyCode.G))
        {
            Transform originT = dropOrigin ? dropOrigin : transform;
            Vector3 fwd = transform.forward;
            Vector3 spillOrigin = originT.position + Vector3.up * 0.3f;

            if (carrier && carrier.IsActive)
            {
                if (inventory != null)
                {
                    // 1) spill loaded cargo with realistic motion
                    if (carrier.HasMountedCargo)
                        carrier.SpillAllAt(spillOrigin, fwd * 0.2f);

                    // 2) select carrier slot and drop the CARRIER ITSELF straight down
                    int carrierIdx = inventory.FindFirstCarrierIndex();
                    if (carrierIdx >= 0) inventory.SetActiveIndex(carrierIdx);

                    bool ok = inventory.DropActiveItem(originT, Vector3.zero); // ← no forward impulse
                    carrier.SetActiveMode(false);
                    inventory.SelectFirstNonCarrierSlot();

                    if (!ok) Debug.LogWarning("[PlayerController] DropActiveItem failed for carrier.");
                }
            }
            else
            {
                // normal drop for non-carrier
                inventory?.DropActiveItem(originT, fwd);
            }
        }



        // LMB: use active item only when not in carrier mode
        if (Input.GetMouseButtonDown(0) && !(carrier && carrier.IsActive))
        {
            inventory?.UseActiveItem(this);
        }
    }

    /// <summary>
    /// Shared candidate finder (used by UI too). Always uses FIRST-PERSON camera for picking.
    /// </summary>
    public bool FindInteractCandidate(out WorldItem item)
    {
        item = null;

        // Always use first-person camera transform for picking
        Camera fpCam = cameraSwitcher ? cameraSwitcher.firstPersonCam : null;
        if (!fpCam) fpCam = Camera.main;

        // LOS: exclude Interactable (items) and this Player layer
        int playerMask = 1 << gameObject.layer;
        LayerMask losMask = (~interactMask) & (~playerMask);

        bool isThird = cameraSwitcher ? cameraSwitcher.IsThirdPerson() : false;

        // Use forgiving radius only in third-person if the toggle is on.
        float radius = (forgivingPickupOnlyInThird && !isThird) ? 0.01f : pickupRadius;

        // In forgiving phase, exclude carrier from auto selection so back-mounted carrier doesn't get picked
        return InteractFinder.FindCandidate(
            rayCam: fpCam,
            player: transform,
            maxDistance: interactDistance,
            rayRadius: radius,
            interactMask: interactMask,
            losBlockMask: losMask,
            excludeCarrierInAutoSelect: true,
            best: out item
        );
    }


}
