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

    [Header("Lever")]
    public float leverInteractDistance = 3f;
    public LayerMask leverMask;

    [Header("Refs")]
    public CameraSwitcher cameraSwitcher;
    public InventorySystem inventory;
    public CarrierController carrier;
    public Transform dropOrigin;

    [Header("Knockback")]
    public bool canBeKnockedBack = true;
    public float knockbackDamping = 5f;
    public float knockbackUpFactor = 0.5f;
    public float knockdownDuration = 0.6f;

    [Header("Debug")]
    public bool debugLever;

    private CharacterController controller;
    private float verticalVel;

    private Vector3 knockbackVelocity;
    private bool isKnockedDown;
    private float knockdownTimer;

    private UniversalLever currentLever;
    private bool leverUseHeld;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Move();
        UpdateLeverFocusAndTick();
        HandleHotbar();
        HandleActions();

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
        bool blockInput = isKnockedDown;

        float h = blockInput ? 0f : Input.GetAxisRaw("Horizontal");
        float v = blockInput ? 0f : Input.GetAxisRaw("Vertical");

        Vector3 moveLocal = new Vector3(h, 0f, v).normalized;
        Vector3 moveWorld = transform.TransformDirection(moveLocal);

        float baseSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

        Vector3 horizontalVel = moveWorld * baseSpeed;
        Vector3 knockHoriz = new Vector3(knockbackVelocity.x, 0f, knockbackVelocity.z);
        horizontalVel += knockHoriz;

        controller.Move(horizontalVel * Time.deltaTime);

        if (grounded && verticalVel < 0f)
            verticalVel = -2f;

        if (!blockInput && Input.GetKeyDown(KeyCode.Space) && grounded)
            verticalVel = jumpForce;

        verticalVel += gravity * Time.deltaTime;

        float totalY = verticalVel + knockbackVelocity.y;
        controller.Move(Vector3.up * totalY * Time.deltaTime);

        if (knockbackVelocity.sqrMagnitude > 0.01f)
        {
            knockbackVelocity = Vector3.Lerp(
                knockbackVelocity,
                Vector3.zero,
                knockbackDamping * Time.deltaTime
            );
        }
        else
        {
            knockbackVelocity = Vector3.zero;
        }

        if (isKnockedDown)
        {
            knockdownTimer -= Time.deltaTime;
            if (knockdownTimer <= 0f)
            {
                isKnockedDown = false;
            }
        }
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
        // Optional: block all interactions while knocked down
        if (isKnockedDown)
            return;

        // E pressed
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (currentLever != null)
            {
                leverUseHeld = true;
                currentLever.OnUsePressed();
                return;
            }

            // 2) Otherwise, try to pick up a world item
            Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
            if (cam && Physics.Raycast(
                    cam.transform.position,
                    cam.transform.forward,
                    out RaycastHit hit,
                    interactDistance,
                    interactMask,
                    QueryTriggerInteraction.Collide))
            {
                var worldItem = hit.collider.GetComponentInParent<WorldItem>();
                if (worldItem && inventory != null)
                {
                    Debug.Log("Try Pickup");
                    inventory.TryPickupWorldItem(worldItem);
                }
            }
        }

        // E released
        if (Input.GetKeyUp(KeyCode.E))
        {
            if (currentLever != null)
            {
                leverUseHeld = false;
                currentLever.OnUseReleased();
            }
        }

        // Drop active item
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (!inventory) return;
            Vector3 fwd = transform.forward;
            Transform origin = dropOrigin ? dropOrigin : transform;
            inventory.DropActiveItem(origin, fwd);
        }

        // LMB: active item use hook
        if (Input.GetMouseButtonDown(0))
        {
            // implement active item use here if needed
        }
    }

    void UpdateLeverFocusAndTick()
    {
        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam)
        {
            ClearLeverFocus();
            return;
        }

        float dist = leverInteractDistance > 0f ? leverInteractDistance : interactDistance;

        LayerMask combinedMask = interactMask;
        if (leverMask.value != 0)
        {
            combinedMask |= leverMask;
        }

        UniversalLever hitLever = null;

        if (Physics.Raycast(
                cam.transform.position,
                cam.transform.forward,
                out RaycastHit hit,
                dist,
                combinedMask,
                QueryTriggerInteraction.Collide))
        {
            hitLever = hit.collider.GetComponentInParent<UniversalLever>();

            if (debugLever)
            {
                Debug.Log($"Lever ray hit: {hit.collider.name}, lever = {(hitLever ? hitLever.name : "none")}");
            }
        }
        else
        {
            if (debugLever)
            {
                Debug.Log("Lever ray hit nothing");
            }
        }

        if (hitLever != currentLever)
        {
            // exit old lever focus
            if (currentLever != null)
            {
                currentLever.FocusExit();
            }

            currentLever = hitLever;
            leverUseHeld = false;

            // enter new lever focus
            if (currentLever != null)
            {
                currentLever.FocusEnter();
            }
        }

        if (currentLever != null && leverUseHeld)
        {
            currentLever.Tick(Time.deltaTime);
        }
    }

    void ClearLeverFocus()
    {
        if (currentLever != null)
        {
            currentLever.FocusExit();
            currentLever = null;
        }
        leverUseHeld = false;
    }

    public void ApplyKnockback(Vector3 sourcePosition, float force, bool causeCargoSpill)
    {
        if (!canBeKnockedBack || controller == null) return;

        Vector3 dir = transform.position - sourcePosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = -transform.forward;
        }
        dir.Normalize();

        Vector3 horizontal = dir * force;
        Vector3 vertical = Vector3.up * (force * knockbackUpFactor);

        knockbackVelocity = horizontal + vertical;

        isKnockedDown = true;
        knockdownTimer = knockdownDuration;

        if (carrier != null && causeCargoSpill && carrier.HasAnyMounted())
        {
            carrier.SpillAllOnCarrierDrop(transform.position, dir);
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
            QueryTriggerInteraction.Collide))
        {
            world = hit.collider.GetComponentInParent<WorldItem>();
            return world != null;
        }

        return false;
    }
}
