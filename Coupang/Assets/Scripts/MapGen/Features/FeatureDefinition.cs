using UnityEngine;

/// <summary>
/// Defines a selectable "feature region" (e.g., Village, Facility Entrance, Ice Core).
/// The generator selects and places features BEFORE base terrain height is finalized,
/// so this asset must provide footprint / bounds information upfront.
/// </summary>
[CreateAssetMenu(menuName = "Map/Features/Feature Definition")]
public class FeatureDefinition : ScriptableObject
{
    public enum ContentType
    {
        None = 0,
        Prefab = 1,
        Scene = 2,
    }

    public enum FootprintShape
    {
        Circle = 0,
        Box = 1,
    }

    public enum FlattenTargetMode
    {
        /// <summary>Flatten to the sampled raw terrain height at the chosen placement center.</summary>
        SampledHeight = 0,

        /// <summary>Flatten to MapProfile.baseHeight.</summary>
        BaseHeight = 1,

        /// <summary>Flatten to a fixed world Y value.</summary>
        FixedWorldY = 2,
    }

    [Header("Identity")]
    [Tooltip("Unique identifier used for debugging / saved data.")]
    public string id = "Feature";

    [Header("Content")]
    public ContentType contentType = ContentType.Prefab;

    [Tooltip("Optional prefab content to spawn for this feature.")]
    public GameObject prefab;

    [Tooltip("Optional additive scene name to load for this feature.")]
    public string sceneName;

    [Tooltip("If true, the generator will spawn the prefab (if set) after terrain is built.")]
    public bool spawnContent = true;

    [Tooltip("Local-space offset applied when spawning prefab content (in terrainParent space).")]
    public Vector3 contentOffset = Vector3.zero;

    [Header("Footprint")]
    public FootprintShape footprintShape = FootprintShape.Circle;

    [Min(0f)]
    [Tooltip("Footprint radius in world units (used when footprintShape is Circle).")]
    public float footprintRadius = 12f;

    [Tooltip("Footprint size in world units (X=width, Y=length(Z)) used when footprintShape is Box.")]
    public Vector2 footprintBoxSize = new Vector2(20f, 20f);

    [Min(0f)]
    [Tooltip("Additional blend distance outside the footprint where height is smoothly interpolated.")]
    public float flattenFalloff = 6f;

    [Header("Flattening")]
    [Tooltip("If false, this feature does not influence the base terrain height map.")]
    public bool applyFlattenToBaseTerrain = true;

    public FlattenTargetMode flattenTargetMode = FlattenTargetMode.SampledHeight;

    [Tooltip("Only used when FlattenTargetMode is FixedWorldY.")]
    public float flattenFixedWorldY = 0f;

    [Tooltip("Extra world Y offset added to the flatten target height.")]
    public float flattenHeightOffset = 0f;

    [Header("Placement Constraints")]
    [Tooltip("Minimum radius from planet center (world origin).")]
    public float minRadiusFromCenter = 0f;

    [Tooltip("Maximum radius from planet center (world origin). Use a large number for no limit.")]
    public float maxRadiusFromCenter = 99999f;

    [Tooltip("If true, placement will avoid the landing zone area (MapProfile.landingRadius + falloff).")]
    public bool avoidLandingZone = true;

    [Tooltip("Extra distance beyond landingRadius+landingFalloff to avoid.")]
    public float extraAvoidLandingRadius = 0f;

    [Range(0f, 89f)]
    [Tooltip("Max allowed terrain slope in degrees within the footprint.")]
    public float maxSlopeDegrees = 10f;

    [Min(0f)]
    [Tooltip("Additional padding added to overlap checks (in world units).")]
    public float reservedPadding = 2f;

    [Header("Cave Interaction")]
    [Tooltip("If true, noise caves will NOT be carved inside the footprint XZ area.")]
    public bool disableNoiseCavesInFootprint = true;

    [Tooltip("If true, path caves (tunnels/rooms) will NOT be carved inside the footprint XZ area.")]
    public bool disablePathCavesInFootprint = true;

    [Header("Clearance & Foundation")]
    [Tooltip("If true, a 3D clearance volume will be forced to Air after base fill, preventing terrain from poking into the feature content.")]
    public bool applyClearanceVolume = true;

    [Tooltip("Clearance volume size in world units (X,Y,Z). This is axis-aligned in terrainParent space for now.")]
    public Vector3 clearanceSize = new Vector3(20f, 10f, 20f);

    [Tooltip("Center offset of the clearance volume relative to the feature placement position (terrainParent space).")]
    public Vector3 clearanceCenterOffset = new Vector3(0f, 5f, 0f);

    [Tooltip("If true, the generator will reinforce the ground below the footprint to prevent caves from making the area too thin.")]
    public bool applyFoundation = true;

    [Min(0)]
    [Tooltip("Foundation depth below the flatten target, in voxel units.")]
    public int foundationDepthVoxels = 2;

    [Header("Debug")]
    public bool debugDrawGizmos = false;

    public float GetFootprintHalfWidth()
    {
        if (footprintShape == FootprintShape.Circle)
            return footprintRadius;

        return Mathf.Max(0f, footprintBoxSize.x * 0.5f);
    }

    public float GetFootprintHalfLength()
    {
        if (footprintShape == FootprintShape.Circle)
            return footprintRadius;

        return Mathf.Max(0f, footprintBoxSize.y * 0.5f);
    }

    public float GetReservedRadius()
    {
        float r;
        if (footprintShape == FootprintShape.Circle)
        {
            r = footprintRadius;
        }
        else
        {
            float hx = GetFootprintHalfWidth();
            float hz = GetFootprintHalfLength();
            r = Mathf.Sqrt(hx * hx + hz * hz);
        }

        return Mathf.Max(0f, r + flattenFalloff + reservedPadding);
    }
}
