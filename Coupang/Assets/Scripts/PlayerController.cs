using UnityEngine;


[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpForce = 5f;
    public float gravity = -19.62f; // -9.81 * 2



    [Header("Interaction")]
    public float interactDistance = 3f;
    public LayerMask interactMask;


    [Header("Refs")]
    public CameraSwitcher cameraSwitcher;
    public InventorySystem inventory;
    public CarrierController carrier;
    public Transform dropOrigin; // 플레이어 앞 위치


    private CharacterController controller;
    private float yaw, pitch;
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
    }

    void Move()
    {
        bool grounded = controller.isGrounded;
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 move = (transform.right * h + transform.forward * v).normalized;
        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);


        if (grounded && verticalVel < 0) verticalVel = -2f;
        if (Input.GetKeyDown(KeyCode.Space) && grounded) verticalVel = jumpForce;
        verticalVel += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVel * Time.deltaTime);
    }


    void HandleHotbar()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveSlot(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveSlot(4);
    }

    void HandleActions()
    {
        // E: 줍기/장착/적재
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteractOrPickup();
        }


        // G: 버리기 (활성 슬롯) / 지게 적재물 상단 하차
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (!inventory.DropActiveItem(dropOrigin))
            {
                if (carrier != null && carrier.IsEquipped)
                    carrier.UnloadTop(dropOrigin.position + transform.forward * 1.0f + Vector3.up * 0.5f, transform.forward);
            }
        }
        if (carrier != null && carrier.IsEquipped)
        {
            carrier.ApplyBalanceInput(Input.GetMouseButton(0) ? -1f : 0f, Input.GetMouseButton(1) ? 1f : 0f);
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) inventory.UseActiveItem(this);
        }
    }
    void TryInteractOrPickup()
    {
        Camera cam = cameraSwitcher.GetActiveCamera();
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            var worldItem = hit.collider.GetComponentInParent<WorldItem>();
            if (worldItem != null)
            {
                if (worldItem.definition != null && worldItem.definition.isCarrier)
                {
                    // 지게 아이템: 사용(토글 장착)
                    if (inventory.TryPickupWorldItem(worldItem))
                    {
                        // 즉시 사용 = 장착 토글
                        inventory.UseByDefinition(worldItem.definition, this);
                    }
                    return;
                }


                // 지게 장착 중이면 적재, 아니면 인벤토리 담기
                if (carrier != null && carrier.IsEquipped && worldItem.definition.itemType == ItemType.Cargo)
                {
                    if (carrier.TryMount(worldItem)) return;
                }
                else
                {
                    inventory.TryPickupWorldItem(worldItem);
                }
            }
        }
    }
}