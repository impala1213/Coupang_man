// Assets/Scripts/Player/PlayerController.cs
using System;
using System.Reflection;
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

    [Header("Drop / Throw (G)")]
    [Tooltip("If G is held longer than this, it becomes a throw (instead of a drop).")]
    public float throwHoldTime = 0.35f;

    [Tooltip("Extra hold time after throwHoldTime to reach full throw power.")]
    public float throwChargeTime = 1.25f;

    [Tooltip("Min/Max throw force (m/s).")]
    public float throwMinForce = 6f;
    public float throwMaxForce = 18f;

    [Tooltip("Upward bias added to aim direction. Higher = more lob arc.")]
    public float throwMinUpBias = 0.05f;
    public float throwMaxUpBias = 0.35f;

    [Tooltip("Min/Max spin magnitude.")]
    public float throwMinSpin = 2f;
    public float throwMaxSpin = 10f;

    [Tooltip("Radius to find the freshly spawned dropped item.")]
    public float throwFindRadius = 2.0f;

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

    // normal item drop/throw (G tap/hold)
    private bool itemHoldActive;
    private float itemHoldTimer;
    private int itemHoldIndex;

    // Optional: definition-based carry lock support (reflection)
    private static bool s_carryKindChecked;
    private static FieldInfo s_carryKindField;

    // ItemData reflection cache (NO compile-time dependency)
    private static bool s_itemDataChecked;
    private static Type s_itemDataType;
    private static FieldInfo s_itemDataDefinitionField;

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

        // Must run every frame to detect G release and decide drop vs throw.
        HandleItemDropThrowHold();

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

        if (IsControlLocked)
        {
            if (flashlight.IsOn) flashlight.SetOn(false);
            return;
        }

        if (Input.GetKeyDown(flashlightKey))
        {
            if (energy != null && energy.IsDepleted) return;
            flashlight.Toggle();
        }
    }

    void UpdateEnergyDrainUnitsAndTick()
    {
        if (!energy) return;

        int units = Mathf.Max(0, idleDrainUnits);

        if (isSprinting) units += Mathf.Max(0, sprintDrainUnits);
        if (flashlight != null && flashlight.IsOn) units += Mathf.Max(0, flashlightDrainUnits);

        energy.SetDrainUnits(units);
        energy.Tick(Time.deltaTime);

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
        if (blockInput)
        {
            moveLocal = Vector3.zero;
            moveWorld = Vector3.zero;
            isSprinting = false;
            baseSpeed = 0f;
        }

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
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);
        else
            knockbackVelocity = Vector3.zero;

        if (isKnockedDown)
        {
            knockdownTimer -= Time.deltaTime;
            if (knockdownTimer <= 0f)
                isKnockedDown = false;
        }
    }

    void HandleHotbar()
    {
        if (IsControlLocked) return;
        if (!inventory) return;

        if (IsCarryLockedByDefinition(inventory.ActiveDef()))
            return;

        if (itemHoldActive)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        if (IsControlLocked) return;

        // E: interactable first, otherwise pickup
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (currentInteractable != null)
            {
                interactUseHeld = currentInteractable.IsHoldInteraction;
                currentInteractable.OnUsePressed(this);
            }
            else
            {
                if (IsCarryLockedByDefinition(inventory != null ? inventory.ActiveDef() : null))
                    return;

                Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
                if (cam && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit,
                    interactDistance, interactMask, QueryTriggerInteraction.Collide))
                {
                    // Primary pickup path
                    WorldItem worldItem = hit.collider.GetComponentInParent<WorldItem>();

                    // Fallback pickup path: object has ItemData but Player asm cannot reference it directly -> reflection
                    if (!worldItem)
                    {
                        ItemDefinition def = TryGetItemDefinitionFromItemDataReflection(hit.collider);
                        if (def != null)
                        {
                            Transform root = hit.collider.transform;

                            var pi = hit.collider.GetComponentInParent<PickupInteractable>();
                            if (pi != null) root = pi.transform;

                            worldItem = root.GetComponent<WorldItem>();
                            if (!worldItem) worldItem = root.gameObject.AddComponent<WorldItem>();
                            worldItem.definition = def;
                        }
                    }

                    if (worldItem && inventory != null)
                    {
                        inventory.TryPickupWorldItem(worldItem);
                    }
                    else
                    {
                        if (debugLever)
                            Debug.Log($"[PlayerController] Pickup failed: hit={hit.collider.name}, hasWorldItem={(worldItem != null)}, hasInventory={(inventory != null)}");
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

        // G: carrier hold drop OR item tap-drop / hold-throw
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (!inventory) return;

            ItemDefinition activeDef = inventory.ActiveDef();
            bool hasCarrierItem = (activeDef != null && activeDef.isCarrier);

            itemHoldActive = false;
            itemHoldTimer = 0f;

            if (hasCarrierItem)
            {
                if (!carrier)
                {
                    Debug.LogWarning("[PlayerController] Active item is marked as carrier, but 'carrier' reference is null.");
                    return;
                }

                carrierDropHolding = true;
                carrierDropTimer = 0f;
            }
            else
            {
                carrierDropHolding = false;
                carrierDropTimer = 0f;

                itemHoldActive = true;
                itemHoldTimer = 0f;
                itemHoldIndex = inventory.activeIndex;
            }
        }

        // LMB: use hook (intentionally empty)
        if (Input.GetMouseButtonDown(0))
        {
            // Item use system hook
        }
    }

    // Decide drop vs throw based on hold duration and camera direction.
    void HandleItemDropThrowHold()
    {
        if (!itemHoldActive) return;

        if (IsControlLocked || inventory == null)
        {
            itemHoldActive = false;
            itemHoldTimer = 0f;
            return;
        }

        if (Input.GetKey(KeyCode.G))
        {
            itemHoldTimer += Time.deltaTime;
            return;
        }

        // Released
        itemHoldActive = false;

        inventory.SetActiveIndex(itemHoldIndex);
        ItemDefinition def = inventory.ActiveDef();
        if (def == null)
        {
            itemHoldTimer = 0f;
            return;
        }

        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        Vector3 aimForward = cam ? cam.transform.forward : transform.forward;
        aimForward.Normalize();

        Transform origin = dropOrigin ? dropOrigin : transform;

        // Must match InventorySystem.DropActiveItem spawn position
        Vector3 expectedDropPos = origin
            ? origin.position + aimForward * 0.6f + Vector3.up * 0.5f
            : transform.position + aimForward * 0.6f + Vector3.up * 0.5f;

        bool wantsThrow = (itemHoldTimer >= throwHoldTime);
        bool canThrow = !def.isCarrier && IsOneHandByDefinitionOrDefault(def);

        // Always drop (spawns world object + removes from inventory)
        inventory.DropActiveItem(origin, aimForward);

        if (wantsThrow && canThrow)
        {
            float denom = Mathf.Max(0.01f, throwChargeTime);
            float charge01 = Mathf.Clamp01((itemHoldTimer - throwHoldTime) / denom);

            float force = Mathf.Lerp(throwMinForce, throwMaxForce, charge01);

            // short hold -> higher lob, long hold -> flatter
            float upBias = Mathf.Lerp(throwMaxUpBias, throwMinUpBias, charge01);

            float spin = Mathf.Lerp(throwMinSpin, throwMaxSpin, charge01);

            Vector3 dir = (aimForward + Vector3.up * upBias).normalized;

            TryApplyThrowToFreshDrop(def, expectedDropPos, dir, force, spin);
        }

        itemHoldTimer = 0f;
    }

    private void TryApplyThrowToFreshDrop(ItemDefinition def, Vector3 expectedDropPos, Vector3 dir, float force, float spin)
    {
        if (def == null) return;

        WorldItem best = null;
        float bestDist = float.MaxValue;

        var all = WorldItem.AllWorldItems;
        int count = all != null ? all.Count : 0;

        for (int i = 0; i < count; i++)
        {
            var wi = all[i];
            if (!wi) continue;
            if (wi.IsCarrierItem) continue;
            if (wi.isOnCarrier) continue;
            if (wi.definition != def) continue;

            float d = Vector3.Distance(wi.transform.position, expectedDropPos);
            if (d <= throwFindRadius && d < bestDist)
            {
                bestDist = d;
                best = wi;
            }
        }

        if (!best) return;

        if (!best.rb) best.rb = best.GetComponent<Rigidbody>();
        if (!best.rb) best.rb = best.gameObject.AddComponent<Rigidbody>();

        best.rb.isKinematic = false;
        best.rb.useGravity = true;

        Vector3 playerVel = controller != null ? controller.velocity : Vector3.zero;
        Vector3 initialVel = dir * Mathf.Max(0f, force) + playerVel * 0.15f;
        best.ArmIgnoreBreakForThrower(transform.root, 0.25f);
        best.rb.linearVelocity = initialVel;

        if (spin > 0f)
            best.rb.angularVelocity = UnityEngine.Random.onUnitSphere * spin;
    }

    void HandleCarrierDropHold()
    {
        if (IsControlLocked) return;
        if (!carrierDropHolding) return;

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

        float required = carrierDropBaseHold;
        if (carrier.totalWeight > 0f)
        {
            required += carrier.totalWeight * carrierDropPerWeight;
            required = Mathf.Min(required, carrierDropMaxHold);
        }

        carrierDropTimer += Time.deltaTime;

        if (carrierDropTimer >= required)
        {
            Transform origin = dropOrigin ? dropOrigin : transform;
            Vector3 pos = origin.position + transform.forward * 0.6f + Vector3.up * 0.3f;
            Vector3 fwd = transform.forward;

            bool removed = inventory.DropCarrierAsBundle(origin, fwd);
            if (removed)
            {
                carrier.DropAsBundle(pos, fwd);
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
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit,
            carrierInspectMaxDistance, ~0, QueryTriggerInteraction.Collide))
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
        if (leverMask.value != 0) combinedMask |= leverMask;

        PlayerInteractableBase hitInteractable = null;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit,
            dist, combinedMask, QueryTriggerInteraction.Collide))
        {
            hitInteractable = hit.collider.GetComponentInParent<PlayerInteractableBase>();
            if (debugLever)
                Debug.Log($"Interact ray hit: {hit.collider.name}, interactable = {(hitInteractable ? hitInteractable.name : "none")}");
        }
        else
        {
            if (debugLever) Debug.Log("Interact ray hit nothing");
        }

        if (hitInteractable != currentInteractable)
        {
            if (currentInteractable != null) currentInteractable.OnFocusExit(this);

            currentInteractable = hitInteractable;

            if (currentInteractable != null) currentInteractable.OnFocusEnter(this);
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
            dir = -transform.forward;

        dir.Normalize();

        Vector3 horizontal = dir * force;
        Vector3 vertical = Vector3.up * (force * knockbackUpFactor);

        knockbackVelocity = horizontal + vertical;

        isKnockedDown = true;
        knockdownTimer = knockdownDuration;

        if (carrier != null && causeCargoSpill && carrier.HasAnyMounted())
            carrier.SpillAllOnCarrierDrop(transform.position, dir);
    }

    public bool FindInteractCandidate(out WorldItem world)
    {
        world = null;

        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        if (!cam) return false;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit,
            interactDistance, interactMask, QueryTriggerInteraction.Collide))
        {
            world = hit.collider.GetComponentInParent<WorldItem>();
            return world != null;
        }

        return false;
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // ItemData reflection pickup fallback (no compile-time dependency)
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    private static ItemDefinition TryGetItemDefinitionFromItemDataReflection(Collider col)
    {
        EnsureItemDataReflection();

        if (s_itemDataType == null || s_itemDataDefinitionField == null)
            return null;

        // Search upward manually
        Transform t = col.transform;
        while (t != null)
        {
            Component comp = t.GetComponent(s_itemDataType);
            if (comp != null)
            {
                object v = s_itemDataDefinitionField.GetValue(comp);
                return v as ItemDefinition;
            }
            t = t.parent;
        }

        return null;
    }

    private static void EnsureItemDataReflection()
    {
        if (s_itemDataChecked) return;
        s_itemDataChecked = true;

        // Find ItemData type by name from loaded assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = asm.GetType("ItemData", false);
                if (type != null)
                {
                    s_itemDataType = type;
                    break;
                }
            }
            catch { /* ignore */ }
        }

        if (s_itemDataType == null)
            return;

        // ItemData has public ItemDefinition definition;
        s_itemDataDefinitionField = s_itemDataType.GetField("definition", BindingFlags.Public | BindingFlags.Instance);
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // carryKind reflection helpers (optional)
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    private static bool IsCarryLockedByDefinition(ItemDefinition def)
    {
        if (def == null) return false;
        if (def.isCarrier) return false;

        EnsureCarryKindField();

        if (s_carryKindField == null) return false;

        object v = s_carryKindField.GetValue(def);
        if (v == null) return false;

        string name = v.ToString();
        return name == "TwoHand" || name == "TwoPersonCargo";
    }

    private static bool IsOneHandByDefinitionOrDefault(ItemDefinition def)
    {
        if (def == null) return true;

        EnsureCarryKindField();
        if (s_carryKindField == null) return true;

        object v = s_carryKindField.GetValue(def);
        if (v == null) return true;

        return v.ToString() == "OneHand";
    }

    private static void EnsureCarryKindField()
    {
        if (s_carryKindChecked) return;
        s_carryKindChecked = true;

        s_carryKindField = typeof(ItemDefinition).GetField("carryKind", BindingFlags.Public | BindingFlags.Instance);
    }
}
