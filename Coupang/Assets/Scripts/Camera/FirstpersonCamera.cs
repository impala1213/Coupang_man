using UnityEngine;
using UnityEngine.InputSystem;

public class FirstpersonCamera : MonoBehaviour
{
    [Header("Settings")]
    public float mouseSensitivity = 100f;
    public float verticalRotationLimit = 80f;

    [Header("Idle Head-Bob Filter (Keep Camera Attached)")]
    [Tooltip("Head bone (or camera socket) transform. If null, uses the current parent.")]
    public Transform headSource;

    [Tooltip("Preserve the camera's initial localPosition under the head bone (recommended).")]
    public bool preserveInitialLocalPose = true;

    [Tooltip("Optional: if preserveInitialLocalPose is false, the camera will be placed at this local offset under headSource at Start.")]
    public Vector3 headLocalOffset = new Vector3(0f, 0.05f, 0.15f);

    [Tooltip("Enable filtering ONLY while idle/grounded (removes small idle bob).")]
    public bool filterIdleOnly = true;

    [Tooltip("Consider the player idle if grounded and speed is below this threshold.")]
    public float idleSpeedThreshold = 0.12f;

    [Tooltip("Idle smoothing time (bigger = steadier camera).")]
    public float idleSmoothTime = 0.85f;

    [Tooltip("Deadzone for vertical bob while idle. Small changes within this range are ignored.")]
    public float idleYDeadzone = 0.02f;

    [Tooltip("Maximum vertical bob cancellation in meters (prevents over-cancelling if detection misfires).")]
    public float maxIdleCancel = 0.06f;

    [Tooltip("How strong the cancellation is (1 = cancel as much as allowed).")]
    [Range(0f, 1f)]
    public float cancelStrength = 1f;

    // Backward-compat: kept so existing inspector data doesn't break, but no longer used.
    [Header("Legacy")]
    [Tooltip("(Deprecated) Detaching is no longer used. Keep the camera parented to the head bone.")]
    public bool detachFromHeadOnAwake = false;

    private Transform playerRoot;
    private PlayerController playerController;
    private CharacterController charController;

    private Vector3 baseLocalPos;
    private bool baseLocalPosCaptured;

    // head-y low-pass (in playerRoot local space)
    private float stableHeadY;
    private float stableHeadYVel;
    private bool hasStableHeadY;

    private float xRotation = 0f;
    private PlayerControls playerControls;
    private Vector2 lookInput;

    private void Awake()
    {
        playerControls = new PlayerControls();
        playerControls.Player.Enable();
        playerControls.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        playerControls.Player.Look.canceled += ctx => lookInput = Vector2.zero;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        playerRoot = transform.root;
        if (playerRoot != null) playerController = playerRoot.GetComponent<PlayerController>();
        if (playerRoot != null) charController = playerRoot.GetComponent<CharacterController>();

        if (playerRoot != null && playerRoot.GetComponent<PlayerController>() == null)
            Debug.LogError("Player Root does not have PlayerController. Please check the hierarchy structure.");

        // Auto-detect head source from current parent (common setup: camera is a child of head bone).
        if (headSource == null)
        {
            Transform p = transform.parent;
            if (p != null && p != playerRoot)
                headSource = p;
        }

        // Important: DO NOT detach. The user wants the original head motion for big events.
        // detachFromHeadOnAwake is kept only for inspector compatibility.
    }

    private void Start()
    {
        // Capture the actual authored camera pose after the scene has initialized.
        if (headSource == null) return;

        if (!preserveInitialLocalPose)
        {
            // Place at explicit socket offset if requested.
            transform.localPosition = headLocalOffset;
        }

        baseLocalPos = transform.localPosition;
        baseLocalPosCaptured = true;

        // Initialize head-y baseline to prevent a start-frame jump.
        if (playerRoot != null)
        {
            stableHeadY = playerRoot.InverseTransformPoint(headSource.position).y;
            hasStableHeadY = true;
        }
    }

    private void Update()
    {
        HandleCameraLook();
    }

    private void LateUpdate()
    {
        ApplyIdleHeadBobCancellation();
    }

    private void ApplyIdleHeadBobCancellation()
    {
        if (!baseLocalPosCaptured) return;
        if (playerRoot == null) return;
        if (headSource == null) return;

        // This implementation assumes the camera stays parented under headSource.
        // If not, we don't apply corrections to avoid unexpected jumps.
        if (transform.parent != headSource)
            return;

        bool shouldFilter = true;
        if (filterIdleOnly)
        {
            bool grounded = charController != null && charController.isGrounded;
            float speed = (charController != null)
                ? new Vector3(charController.velocity.x, 0f, charController.velocity.z).magnitude
                : 0f;
            bool controlLocked = playerController != null && playerController.IsControlLocked;

            shouldFilter = grounded && speed <= idleSpeedThreshold && !controlLocked;
        }

        // Compute head height in player root space.
        float headY = playerRoot.InverseTransformPoint(headSource.position).y;

        if (!shouldFilter)
        {
            // No cancellation outside idle: keep the authored pose (no offset), and reset baseline.
            transform.localPosition = baseLocalPos;
            stableHeadY = headY;
            stableHeadYVel = 0f;
            hasStableHeadY = true;
            return;
        }

        if (!hasStableHeadY)
        {
            stableHeadY = headY;
            hasStableHeadY = true;
        }

        // Deadzone: ignore very small micro-bob movement.
        float targetY = headY;
        if (Mathf.Abs(headY - stableHeadY) <= idleYDeadzone)
            targetY = stableHeadY;

        // Low-pass the head height to estimate the "slow" component.
        stableHeadY = Mathf.SmoothDamp(stableHeadY, targetY, ref stableHeadYVel, Mathf.Max(0.0001f, idleSmoothTime));

        // High-frequency component = bob we want to cancel.
        float bobDelta = headY - stableHeadY;
        float cancelY = Mathf.Clamp(bobDelta, -maxIdleCancel, maxIdleCancel) * cancelStrength;

        // Apply the opposite offset along playerRoot up axis, converted into head local space.
        Vector3 correctionWorld = playerRoot.TransformVector(new Vector3(0f, -cancelY, 0f));
        Vector3 correctionLocal = headSource.InverseTransformVector(correctionWorld);

        transform.localPosition = baseLocalPos + correctionLocal;
    }

    private void HandleCameraLook()
    {
        if (playerController != null && playerController.IsControlLocked)
        {
            lookInput = Vector2.zero;
            return;
        }

        if (lookInput == Vector2.zero) return;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // Vertical: local to head bone.
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -verticalRotationLimit, verticalRotationLimit);
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Horizontal: rotate the player root.
        playerRoot.Rotate(Vector3.up * mouseX);
    }
}
