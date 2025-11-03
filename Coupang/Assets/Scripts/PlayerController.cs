using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    // --- 1. Components and Core Variables ---
    private Rigidbody rb;
    private Animator anim;
    private PlayerControls playerControls;
    private Vector2 moveInput;

    public float moveSpeed = 5f;

    // --- Camera Manager Reference ---
    [Header("Camera Manager")]
    // Assign the Main Camera object which has the CameraViewManager script
    public CameraViewManager cameraManager;
    private bool lastCarrierState = false; // To track state change

    // --- 2. Ragdoll Variables ---
    public float toppleThreshold = 0.95f;
    public float recoveryTime = 3f;
    public float getUpAnimDuration = 2.5f;

    private Rigidbody[] ragdollRBs;
    private Collider[] ragdollColliders;
    private bool isToppling = false;

    // --- 3. Ground Detection Variables ---
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    private bool isGrounded;
    private Vector3 groundNormal;

    // --- 4. Balance System Variables ---
    public float balanceRecoverySpeed = 2f;
    public float autoBalanceSpeed = 0.5f;
    public float instabilityFactor = 5f;
    public float torqueFactor = 50f;

    [HideInInspector]
    public float balanceFactor = 0f;
    private float balanceInput;

    // --- 5. Cargo/Center of Mass Variables ---
    [Header("Carrier System")]
    public bool isCarrierEquipped = false;

    public Transform cargoStackParent;
    public float baseMass = 80f;
    public float singleCargoMass = 5f;

    private readonly Vector3 baseCoM = new Vector3(0f, 1f, 0f);
    public Vector3 cargoCenterOfMass = Vector3.zero;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponentInChildren<Animator>();

        playerControls = new PlayerControls();
        playerControls.Player.Enable();

        playerControls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        playerControls.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        playerControls.Player.BalanceLeft.performed += ctx => balanceInput = -1f;
        playerControls.Player.BalanceLeft.canceled += ctx => balanceInput = 0f;
        playerControls.Player.BalanceRight.performed += ctx => balanceInput = 1f;
        playerControls.Player.BalanceRight.canceled += ctx => balanceInput = 0f;
    }

    private void Start()
    {
        SetupRagdollComponents();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Initial view setup
        lastCarrierState = isCarrierEquipped;
        if (cameraManager != null)
            cameraManager.SetView(isCarrierEquipped ? CameraViewManager.ViewMode.ThirdPerson : CameraViewManager.ViewMode.FirstPerson);
    }

    private void OnEnable()
    {
        playerControls.Player.Enable();
    }

    private void OnDisable()
    {
        playerControls.Player.Disable();
    }

    private void Update()
    {
        CheckGround();
        HandleAnimation();
    }

    private void FixedUpdate()
    {
        UpdateCargoSystem();

        if (!isToppling)
        {
            HandleMovement();

            // ?? NEW LOGIC: Only handle balance if grounded AND carrier is equipped
            if (isGrounded && isCarrierEquipped)
            {
                HandleBalance();
            }
        }
        else
        {
            rb.linearVelocity *= 0.9f;
        }

        // ?? NEW LOGIC: Check for state change to toggle camera view
        if (isCarrierEquipped != lastCarrierState)
        {
            if (cameraManager != null)
            {
                cameraManager.SetView(isCarrierEquipped ? CameraViewManager.ViewMode.ThirdPerson : CameraViewManager.ViewMode.FirstPerson);
            }
            lastCarrierState = isCarrierEquipped;
        }
    }

    // --- Ragdoll, Ground Check, Update Cargo System, Start/End Recovery (Same as previous, omitted for brevity) ---
    private void SetupRagdollComponents() {/* ... */}
    private void SetRagdollEnabled(bool isEnabled) {/* ... */}

    private void HandleMovement()
    {
        // Movement is based on the Player Root's forward/right direction (controlled by mouse rotation)
        Vector3 moveDirection = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;
        Vector3 targetVelocity = moveDirection * moveSpeed;

        Vector3 currentHorizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        Vector3 velocityChange = (targetVelocity - currentHorizontalVelocity);

        velocityChange.x = Mathf.Clamp(velocityChange.x, -10f, 10f);
        velocityChange.z = Mathf.Clamp(velocityChange.z, -10f, 10f);
        velocityChange.y = 0;

        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    private void CheckGround() {/* ... */}
    private void UpdateCargoSystem()
    {
        // ?? Balance system variables are reset when the carrier is not equipped
        if (!isCarrierEquipped)
        {
            rb.mass = baseMass;
            rb.centerOfMass = baseCoM;
            cargoCenterOfMass = baseCoM;
            return;
        }

        // ... (Dynamic CoM calculation logic continues here) ...
        Vector3 weightedPositionSum = Vector3.zero;
        float totalMass = baseMass;

        weightedPositionSum += baseCoM * baseMass;

        if (cargoStackParent != null)
        {
            int cargoCount = cargoStackParent.childCount;
            for (int i = 0; i < cargoCount; i++)
            {
                Transform cargo = cargoStackParent.GetChild(i);
                totalMass += singleCargoMass;

                Vector3 cargoLocalPos = transform.InverseTransformPoint(cargo.position);
                weightedPositionSum += cargoLocalPos * singleCargoMass;
            }
        }

        if (totalMass > 0)
        {
            cargoCenterOfMass = weightedPositionSum / totalMass;
        }
        else
        {
            cargoCenterOfMass = baseCoM;
        }

        rb.mass = totalMass;
        rb.centerOfMass = cargoCenterOfMass;
    }

    // --- Balance and Torque Application Logic (Only runs when isCarrierEquipped is true) ---
    private void HandleBalance()
    {
        // 1. Calculate Instability (Torque Source) - Removed Time.fixedDeltaTime from individual factors
        float slopeSideDot = Vector3.Dot(transform.right, groundNormal);
        float slopeInstability = slopeSideDot * instabilityFactor;

        float cargoInstability = cargoCenterOfMass.x * 0.5f;

        // Update total balance factor
        balanceFactor += (slopeInstability + cargoInstability) * Time.fixedDeltaTime;

        // 2. Apply Recovery/Input
        if (balanceInput != 0)
            balanceFactor = Mathf.MoveTowards(balanceFactor, -balanceInput, balanceRecoverySpeed * Time.fixedDeltaTime);
        else
            balanceFactor = Mathf.MoveTowards(balanceFactor, 0f, autoBalanceSpeed * Time.fixedDeltaTime);

        balanceFactor = Mathf.Clamp(balanceFactor, -1f, 1f);

        // 3. Apply Corrective Torque
        Vector3 balanceTorque = transform.forward * -balanceFactor * torqueFactor;
        rb.AddTorque(balanceTorque, ForceMode.Acceleration);

        if (Mathf.Abs(balanceFactor) > toppleThreshold && !isToppling)
        {
            StartToppling(balanceFactor > 0);
        }
    }

    private void StartToppling(bool leanRight) {/* ... */}
    private void StartRecovery() {/* ... */}
    private void EndRecovery()
    {
        isToppling = false;
        playerControls.Player.Enable();

        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        balanceFactor = 0f;
    }
    private void HandleAnimation() {/* ... */}
}