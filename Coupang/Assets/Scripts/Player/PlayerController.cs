// Assets/Scripts/Player/PlayerController.cs
using System;
using System.Reflection;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("Optional. If null, will be resolved from children.")]
    public Animator animator;

    [Tooltip("If true, PlayerController drives Animator parameters (locomotion + hold pose).")]
    public bool driveAnimatorParams = true;

    [Tooltip("Primary locomotion float param (commonly 'Speed').")]
    public string speedParam = "Speed";

    [Tooltip("Optional secondary float param for input magnitude 0..1 (commonly 'movement'). Leave empty to disable.")]
    public string movementParam = "movement";

    [Tooltip("Smoothing for animator float parameters (higher = snappier).")]
    public float animSmoothing = 12f;

    [Tooltip("Animator int param for upper-body hold pose: 0=None, 1=OneHand, 2=TwoHand. Leave empty to disable.")]
    public string holdPoseParam = "HoldPose";

    [Tooltip("Animator trigger param for knockdown (AnyState -> PlayerFallDown). Leave empty to disable.")]
    public string fallDownTriggerParam = "FallDown";

    [Tooltip("If >= 0, sets this layer's weight to 0 when HoldPose=0, else 1. Use for UpperBody layer.")]
    public int upperBodyLayerIndex = 1;

    private string _lastSpeedParam;
    private string _lastMovementParam;
    private string _lastHoldPoseParam;
    private string _lastFallDownTriggerParam;
    private int _speedHash;
    private int _movementHash;
    private int _holdPoseHash;
    private int _fallDownTriggerHash;
    private bool _animHasSpeed;
    private bool _animHasMovement;
    private bool _animHasHoldPose;
    private bool _animHasFallDownTrigger;
    private int _paramCacheKey;

    private float _animSpeed;
    private float _animMovement;

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
    [Tooltip("Fallback input lock duration if animator knockdown/stand states are not configured.")]
    public float knockdownDuration = 0.6f;

    [Header("Knockdown Input Lock (FallDown -> Stand)")]
    [Tooltip("If true, player input is locked while the Animator is in PlayerFallDown or PlayerStand (and during transitions).")]
    public bool lockInputUntilStandEnds = true;

    [Tooltip("Animator base layer index to check for FallDown/Stand states.")]
    public int knockdownAnimLayer = 0;

    [Tooltip("Animator state name for fall down (AnyState -> this).")]
    public string fallDownStateName = "PlayerFallDown";

    [Tooltip("Animator state name for stand up.")]
    public string standStateName = "PlayerStand";

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


[Header("Use / Attack Action (LMB + Throw Anim)")]
[Tooltip("If true, PlayerController can drive AttackReady(bool) + Attack(trigger) for usable items and charged throw.")]
public bool enableUseActions = true;

[Tooltip("Animator layer index used for AttackReady/Attack states (often UpperBody).")]
public int actionAnimLayerIndex = 1;

[Tooltip("Animator bool param that keeps player in AttackReady/prepare state while held.")]
public string attackReadyBoolParam = "AttackReady";

[Tooltip("Animator trigger param that starts the Attack animation.")]
public string attackTriggerParam = "Attack";

[Tooltip("Animator state name for the attack clip (used to lock attack input until it finishes).")]
public string actionAttackStateName = "PlayerActionAttack";

[Tooltip("Seconds required in AttackReady before attack can fire. (Quick tap queues release, attack fires after this.)")]
public float actionPrepareTime = 0.25f;

public float chargeMaxHoldTime = 1.0f; // seconds to reach full charge
[Tooltip("Fallback seconds after Attack starts to execute if no animation event is configured.")]
public float actionExecuteFallbackDelay = 0.12f;

[Tooltip("Safety timeout for ending the action even if state name tracking fails.")]
public float actionTotalTime = 0.8f;

[Tooltip("If true, blocks further attacks until the Animator has finished the attack state.")]
public bool unlockWhenAttackAnimEnds = true;

[Tooltip("If true, auto-add an animation event relay on the Animator GameObject (no extra script file needed).")]
public bool autoAttachAnimEventRelay = true;

    [Header("Debug")]
    public bool debugLever;

    private CharacterController controller;
    private float verticalVel;
    private bool isSprinting;

    private Vector3 knockbackVelocity;
    private bool isKnockedDown;
    private float knockdownTimer;

    // Animator-driven knockdown lock (keeps control locked through stand animation)
    private bool knockdownAnimLock;

    public bool IsControlLocked
    {
        get
        {
            if (isKnockedDown) return true;
            if (knockdownAnimLock) return true;
            if (energy != null && energy.IsDepleted) return true;
            if (InteractionLock.ModalUIOpen) return true;
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

// LMB use / attack + charged throw action state
private enum UseActionType { None, Primary, Throw }
private UseActionType _useAction = UseActionType.None;
private bool _usePreparing;
private bool _useReleaseQueued;
private float _useHeldTime;
private float _usePrepareStartTime;
private float _useAttackStartTime;
private float _useExecuteFallbackAt;
private float _useHardEndAt;
private bool _useExecuted;
private bool _useSawAttackState;

private IPlayerUsable _useUsable; // only for Primary
private bool _useWantsAnim;

private Vector3 _useAimForward;

// throw cached params (computed on release)
private int _throwIndex;
private ItemDefinition _throwDef;
private Vector3 _throwDir;
private float _throwForce;
private float _throwSpin;

// Animator param cache for attack control
private int _attackReadyHash;
private int _attackTriggerHash;
private bool _animHasAttackReady;
private bool _animHasAttackTrigger;
private int _attackParamCacheKey;


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

        if (!animator)
            animator = GetComponentInChildren<Animator>(true);

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

        RefreshAnimatorParamCache(force: true);

RefreshAttackAnimatorParamCache(force: true);
EnsureAnimEventRelay();
    }

    void Update()
    {
        UpdateKnockdownAnimLock();
        HandleFlashlightToggle();
        Move();
        UpdateInteractableFocusAndTick();

        HandleHotbar();
        HandleActions();
        TickUseAction();

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

        if (knockdownTimer > 0f)
            knockdownTimer -= Time.deltaTime;

        if (knockdownTimer < 0f)
            knockdownTimer = 0f;

        // Timer lock is a fallback. Animator lock (FallDown -> Stand) is handled separately.
        isKnockedDown = knockdownTimer > 0f;

        // Drive animator locomotion params from the same input/speed values used for movement.
        if (driveAnimatorParams)
        {
            // moveLocal is normalized, so magnitude is 0 or 1 in this controller.
            float move01 = moveLocal.magnitude;
            float desiredSpeed = move01 * baseSpeed;

            // During knockdown/control lock, force in-place idle and clear hold pose.
            if (IsControlLocked)
            {
                move01 = 0f;
                desiredSpeed = 0f;
            }

            UpdateAnimatorParams(desiredSpeed, move01);
        }
    }

    void HandleHotbar()
    {
        if (IsControlLocked) return;


// LMB: usable item action (hold=ready, release=attack)
if (enableUseActions)
{
    if (Input.GetMouseButtonDown(0))
        TryBeginPrimaryUsePrepare();

    if (Input.GetMouseButtonUp(0))
        OnPrimaryUseReleased();
}

        if (!inventory) return;

        if (IsCarryLockedByDefinition(inventory.ActiveDef()))
            return;

        if (itemHoldActive)
            return;


        if (_useAction != UseActionType.None)
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
            if (_useAction != UseActionType.None) return;
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

    // If the player holds long enough, convert this hold into a "throw action" (plays AttackReady/Attack),
    // so quick tap-drop remains animation-free.
    if (enableUseActions && _useAction == UseActionType.None)
    {
        ItemDefinition curDef = inventory.ActiveDef();
        bool canThrowNow = (curDef != null) && !curDef.isCarrier && IsOneHandByDefinitionOrDefault(curDef);

        if (canThrowNow && itemHoldTimer >= throwHoldTime)
        {
            BeginThrowPrepare(curDef, itemHoldIndex);
        }
    }

    return;
}



// Released
itemHoldActive = false;

// If we already converted to throw action (AttackReady), releasing should trigger Attack (and actual throw at animation event).
if (_useAction == UseActionType.Throw)
{
    OnThrowReleased();
    itemHoldTimer = 0f;
    return;
}

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

        if (InteractionLock.ModalUIOpen)
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
            knockdownAnimLock = false;
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

        // Fallback lock (in case animator states are not set up)
        knockdownTimer = Mathf.Max(0f, knockdownDuration);

        // Animator-driven lock: stays locked through PlayerFallDown -> PlayerStand, until stand ends.
        // We don't detach/disable anything; input is blocked by IsControlLocked.
        if (lockInputUntilStandEnds)
            knockdownAnimLock = true;

        // Trigger fall down animation if configured.
        if (driveAnimatorParams)
            RefreshAnimatorParamCache();

        if (animator && _animHasFallDownTrigger)
        {
            try
            {
                animator.ResetTrigger(_fallDownTriggerHash);
                animator.SetTrigger(_fallDownTriggerHash);
            }
            catch { /* ignore */ }
        }

        // Spill the player's inventory on knockback (drop everything as world items).
        // This is what makes knockdown feel punishing and also cancels hold poses.
        if (causeCargoSpill && inventory != null)
        {
            Transform o = dropOrigin ? dropOrigin : transform;
            inventory.SpillAllToWorld(o, dir, transform.root);

            // Keep references in sync if the carrier item was dropped.
            if (inventory.carrier == null)
                carrier = null;
        }

        if (carrier != null && causeCargoSpill && carrier.HasAnyMounted())
            carrier.SpillAllOnCarrierDrop(transform.position, dir);

    }

    /// <summary>
    /// Keeps input locked while animator is in FallDown or Stand (or transitioning between them).
    /// This prevents movement during knockdown and the stand-up animation.
    /// </summary>
    private void UpdateKnockdownAnimLock()
    {
        if (!lockInputUntilStandEnds)
        {
            knockdownAnimLock = false;
            return;
        }

        if (!animator || !animator.isActiveAndEnabled)
        {
            // No animator -> only fallback timer applies.
            knockdownAnimLock = false;
            return;
        }

        int layer = Mathf.Clamp(knockdownAnimLayer, 0, animator.layerCount - 1);

        // Check current + next state (to cover transitions).
        AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(layer);
        bool inTransition = animator.IsInTransition(layer);
        AnimatorStateInfo next = inTransition ? animator.GetNextAnimatorStateInfo(layer) : default;

        bool curFall = !string.IsNullOrEmpty(fallDownStateName) && cur.IsName(fallDownStateName);
        bool curStand = !string.IsNullOrEmpty(standStateName) && cur.IsName(standStateName);

        bool nextFall = inTransition && !string.IsNullOrEmpty(fallDownStateName) && next.IsName(fallDownStateName);
        bool nextStand = inTransition && !string.IsNullOrEmpty(standStateName) && next.IsName(standStateName);

        bool inKnockStates = curFall || curStand || nextFall || nextStand;

        // If we're currently locking due to knockdown, release only after we've fully left both states.
        if (knockdownAnimLock)
        {
            if (!inKnockStates)
                knockdownAnimLock = false;
        }
        else
        {
            // If not locking but we enter fall/stand, start locking.
            if (inKnockStates)
                knockdownAnimLock = true;
        }
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

    // ------------------------------------------------------------
    // Animator driving (optional)
    // ------------------------------------------------------------
    private void RefreshAnimatorParamCache(bool force = false)
    {
        if (!driveAnimatorParams) return;

        if (!animator)
        {
            _animHasSpeed = false;
            _animHasMovement = false;
            _animHasHoldPose = false;
            _animHasFallDownTrigger = false;
            _paramCacheKey = 0;
            return;
        }

        string sp = speedParam ?? string.Empty;
        string mp = movementParam ?? string.Empty;
        string hp = holdPoseParam ?? string.Empty;
        string fp = fallDownTriggerParam ?? string.Empty;

        int key = animator.GetInstanceID();
        unchecked
        {
            key = (key * 397) ^ sp.GetHashCode();
            key = (key * 397) ^ mp.GetHashCode();
            key = (key * 397) ^ hp.GetHashCode();
            key = (key * 397) ^ fp.GetHashCode();
        }

        if (!force && key == _paramCacheKey) return;
        _paramCacheKey = key;

        _lastSpeedParam = sp;
        _lastMovementParam = mp;
        _lastHoldPoseParam = hp;
        _lastFallDownTriggerParam = fp;

        _speedHash = !string.IsNullOrEmpty(sp) ? Animator.StringToHash(sp) : 0;
        _movementHash = !string.IsNullOrEmpty(mp) ? Animator.StringToHash(mp) : 0;
        _holdPoseHash = !string.IsNullOrEmpty(hp) ? Animator.StringToHash(hp) : 0;
        _fallDownTriggerHash = !string.IsNullOrEmpty(fp) ? Animator.StringToHash(fp) : 0;

        _animHasSpeed = HasParamOfType(animator, sp, AnimatorControllerParameterType.Float);
        _animHasMovement = HasParamOfType(animator, mp, AnimatorControllerParameterType.Float);
        _animHasHoldPose = HasParamOfType(animator, hp, AnimatorControllerParameterType.Int);
        _animHasFallDownTrigger = HasParamOfType(animator, fp, AnimatorControllerParameterType.Trigger);
    }

    private static bool HasParamOfType(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (!a || string.IsNullOrEmpty(name)) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type == type && ps[i].name == name)
                return true;
        }
        return false;
    }

    private void UpdateAnimatorParams(float desiredSpeed, float desiredMove01)
    {
        if (!driveAnimatorParams || !animator) return;
        RefreshAnimatorParamCache();

        float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(0.01f, animSmoothing));

        if (_animHasSpeed)
        {
            _animSpeed = Mathf.Lerp(_animSpeed, desiredSpeed, t);
            animator.SetFloat(_speedHash, _animSpeed);
        }

        if (_animHasMovement)
        {
            _animMovement = Mathf.Lerp(_animMovement, desiredMove01, t);
            animator.SetFloat(_movementHash, _animMovement);
        }

        // Optional: upper-body pose overlay (0=None, 1=OneHand, 2=TwoHand)
        int holdPose = ComputeHoldPose();

        if (IsControlLocked)
            holdPose = 0;

        if (_animHasHoldPose)
            animator.SetInteger(_holdPoseHash, holdPose);

        if (upperBodyLayerIndex >= 0 && upperBodyLayerIndex < animator.layerCount)
        {
            float w = (holdPose == 0) ? 0f : 1f;
            animator.SetLayerWeight(upperBodyLayerIndex, w);
        }
    }

    private int ComputeHoldPose()
    {
        if (!inventory) return 0;
        ItemDefinition def = inventory.ActiveDef();
        if (def == null) return 0;

        // Carriers are handled by carrier system; don't force arm pose here.
        if (def.isCarrier) return 0;

        // If carryKind doesn't exist, default is OneHand (safe fallback).
        return IsOneHandByDefinitionOrDefault(def) ? 1 : 2;
    }

    // Optional: animation event hook for stand-end unlock
    public void AnimEvent_StandFinished()
    {
        // Intentionally left for integration with knockdown lock logic
        isKnockedDown = false;
        knockdownTimer = 0f;
    }

    // ------------------------------------------------------------
    // ItemData reflection pickup fallback (no compile-time dependency)
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // carryKind reflection helpers (optional)
    // ------------------------------------------------------------
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

// ─────────────────────────────────────────────
// Use / Attack Action (LMB + Throw Anim)
// ─────────────────────────────────────────────

private void RefreshAttackAnimatorParamCache(bool force = false)
{
    if (!animator)
    {
        _animHasAttackReady = false;
        _animHasAttackTrigger = false;
        return;
    }

    int key = 17;
    key = key * 31 + (attackReadyBoolParam != null ? attackReadyBoolParam.GetHashCode() : 0);
    key = key * 31 + (attackTriggerParam != null ? attackTriggerParam.GetHashCode() : 0);

    if (!force && key == _attackParamCacheKey)
        return;

    _attackParamCacheKey = key;

    _animHasAttackReady = false;
    _animHasAttackTrigger = false;

    if (!string.IsNullOrEmpty(attackReadyBoolParam))
    {
        _attackReadyHash = Animator.StringToHash(attackReadyBoolParam);
    }

    if (!string.IsNullOrEmpty(attackTriggerParam))
    {
        _attackTriggerHash = Animator.StringToHash(attackTriggerParam);
    }

    // Verify parameter existence
    foreach (var p in animator.parameters)
    {
        if (!_animHasAttackReady &&
            p.type == AnimatorControllerParameterType.Bool &&
            p.name == attackReadyBoolParam)
            _animHasAttackReady = true;

        if (!_animHasAttackTrigger &&
            p.type == AnimatorControllerParameterType.Trigger &&
            p.name == attackTriggerParam)
            _animHasAttackTrigger = true;
    }
}

private void EnsureAnimEventRelay()
{
    if (!autoAttachAnimEventRelay) return;
    if (!animator) return;

    var go = animator.gameObject;
    var relay = go.GetComponent<PlayerAnimationEventRelay>();
    if (!relay) relay = go.AddComponent<PlayerAnimationEventRelay>();
    relay.controller = this;
}

private bool CanDriveAttackAnim()
{
    if (!enableUseActions) return false;
    if (!animator) return false;

    RefreshAttackAnimatorParamCache();

    return _animHasAttackReady && _animHasAttackTrigger;
}

private void SetAttackReady(bool ready)
{
    if (!CanDriveAttackAnim()) return;
    animator.SetBool(_attackReadyHash, ready);
}

private void FireAttackTrigger()
{
    if (!CanDriveAttackAnim()) return;
    animator.SetTrigger(_attackTriggerHash);
}

private IPlayerUsable GetHeldUsable()
{
    if (inventory == null) return null;
    var held = inventory.HeldInstance;
    if (!held) return null;

    // Unity GetComponent<T>() does not support interfaces. Search MonoBehaviours.
    var behaviours = held.GetComponentsInChildren<MonoBehaviour>(true);
    for (int i = 0; i < behaviours.Length; i++)
    {
        if (behaviours[i] is IPlayerUsable usable)
            return usable;
    }
    return null;
}

private void TryBeginPrimaryUsePrepare()
{
    if (_useAction != UseActionType.None) return;
    if (IsControlLocked) return;
    if (inventory == null) return;

    if (IsCarryLockedByDefinition(inventory.ActiveDef()))
        return;

    var usable = GetHeldUsable();
    if (usable == null) return; // not a usable item

    _useUsable = usable;
    _useWantsAnim = usable.UsesAttackAnimation && CanDriveAttackAnim();

    _useAction = UseActionType.Primary;
    _usePreparing = true;
    _useReleaseQueued = false;
    _useHeldTime = 0f;
    _usePrepareStartTime = Time.time;
    _useExecuted = false;
    _useSawAttackState = false;

    _useAimForward = GetAimForward();

    if (_useWantsAnim)
    {
        // Enter AttackReady state
        SetAttackReady(true);
        animator.ResetTrigger(_attackTriggerHash);
    }

    var ctx = new PlayerUseActionContext
    {
        input = PlayerUseInput.Primary,
        heldTime = 0f,
        charge01 = Mathf.Clamp01((0f) / Mathf.Max(0.0001f, chargeMaxHoldTime)),
        aimForward = _useAimForward,
        usesAnimation = _useWantsAnim
    };
    usable.OnPrepare(this, ctx);
}

private void OnPrimaryUseReleased()
{
    if (_useAction != UseActionType.Primary) return;

    _useAimForward = GetAimForward();

    // If still preparing, queue release. If prepare time already passed, start attack immediately.
    if (_usePreparing)
    {
        float preparedFor = Time.time - _usePrepareStartTime;
        if (preparedFor >= actionPrepareTime)
        {
            StartUseAttack(PlayerUseInput.Primary);
        }
        else
        {
            _useReleaseQueued = true;
        }
    }
}

private void BeginThrowPrepare(ItemDefinition def, int index)
{
    if (_useAction != UseActionType.None) return;
    if (IsControlLocked) return;
    if (inventory == null) return;
    if (def == null) return;

    // Lock to this item
    inventory.SetActiveIndex(index);
    _throwIndex = index;
    _throwDef = def;

    _useUsable = null;
    _useWantsAnim = CanDriveAttackAnim(); // throw always tries to use the action anim if configured

    _useAction = UseActionType.Throw;
    _usePreparing = true;
    _useReleaseQueued = false;
    _useHeldTime = 0f;
    _usePrepareStartTime = Time.time;
    _useExecuted = false;
    _useSawAttackState = false;

    _useAimForward = GetAimForward();

    if (_useWantsAnim)
    {
        SetAttackReady(true);
        animator.ResetTrigger(_attackTriggerHash);
    }
}

private void OnThrowReleased()
{
    if (_useAction != UseActionType.Throw) return;

    // Compute final throw params based on total hold duration
    float denom = Mathf.Max(0.01f, throwChargeTime);
    float charge01 = Mathf.Clamp01((itemHoldTimer - throwHoldTime) / denom);

    float force = Mathf.Lerp(throwMinForce, throwMaxForce, charge01);
    float upBias = Mathf.Lerp(throwMaxUpBias, throwMinUpBias, charge01);
    float spin = Mathf.Lerp(throwMinSpin, throwMaxSpin, charge01);

    _useAimForward = GetAimForward();
    _throwForce = force;
    _throwSpin = spin;
    _throwDir = (_useAimForward + Vector3.up * upBias).normalized;

    if (_usePreparing)
    {
        float preparedFor = Time.time - _usePrepareStartTime;
        if (preparedFor >= actionPrepareTime)
        {
            StartUseAttack(PlayerUseInput.Throw);
        }
        else
        {
            _useReleaseQueued = true;
        }
    }
}

private void StartUseAttack(PlayerUseInput input)
{
    if (_useAction == UseActionType.None) return;
    if (!_usePreparing) return;

    _usePreparing = false;
    _useAttackStartTime = Time.time;
    _useExecuteFallbackAt = Time.time + Mathf.Max(0f, actionExecuteFallbackDelay);
    _useHardEndAt = Time.time + Mathf.Max(0.05f, actionTotalTime);

    if (_useWantsAnim)
    {
        // Leave AttackReady -> enter Attack via trigger
        SetAttackReady(false);
        FireAttackTrigger();
    }
    else
    {
        // No animation: execute at fallback time
    }
}

private void CancelUseAction()
{
    if (_useAction == UseActionType.None) return;

    // Clear animator params
    if (_useWantsAnim)
    {
        SetAttackReady(false);
        if (animator && _animHasAttackTrigger)
            animator.ResetTrigger(_attackTriggerHash);
    }

    // Notify usable
    if (_useAction == UseActionType.Primary && _useUsable != null)
    {
        var ctx = new PlayerUseActionContext
        {
            input = PlayerUseInput.Primary,
            heldTime = _useHeldTime,
            charge01 = Mathf.Clamp01((_useHeldTime) / Mathf.Max(0.0001f, chargeMaxHoldTime)),
            aimForward = _useAimForward,
            usesAnimation = _useWantsAnim
        };
        _useUsable.OnCancel(this, ctx);
    }

    _useAction = UseActionType.None;
    _usePreparing = false;
    _useReleaseQueued = false;
    _useHeldTime = 0f;
    _useExecuted = false;
    _useUsable = null;
    _throwDef = null;
}

private void EndUseAction()
{
    if (_useWantsAnim)
    {
        SetAttackReady(false);
        if (animator && _animHasAttackTrigger)
            animator.ResetTrigger(_attackTriggerHash);
    }

    _useAction = UseActionType.None;
    _usePreparing = false;
    _useReleaseQueued = false;
    _useHeldTime = 0f;
    _useExecuted = false;
    _useUsable = null;
    _throwDef = null;
}

private void TickUseAction()
{
    if (_useAction == UseActionType.None)
        return;

    if (IsControlLocked)
    {
        CancelUseAction();
        return;
    }

    if (_usePreparing)
    {
        _useHeldTime += Time.deltaTime;

        // If released early, fire as soon as prepare time ends
        if (_useReleaseQueued && (Time.time - _usePrepareStartTime) >= actionPrepareTime)
        {
            StartUseAttack(_useAction == UseActionType.Primary ? PlayerUseInput.Primary : PlayerUseInput.Throw);
        }

        return;
    }

    // Attacking stage
    if (!_useExecuted && Time.time >= _useExecuteFallbackAt)
    {
        ExecuteUseNow();
    }

    bool endByTime = Time.time >= _useHardEndAt;
    bool endByAnim = false;

    if (unlockWhenAttackAnimEnds && _useWantsAnim && animator && !string.IsNullOrEmpty(actionAttackStateName))
    {
        int layer = Mathf.Clamp(actionAnimLayerIndex, 0, animator.layerCount - 1);
        bool inTrans = animator.IsInTransition(layer);
        var st = animator.GetCurrentAnimatorStateInfo(layer);

        bool inAttack = st.IsName(actionAttackStateName);
        if (inAttack)
        {
            _useSawAttackState = true;
            if (!inTrans && st.normalizedTime >= 1f)
                endByAnim = true;
        }
        else
        {
            if (_useSawAttackState && !inTrans)
                endByAnim = true;
        }
    }

    if (endByAnim || endByTime)
    {
        EndUseAction();
    }
}

private Vector3 GetAimForward()
{
    Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
    Vector3 fwd = cam ? cam.transform.forward : transform.forward;
    if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
    return fwd.normalized;
}

/// <summary>
/// Called by Animation Event (AttackExecute) via PlayerAnimationEventRelay.
/// Put the event on the ATTACK clip at the exact hit/release frame.
/// </summary>
public void AnimEvent_AttackExecute()
{
    if (_useAction == UseActionType.None) return;
    if (_usePreparing) return;
    if (_useExecuted) return;

    ExecuteUseNow();
}

private void ExecuteUseNow()
{
    if (_useExecuted) return;
    _useExecuted = true;

    if (_useAction == UseActionType.Primary)
    {
        if (_useUsable != null)
        {
            var ctx = new PlayerUseActionContext
            {
                input = PlayerUseInput.Primary,
                heldTime = _useHeldTime,
                charge01 = Mathf.Clamp01((_useHeldTime) / Mathf.Max(0.0001f, chargeMaxHoldTime)),
                aimForward = _useAimForward,
                usesAnimation = _useWantsAnim
            };
            _useUsable.OnExecute(this, ctx);
        }
        return;
    }

    if (_useAction == UseActionType.Throw)
    {
        if (inventory == null) return;

        inventory.SetActiveIndex(_throwIndex);

        Transform origin = dropOrigin ? dropOrigin : transform;
        Vector3 fwd = _useAimForward;

        WorldItem spawned;
        bool dropped = inventory.DropActiveItem(origin, fwd, out spawned);

        if (!dropped) return;

        if (spawned != null)
        {
            ApplyThrowToWorldItem(spawned, _throwDir, _throwForce, _throwSpin);
        }
        else
        {
            // Fallback: search nearby (old method)
            Vector3 expectedDropPos = origin
                ? origin.position + fwd * 0.6f + Vector3.up * 0.5f
                : transform.position + fwd * 0.6f + Vector3.up * 0.5f;

            TryApplyThrowToFreshDrop(_throwDef, expectedDropPos, _throwDir, _throwForce, _throwSpin);
        }
    }
}

private void ApplyThrowToWorldItem(WorldItem wi, Vector3 dir, float force, float spin)
{
    if (!wi) return;

    if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
    if (!wi.rb) wi.rb = wi.gameObject.AddComponent<Rigidbody>();

    wi.rb.isKinematic = false;
    wi.rb.useGravity = true;

    Vector3 playerVel = controller != null ? controller.velocity : Vector3.zero;
    Vector3 initialVel = dir.normalized * Mathf.Max(0f, force) + playerVel * 0.15f;

    wi.ArmIgnoreBreakForThrower(transform.root, 0.25f);
    wi.rb.linearVelocity = initialVel;

    if (spin > 0f)
        wi.rb.angularVelocity = UnityEngine.Random.onUnitSphere * spin;
}

}


/// <summary>
/// Animation Event receiver.
/// Attach (or auto-attached) to the same GameObject that has the Animator.
/// In the ATTACK animation clip, add an Animation Event calling "AttackExecute".
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationEventRelay : MonoBehaviour
{
    public PlayerController controller;

    // Animation Event function name
    public void AttackExecute()
    {
        if (controller) controller.AnimEvent_AttackExecute();
    }
}
