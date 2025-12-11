using UnityEngine;

public class LandingHelper : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Parent transform under which the generated terrain is placed. If null, MapRunner's terrainParent or its own transform is used.")]
    public Transform terrainParent;

    [Tooltip("Anchor where the planet container (or landing object) will be positioned on the ground.")]
    public Transform planetContainerAnchor;

    [Tooltip("Reference to the MapRunner that generates the terrain.")]
    public MapRunner mapRunner;

    [Header("Container")]
    [Tooltip("Collider of the container object whose bottom should rest exactly on the ground.")]
    public Collider containerCollider;

    [Header("Landing Raycast")]
    [Tooltip("How high above the landing hint we start the raycast.")]
    public float raycastAboveOffset = 100f;

    [Tooltip("Maximum distance for the downward raycast.")]
    public float raycastDistance = 500f;

    [Tooltip("Layer mask used to detect terrain colliders.")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("If true, the anchor's up direction will align with the ground normal.")]
    public bool alignAnchorToGroundNormal = true;

    public bool IsEssentialReady { get; private set; }
    public Transform PlanetContainerAnchor => planetContainerAnchor;

    /// <summary>
    /// Initializes terrain + landing anchor using the given profile and seed.
    /// </summary>
    public void Initialize(MapProfile profile, int seed)
    {
        IsEssentialReady = false;

        if (mapRunner == null)
        {
            mapRunner = GetComponent<MapRunner>();
        }

        if (mapRunner == null)
        {
            Debug.LogError("LandingHelper: MapRunner is not assigned.");
            return;
        }

        // Ensure terrainParent is set.
        if (terrainParent == null)
        {
            terrainParent = mapRunner.terrainParent != null ? mapRunner.terrainParent : mapRunner.transform;
        }

        // Make sure MapRunner uses the same terrainParent.
        mapRunner.terrainParent = terrainParent;

        // Generate terrain for this planet.
        mapRunner.Run(profile, seed);

        // Place the anchor on top of the ground.
        SetupAnchor();

        IsEssentialReady = true;
    }

    /// <summary>
    /// Computes an approximate landing position from the terrain module
    /// and then raycasts down to snap the anchor exactly on the ground.
    /// After that, adjusts the anchor so that the container collider bottom
    /// is exactly on the ground with no manual offsets.
    /// </summary>
    public void SetupAnchor()
    {
        if (mapRunner == null)
        {
            Debug.LogError("LandingHelper.SetupAnchor: mapRunner is null.");
            return;
        }

        if (terrainParent == null)
        {
            terrainParent = mapRunner.terrainParent != null ? mapRunner.terrainParent : mapRunner.transform;
        }

        // Create anchor object if missing.
        if (planetContainerAnchor == null)
        {
            GameObject anchor = new GameObject("PlanetContainerAnchor");
            planetContainerAnchor = anchor.transform;
        }

        // Parent under this helper so its world position is easy to control.
        planetContainerAnchor.SetParent(transform, false);

        // Get a rough hint from the terrain module.
        Vector3 hint = transform.position;
        if (mapRunner.LastModuleUsed != null && mapRunner.LastProfileUsed != null)
        {
            hint = mapRunner.LastModuleUsed.GetLandingHint(mapRunner.LastProfileUsed, terrainParent);
        }

        // Raycast straight down from above the hint to find actual ground.
        Vector3 rayOrigin = hint + Vector3.up * raycastAboveOffset;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            // First place the anchor directly on the hit point.
            planetContainerAnchor.position = hit.point;

            if (alignAnchorToGroundNormal)
            {
                // Keep a stable forward direction projected onto the ground plane.
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.forward;
                }

                planetContainerAnchor.rotation = Quaternion.LookRotation(forward, hit.normal);
            }
            else
            {
                planetContainerAnchor.rotation = transform.rotation;
            }

            // Automatically adjust anchor height so that the container collider bottom
            // rests exactly on the ground, with no manual offset.
            if (containerCollider != null)
            {
                // Bounds are in world space and already include the current anchor transform.
                Bounds bounds = containerCollider.bounds;
                float currentBottomY = bounds.min.y;
                float targetBottomY = hit.point.y;

                float deltaY = targetBottomY - currentBottomY;

                // Move the anchor by the exact difference so that the collider bottom touches the ground.
                planetContainerAnchor.position += new Vector3(0f, deltaY, 0f);
            }
        }
        else
        {
            // Fallback: no hit, just place anchor at the hint.
            planetContainerAnchor.position = hint;
            planetContainerAnchor.rotation = transform.rotation;
        }
    }
}
