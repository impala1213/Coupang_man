using UnityEngine;
using UnityEngine.InputSystem;

public class CameraViewManager : MonoBehaviour
{
    public enum ViewMode { FirstPerson, ThirdPerson }

    [Header("References")]
    public Transform playerRoot; // The Player Root object (where PlayerController is attached)
    public Transform firstPersonParent; // The Head Bone Transform (where the camera is initially attached)

    [Header("Settings")]
    public float mouseSensitivity = 100f;
    public float verticalRotationLimit = 80f;
    public Vector3 thirdPersonOffset = new Vector3(0, 2f, -3f);

    private ViewMode currentViewMode = ViewMode.FirstPerson;
    private float xRotation = 0f;
    private PlayerControls playerControls;
    private Vector2 lookInput;

    // Initial 1P position relative to the Head Bone
    private readonly Vector3 firstPersonLocalPosition = new Vector3(0f, 0.05f, 0.15f);

    private void Awake()
    {
        playerControls = new PlayerControls();
        playerControls.Player.Enable();
        playerControls.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        playerControls.Player.Look.canceled += ctx => lookInput = Vector2.zero;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerRoot == null)
            playerRoot = transform.root;
    }

    private void LateUpdate()
    {
        HandleCameraLook();
    }

    // --- Public View Switching Function ---
    public void SetView(ViewMode newMode)
    {
        if (currentViewMode == newMode) return;
        currentViewMode = newMode;

        if (currentViewMode == ViewMode.FirstPerson)
        {
            // Transition to 1st Person (Attach to Head Bone)
            if (firstPersonParent != null)
            {
                transform.SetParent(firstPersonParent);
                transform.localPosition = firstPersonLocalPosition;
                transform.localRotation = Quaternion.identity;
                xRotation = 0f; // Reset vertical tilt
            }
        }
        else // ThirdPerson
        {
            // Transition to 3rd Person (Attach to Player Root)
            if (playerRoot != null)
            {
                transform.SetParent(playerRoot);
                transform.localPosition = thirdPersonOffset;
                // Initial rotation should match the player's facing direction
                transform.localRotation = Quaternion.Euler(20f, 0f, 0f); // Slight downward angle
                xRotation = 20f; // Set initial vertical angle for 3P
            }
        }
    }

    // --- Main Look Logic ---
    private void HandleCameraLook()
    {
        if (lookInput == Vector2.zero) return;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        if (currentViewMode == ViewMode.FirstPerson)
        {
            // 1P: Mouse controls local camera pitch (vertical) and player root yaw (horizontal)
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -verticalRotationLimit, verticalRotationLimit);
            transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

            playerRoot.Rotate(Vector3.up * mouseX);
        }
        else // ThirdPerson
        {
            // 3P: Mouse controls camera pitch (vertical) and player root yaw (horizontal)

            // Horizontal Rotation (Player Root)
            playerRoot.Rotate(Vector3.up * mouseX);

            // Vertical Rotation (Camera Pitch)
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -verticalRotationLimit, verticalRotationLimit);

            // Since the camera is parented to the playerRoot, local rotation controls pitch
            transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.localPosition = thirdPersonOffset;
        }
    }
}