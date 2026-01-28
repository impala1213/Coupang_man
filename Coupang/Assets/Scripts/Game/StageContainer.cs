using UnityEngine;

/// <summary>
/// Marks a container placed in a scene (Ship or Gameplay).
/// GameSession uses this to move ONLY the player and cargo items between scenes,
/// while the container object itself remains authored in each scene.
///
/// Important:
/// - cargoRoot: where loose cargo items live inside the container.
///   GameSession preserves the cargo local transform relative to cargoRoot when transferring.
/// </summary>
[DisallowMultipleComponent]
public class StageContainer : MonoBehaviour
{
    [Header("Roots")]
    [Tooltip("Root transform representing the container's frame of reference. Defaults to this transform.")]
    public Transform containerRoot;

    [Tooltip("Root under which cargo WorldItems are stored inside the container. Defaults to containerRoot.")]
    public Transform cargoRoot;

    [Header("Optional")]
    [Tooltip("Optional player spawn anchor near/inside this container.")]
    public Transform playerSpawnAnchor;

    [Tooltip("Optional door animator for this container.")]
    public Animator doorAnimator;

    private void Awake()
    {
        ResolveDefaults();
    }

    public void ResolveDefaults()
    {
        if (containerRoot == null)
            containerRoot = transform;

        if (cargoRoot == null)
            cargoRoot = containerRoot;
    }
}
