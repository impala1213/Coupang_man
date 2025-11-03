using UnityEngine;
using UnityEngine.InputSystem;

public class FirstpersonCamera : MonoBehaviour
{
    [Header("Settings")]
    public float mouseSensitivity = 100f;
    public float verticalRotationLimit = 80f;

    private Transform playerRoot;

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
        if (playerRoot.GetComponent<PlayerController>() == null)
        {
            Debug.LogError("Player Root does not have PlayerController. Please check the hierarchy structure.");
        }
    }

    private void Update()
    {
        HandleCameraLook();
    }

    private void HandleCameraLook()
    {
        if (lookInput == Vector2.zero) return;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // 1. Camera Vertical Rotation (Local to Head Bone)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -verticalRotationLimit, verticalRotationLimit);

        // Apply vertical rotation to the camera (local rotation)
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // 2. Player Horizontal Rotation (Root Rotation)
        // Rotate the Player Root object to change the character's overall facing direction.
        playerRoot.Rotate(Vector3.up * mouseX);
    }
}