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
    public float carrierDropBaseHold = 0.5f;   // 최소 홀드 시간
    public float carrierDropPerWeight = 0.05f; // 짐 무게(kg)당 추가 시간
    public float carrierDropMaxHold = 3f;      // 상한

    [Header("Carrier Inspect")]
    public float carrierInspectHoldTime = 0.6f;
    public float carrierInspectMaxDistance = 4f;
    public CarrierSlotUI carrierSlotUI;

    [Header("Debug")]
    public bool debugLever;

    private CharacterController controller;
    private float verticalVel;

    private Vector3 knockbackVelocity;
    private bool isKnockedDown;
    private float knockdownTimer;

    private UniversalLever currentLever;
    private bool leverUseHeld;

    // 지게 드롭 홀드 상태
    private bool carrierDropHolding;
    private float carrierDropTimer;

    // 지게 슬롯 인스펙트 상태
    private bool carrierInspecting;
    private float carrierInspectTimer;
    private CarrierController carrierInspectTarget;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (!carrierSlotUI)
            carrierSlotUI = FindFirstObjectByType<CarrierSlotUI>();
    }

    void Update()
    {
        Move();
        UpdateLeverFocusAndTick();
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
        if (!inventory) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        // ── E키: 레버 우선, 아니면 아이템 줍기 ──
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (currentLever != null)
            {
                leverUseHeld = true;
                currentLever.OnUsePressed();
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
            if (currentLever != null)
            {
                leverUseHeld = false;
                currentLever.OnUseReleased();
            }
        }

        // ── G키: 일반 아이템 드롭 or 지게 드롭 홀드 시작 ──
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (!inventory) return;

            ItemDefinition activeDef = inventory.ActiveDef();
            bool activeIsCarrier = (activeDef != null && activeDef.isCarrier && carrier != null);

            if (activeIsCarrier)
            {
                // 지게 드롭은 홀드로
                carrierDropHolding = true;
                carrierDropTimer = 0f;
            }
            else
            {
                // 일반 아이템은 탭으로 바로 드롭
                Vector3 fwd = transform.forward;
                Transform origin = dropOrigin ? dropOrigin : transform;
                inventory.DropActiveItem(origin, fwd);
            }
        }

        // LMB: use active item (hook용)
        if (Input.GetMouseButtonDown(0))
        {
            // active item use hook
        }
    }

    void HandleCarrierDropHold()
    {
        if (!carrierDropHolding)
            return;

        if (!Input.GetKey(KeyCode.G))
        {
            // 홀드 중 키를 떼면 취소
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
            // 실제 지게 통째 드롭
            Transform origin = dropOrigin ? dropOrigin : transform;
            Vector3 pos = origin.position + transform.forward * 0.6f + Vector3.up * 0.3f;
            Vector3 fwd = transform.forward;

            bool removed = inventory.DropCarrierAsBundle(origin, fwd);
            if (removed)
            {
                carrier.DropAsBundle(pos, fwd);

                // 인벤토리/플레이어에서 지게 참조 제거
                inventory.carrier = null;
                carrier = null;
            }

            carrierDropHolding = false;
            carrierDropTimer = 0f;
        }
    }

    void HandleCarrierInspect()
    {
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

        // 레버 포커스 중이면 지게 인스펙트 X
        if (currentLever != null)
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

        // 바로 앞에 월드 아이템이 있다면, 인스펙트보다 줍기가 우선
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
            if (currentLever != null)
            {
                currentLever.FocusExit();
            }

            currentLever = hitLever;

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
