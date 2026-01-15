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

    [Header("Energy")]
    [Tooltip("Optional. If null, will be resolved from this GameObject.")]
    public Energy energy;

    [Tooltip("Energy drain units while idle (time limit).")]
    public int idleDrainUnits = 1;

    [Tooltip("Additional drain units while sprinting (Shift).")]
    public int sprintDrainUnits = 3;

    [Tooltip("Additional drain units while flashlight is ON (F).")]
    public int flashlightDrainUnits = 2;

    [Header("Flashlight")]
    [Tooltip("Optional. If null, will be resolved from children.")]
    public PlayerFlashlight flashlight;

    public KeyCode flashlightKey = KeyCode.F;

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

    [Header("Carrier Drop (Hold)")]
    public float carrierDropBaseHold = 0.5f;   // minimum hold time
    public float carrierDropPerWeight = 0.05f; // extra seconds per kg
    public float carrierDropMaxHold = 3f;      // max required hold time

    [Header("Carrier Inspect")]
    public float carrierInspectHoldTime = 0.6f;
    public float carrierInspectMaxDistance = 4f;
    public CarrierSlotUI carrierSlotUI;

    [Header("Debug")]
    public bool debugLever;

    private CharacterController controller;
    private float verticalVel;

    private bool isSprinting;

    private Vector3 knockbackVelocity;
    private bool isKnockedDown;
    private float knockdownTimer;

    public bool IsControlLocked
    {
        get
        {
            if (isKnockedDown) return true;
            if (energy != null && energy.IsDepleted) return true;
            return false;
        }
    }

    private PlayerInteractableBase currentInteractable;
    private bool interactUseHeld;

    // carrier drop hold state
    private bool carrierDropHolding;
    private float carrierDropTimer;

    // carrier slot inspect state
    private bool carrierInspecting;
    private float carrierInspectTimer;
    private CarrierController carrierInspectTarget;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (!energy)
            energy = GetComponent<Energy>();

        if (!flashlight)
            flashlight = GetComponentInChildren<PlayerFlashlight>(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (!inventory)
            inventory = FindFirstObjectByType<InventorySystem>();

        if (!carrierSlotUI)
            carrierSlotUI = FindFirstObjectByType<CarrierSlotUI>();
    }

    void Update()
    {
        HandleFlashlightToggle();
        Move();
        UpdateInteractableFocusAndTick();
        HandleHotbar();
        HandleActions();
        HandleCarrierDropHold();
        HandleCarrierInspect();

        if (carrier)
        {
            Vector3 pos = transform.position;
            Vector3 vel = controller.velocity;
            carrier.ReportGroundedState(controller.isGrounded, pos, vel);
        }
    }


    void HandleFlashlightToggle()
    {
        if (!flashlight) return;

        // If control is locked, force flashlight OFF.
        if (IsControlLocked)
        {
            if (flashlight.IsOn) flashlight.SetOn(false);
            return;
        }

        if (Input.GetKeyDown(flashlightKey))
        {
            // Do not allow turning ON when energy is already depleted.
            if (energy != null && energy.IsDepleted) return;
            flashlight.Toggle();
        }
    }

    void UpdateEnergyDrainUnitsAndTick()
    {
        if (!energy) return;

        // Base drain is always active while energy is above 0.
        int units = Mathf.Max(0, idleDrainUnits);

        if (isSprinting) units += Mathf.Max(0, sprintDrainUnits);
        if (flashlight != null && flashlight.IsOn) units += Mathf.Max(0, flashlightDrainUnits);

        energy.SetDrainUnits(units);
        energy.Tick(Time.deltaTime);

        // If energy depleted this frame, lock controls immediately and turn off flashlight.
        if (energy.IsDepleted && flashlight != null && flashlight.IsOn)
            flashlight.SetOn(false);

        if (energy.IsDepleted)
            isSprinting = false;
    }
    void Move()
    {
        bool grounded = controller.isGrounded;
        bool blockInput = IsControlLocked;

        float h = blockInput ? 0f : Input.GetAxisRaw("Horizontal");
        float v = blockInput ? 0f : Input.GetAxisRaw("Vertical");

        Vector3 moveLocal = new Vector3(h, 0f, v).normalized;
        Vector3 moveWorld = transform.TransformDirection(moveLocal);

        bool wantsSprint = (!blockInput && Input.GetKey(KeyCode.LeftShift));
        isSprinting = wantsSprint && (moveLocal.sqrMagnitude > 0.001f);
        float baseSpeed = isSprinting ? sprintSpeed : walkSpeed;
        UpdateEnergyDrainUnitsAndTick();
        blockInput = IsControlLocked;
        if (blockInput) { moveLocal = Vector3.zero; moveWorld = Vector3.zero; isSprinting = false; baseSpeed = 0f; }

        Vector3 horizontalVel = moveWorld * baseSpeed;
        Vector3 knockHoriz = new Vector3(knockbackVelocity.x, 0f, knockbackVelocity.z);
        horizontalVel += knockHoriz;

        controller.Move(horizontalVel * Time.deltaTime);

        if (grounded && verticalVel < 0f) verticalVel = -2f;
        if (!blockInput && Input.GetKeyDown(KeyCode.Space) && grounded) verticalVel = jumpForce;
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
        if (IsControlLocked) return;
        if (!inventory) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        if (IsControlLocked) return;
        // ���� E key: lever first, otherwise pickup item ����
        
        // E key: interactable first, otherwise pickup item
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (currentInteractable != null)
            {
                interactUseHeld = currentInteractable.IsHoldInteraction;
                currentInteractable.OnUsePressed(this);
            }
            else
            {
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
                        inventory.TryPickupWorldItem(worldItem);
                    }
                }
            }
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            if (currentInteractable != null && currentInteractable.IsHoldInteraction)
            {
                interactUseHeld = false;
                currentInteractable.OnUseReleased(this);
            }
        }


        // ���� G key: normal item drop OR carrier drop (hold) ����
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (!inventory) return;

            ItemDefinition activeDef = inventory.ActiveDef();
            bool hasCarrierItem = (activeDef != null && activeDef.isCarrier);

            if (hasCarrierItem)
            {
                // Carrier item is selected. Start hold-based drop.
                if (!carrier)
                {
                    Debug.LogWarning("[PlayerController] Active item is marked as carrier, but 'carrier' reference is null. Carrier will not be dropped.");
                    return;
                }

                carrierDropHolding = true;
                carrierDropTimer = 0f;
            }
            else
            {
                // Normal item: tap to drop immediately
                Vector3 fwd = transform.forward;
                Transform origin = dropOrigin ? dropOrigin : transform;
                inventory.DropActiveItem(origin, fwd);
            }
        }

        // LMB: use active item (hook placeholder)
        if (Input.GetMouseButtonDown(0))
        {
            // active item use hook
        }
    }

    void HandleCarrierDropHold()
    {
        if (IsControlLocked) return;
        if (!carrierDropHolding)
            return;

        // If the player releases G while holding, cancel the drop
        if (!Input.GetKey(KeyCode.G))
        {
            carrierDropHolding = false;
            carrierDropTimer = 0f;
            return;
        }

        if (carrier == null || inventory == null)
        {
            carrierDropHolding = false;
            carrierDropTimer = 0f;
            return;
        }

        ItemDefinition activeDef = inventory.ActiveDef();
        if (activeDef == null || !activeDef.isCarrier)
        {
            carrierDropHolding = false;
            carrierDropTimer = 0f;
            return;
        }

        // Compute required hold duration based on carrier total weight
        float required = carrierDropBaseHold;
        if (carrier.totalWeight > 0f)
        {
            required += carrier.totalWeight * carrierDropPerWeight;
            required = Mathf.Min(required, carrierDropMaxHold);
        }

        carrierDropTimer += Time.deltaTime;

        if (carrierDropTimer >= required)
        {
            // Actually drop the carrier as a bundle
            Transform origin = dropOrigin ? dropOrigin : transform;
            Vector3 pos = origin.position + transform.forward * 0.6f + Vector3.up * 0.3f;
            Vector3 fwd = transform.forward;

            bool removed = inventory.DropCarrierAsBundle(origin, fwd);
            if (removed)
            {
                carrier.DropAsBundle(pos, fwd);

                // Clear references from inventory and player
                inventory.carrier = null;
                carrier = null;
            }

            carrierDropHolding = false;
            carrierDropTimer = 0f;
        }
    }

    void HandleCarrierInspect()
    {
        if (IsControlLocked) return;
        if (carrierSlotUI == null) return;

        bool eHeld = Input.GetKey(KeyCode.E);

        if (!eHeld)
        {
            carrierInspectTimer = 0f;
            carrierInspectTarget = null;
            if (carrierInspecting)
            {
                carrierInspecting = false;
                carrierSlotUI.Close();
            }
            return;
        }

        // If a lever is focused, do not inspect carrier
        if (currentInteractable != null)
        {
            carrierInspectTimer = 0f;
            carrierInspectTarget = null;
            if (carrierInspecting)
            {
                carrierInspecting = false;
                carrierSlotUI.Close();
            }
            return;
        }

        // If there is an interactable world item directly in front, prefer pickup
        if (FindInteractCandidate(out var _))
        {
            carrierInspectTimer = 0f;
            carrierInspectTarget = null;
            if (carrierInspecting)
            {
                carrierInspecting = false;
                carrierSlotUI.Close();
            }
            return;
        }

        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam)
        {
            carrierInspectTimer = 0f;
            carrierInspectTarget = null;
            if (carrierInspecting)
            {
                carrierInspecting = false;
                carrierSlotUI.Close();
            }
            return;
        }

        CarrierController target = null;
        if (Physics.Raycast(
                cam.transform.position,
                cam.transform.forward,
                out RaycastHit hit,
                carrierInspectMaxDistance,
                ~0,
                QueryTriggerInteraction.Collide))
        {
            target = hit.collider.GetComponentInParent<CarrierController>();
        }

        if (target == null)
        {
            carrierInspectTimer = 0f;
            carrierInspectTarget = null;
            if (carrierInspecting)
            {
                carrierInspecting = false;
                carrierSlotUI.Close();
            }
            return;
        }

        if (carrierInspectTarget != target)
        {
            carrierInspectTarget = target;
            carrierInspectTimer = 0f;
        }

        carrierInspectTimer += Time.deltaTime;

        if (!carrierInspecting && carrierInspectTimer >= carrierInspectHoldTime)
        {
            carrierInspecting = true;
            carrierSlotUI.Open(target, transform);
        }
    }

    
    void UpdateInteractableFocusAndTick()
    {
        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam)
        {
            ClearInteractableFocus();
            return;
        }

        float dist = leverInteractDistance > 0f ? leverInteractDistance : interactDistance;

        LayerMask combinedMask = interactMask;
        if (leverMask.value != 0)
        {
            combinedMask |= leverMask;
        }

        PlayerInteractableBase hitInteractable = null;

        if (Physics.Raycast(
                cam.transform.position,
                cam.transform.forward,
                out RaycastHit hit,
                dist,
                combinedMask,
                QueryTriggerInteraction.Collide))
        {
            hitInteractable = hit.collider.GetComponentInParent<PlayerInteractableBase>();

            if (debugLever)
            {
                Debug.Log($"Interact ray hit: {hit.collider.name}, interactable = {(hitInteractable ? hitInteractable.name : "none")}");
            }
        }
        else
        {
            if (debugLever)
            {
                Debug.Log("Interact ray hit nothing");
            }
        }

        if (hitInteractable != currentInteractable)
        {
            if (currentInteractable != null)
            {
                currentInteractable.OnFocusExit(this);
            }

            currentInteractable = hitInteractable;

            if (currentInteractable != null)
            {
                currentInteractable.OnFocusEnter(this);
            }
        }

        if (currentInteractable != null && interactUseHeld && currentInteractable.IsHoldInteraction)
        {
            currentInteractable.TickWhileHeld(this, Time.deltaTime);
        }
    }

    void ClearInteractableFocus()
    {
        if (currentInteractable != null)
        {
            currentInteractable.OnFocusExit(this);
            currentInteractable = null;
        }
    }

    /// <summary>
    /// Teleport the player safely (disables CharacterController briefly).
    /// </summary>
    public void TeleportTo(Vector3 worldPosition, Quaternion worldRotation, bool resetKnockback = true)
    {
        if (resetKnockback)
        {
            knockbackVelocity = Vector3.zero;
            isKnockedDown = false;
            knockdownTimer = 0f;
        }

        if (controller != null)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            controller.enabled = wasEnabled;
        }
        else
        {
            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        verticalVel = 0f;
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