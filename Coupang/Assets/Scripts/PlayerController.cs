using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;
    private Animator anim; // Step 7: Animator component
    private PlayerControls playerControls;
    private Vector2 moveInput;
    private Transform cameraMain;

    public float moveSpeed = 5f;

    // --- Ground Detection Variables (Step 4) ---
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    private bool isGrounded;
    private Vector3 groundNormal;

    // --- Balance System Variables (Step 5) ---
    public float balanceRecoverySpeed = 2f;
    public float autoBalanceSpeed = 0.5f;
    public float instabilityFactor = 5f;
    public float torqueFactor = 50f;

    [HideInInspector]
    public float balanceFactor = 0f;
    private float balanceInput;

    // --- Cargo System Variables (Step 6) ---
    public Transform cargoStackParent;
    public float baseMass = 80f;
    public float singleCargoMass = 5f;

    // Player's base CoM in local space
    private readonly Vector3 baseCoM = new Vector3(0f, 1f, 0f);

    // Calculated CoM (used for rb.centerOfMass update and balance instability)
    public Vector3 cargoCenterOfMass = Vector3.zero;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>(); // Step 7: Get the Animator component
        cameraMain = Camera.main.transform;

        playerControls = new PlayerControls();
        playerControls.Player.Enable();

        // Connect Move and Balance inputs
        playerControls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        playerControls.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        playerControls.Player.BalanceLeft.performed += ctx => balanceInput = -1f;
        playerControls.Player.BalanceLeft.canceled += ctx => balanceInput = 0f;
        playerControls.Player.BalanceRight.performed += ctx => balanceInput = 1f;
        playerControls.Player.BalanceRight.canceled += ctx => balanceInput = 0f;
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
        HandleAnimation(); // Step 7: Update Animator parameters
    }

    private void FixedUpdate()
    {
        UpdateCargoSystem(); // Step 6: Recalculate CoM and Mass
        HandleMovement();

        if (isGrounded)
        {
            HandleBalance(); // Step 5: Apply balance forces
        }
    }

    // --- Step 3: Movement Logic ---
    private void HandleMovement()
    {
        Vector3 camForward = Vector3.Scale(cameraMain.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 camRight = Vector3.Scale(cameraMain.right, new Vector3(1, 0, 1)).normalized;

        Vector3 moveDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;

        Vector3 targetVelocity = moveDirection * moveSpeed;

        Vector3 currentHorizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        Vector3 velocityChange = (targetVelocity - currentHorizontalVelocity);

        velocityChange.x = Mathf.Clamp(velocityChange.x, -10f, 10f);
        velocityChange.z = Mathf.Clamp(velocityChange.z, -10f, 10f);
        velocityChange.y = 0;

        rb.AddForce(velocityChange, ForceMode.VelocityChange);

        if (moveDirection != Vector3.zero)
        {
            Quaternion toRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            rb.rotation = Quaternion.RotateTowards(rb.rotation, toRotation, 720f * Time.fixedDeltaTime);
        }
    }

    // --- Step 4: Ground Check Logic ---
    private void CheckGround()
    {
        RaycastHit hit;
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;

        if (Physics.Raycast(rayStart, Vector3.down, out hit, groundCheckDistance, groundLayer))
        {
            isGrounded = true;
            groundNormal = hit.normal;
        }
        else
        {
            isGrounded = false;
            groundNormal = Vector3.up;
        }

        Debug.DrawRay(rayStart, Vector3.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
        if (isGrounded)
        {
            Debug.DrawRay(hit.point, groundNormal, Color.blue);
        }
    }

    // --- Step 6: Dynamic Center of Mass Calculation ---
    private void UpdateCargoSystem()
    {
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

    // --- Step 5: Balance and Torque Application Logic ---
    private void HandleBalance()
    {
        // 1. Instability from terrain slope
        float slopeSideDot = Vector3.Dot(transform.right, groundNormal);
        float slopeInstability = slopeSideDot * instabilityFactor * Time.fixedDeltaTime;

        // 2. Instability from horizontal CoM offset
        float cargoInstability = (cargoCenterOfMass.x * 0.5f) * Time.fixedDeltaTime;

        // 3. Update the balance factor
        balanceFactor += slopeInstability + cargoInstability;

        // 4. Apply manual input for recovery
        if (balanceInput != 0)
        {
            balanceFactor = Mathf.MoveTowards(balanceFactor, -balanceInput, balanceRecoverySpeed * Time.fixedDeltaTime);
        }
        else
        {
            // Auto-recovery to center (0)
            balanceFactor = Mathf.MoveTowards(balanceFactor, 0f, autoBalanceSpeed * Time.fixedDeltaTime);
        }

        // 5. Clamp the balance factor
        balanceFactor = Mathf.Clamp(balanceFactor, -1f, 1f);

        // 6. Apply Torque to Rigidbody
        Vector3 balanceTorque = transform.forward * -balanceFactor * torqueFactor;
        rb.AddTorque(balanceTorque, ForceMode.Acceleration);

        // 7. Toppling check (e.g., transition to ragdoll)
        if (Mathf.Abs(balanceFactor) > 0.9f)
        {
            // Implement your fall/ragdoll logic here
        }
    }

    // --- Step 7: Animation Logic ---
    private void HandleAnimation()
    {
        if (anim == null) return;

        // 1. Movement Speed
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        anim.SetFloat("Speed", currentSpeed);

        // 2. Grounded State
        anim.SetBool("IsGrounded", isGrounded);

        // 3. Balance Leaning (Used by the BalanceIK.cs script for bone rotation)
        anim.SetFloat("BalanceFactor", balanceFactor);
    }
}