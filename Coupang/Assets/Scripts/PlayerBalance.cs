using UnityEngine;

public class PlayerBalance : MonoBehaviour
{
    // --- Required References ---
    [Header("Required References")]
    // Assign the Player Root object with the PlayerController.cs script.
    public GameObject PlayerControllerRoot;
    // Assign the Transform of the spine bone (e.g., Spine_02).
    public Transform SpineBone;

    [Header("Lean Settings")]
    // Maximum angle the character can lean (e.g., 15 to 25 degrees).
    public float MaxLeanAngle = 20f;
    // Speed at which the character reacts to balance changes.
    public float LeanSpeed = 10f;

    // --- Internal Variables ---
    private PlayerController playerController;
    private Quaternion initialSpineRotation;

    private void Start()
    {
        if (PlayerControllerRoot == null)
        {
            Debug.LogError("Player Controller Root is not assigned. Please assign the Player Root object.");
            enabled = false;
            return;
        }

        playerController = PlayerControllerRoot.GetComponent<PlayerController>();
        if (playerController == null)
        {
            Debug.LogError("Could not find PlayerController.cs on the Player Controller Root.");
            enabled = false;
            return;
        }

        if (SpineBone == null)
        {
            Debug.LogError("Spine Bone Transform is not assigned. Please assign the character's spine bone.");
            enabled = false;
            return;
        }

        // Store the initial rotation of the spine bone in local space.
        initialSpineRotation = SpineBone.localRotation;
    }

    // Executes after the Animator has completed all animation calculations.
    private void LateUpdate()
    {
        if (playerController == null || SpineBone == null) return;

        // Get the balanceFactor (-1.0 to 1.0) from the PlayerController.
        float balance = playerController.balanceFactor;

        // Convert the balance factor into an angle. We use negative to align lean direction.
        float targetAngle = -balance * MaxLeanAngle;

        // Calculate the target rotation: Rotate around the local Z-axis (which typically handles side-to-side lean).
        Quaternion targetRotation = initialSpineRotation * Quaternion.Euler(
            0,
            0,
            targetAngle
        );

        // Smoothly interpolate the current rotation towards the target rotation.
        SpineBone.localRotation = Quaternion.Slerp(SpineBone.localRotation, targetRotation, Time.deltaTime * LeanSpeed);
    }
}