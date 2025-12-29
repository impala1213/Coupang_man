using UnityEngine;

[CreateAssetMenu(menuName = "Map/Map Profile")]
public class MapProfile : ScriptableObject
{
    // ─────────────────────────────────────────
    // Basic grid / terrain noise
    // ─────────────────────────────────────────

    [Header("Grid")]
    [Tooltip("Number of voxels along X (world width in X).")]
    public int mapWidth = 64;

    [Tooltip("Number of voxels along Z (world length in Z).")]
    public int mapLength = 64;

    [Tooltip("World size of one voxel edge. Used by voxel terrain module.")]
    public float tileSize = 2f;

    [Header("Height Noise")]
    [Tooltip("Base world height for the terrain surface before noise is applied.")]
    public float baseHeight = 0f;

    [Tooltip("Vertical scale of the surface noise.")]
    public float heightScale = 8f;

    [Tooltip("Base frequency for 2D surface noise.")]
    public float noiseScale = 0.02f;

    [Tooltip("Number of octaves for surface noise.")]
    public int noiseOctaves = 4;

    [Tooltip("Frequency multiplier per octave for surface noise.")]
    public float noiseLacunarity = 2f;

    [Tooltip("Amplitude multiplier per octave for surface noise.")]
    public float noisePersistence = 0.45f;

    // ─────────────────────────────────────────
    // Landing area (used by VoxelTerrainModule + LandingHelper)
    // ─────────────────────────────────────────

    [Header("Landing Area")]
    [Tooltip("Radius around the world origin that is flattened for landing.")]
    public float landingRadius = 10f;

    [Tooltip("Blend width between the flat landing area and normal noisy terrain.")]
    public float landingFalloff = 6f;

    // ─────────────────────────────────────────
    // Visuals
    // ─────────────────────────────────────────

    [Header("Visual")]
    [Tooltip("Optional override material for ground/cave rendering. " +
             "If null, VoxelTerrainModule will use its own default materials.")]
    public Material groundMaterialOverride;

    // ─────────────────────────────────────────
    // Terrain logic
    // ─────────────────────────────────────────

    [Header("Terrain Logic")]
    [Tooltip("Terrain generator ScriptableObject. " +
             "Assign VoxelTerrainModule here for voxel-based planets.")]
    public TerrainModule terrainModule;

    // ─────────────────────────────────────────
    // Feature regions (selected BEFORE base terrain is finalized)
    // ─────────────────────────────────────────

    [Header("Feature Regions")]
    [Tooltip("If true, feature regions are selected and used to constrain base terrain height (flattening).")]
    public bool enableFeatureRegions = true;

    [Tooltip("Optional feature directory used to select and place regions like villages / facilities / cores.")]
    public FeatureDirectory featureDirectory;

    // ─────────────────────────────────────────
    // Voxel spawn rules (structures / monsters / items)
    // ─────────────────────────────────────────

    /// <summary>
    /// Common spawn filter for voxel-based objects.
    /// Controls: surface type (floor/wall/ceiling), region type (room/corridor/cave/exterior),
    /// world height limits, distance from planet center, and landing zone avoidance.
    /// </summary>
    [System.Serializable]
    public class VoxelSpawnFilter
    {
        [Header("Surface Type")]
        [Tooltip("Allow spawning on floor cells (Air above solid).")]
        public bool allowOnFloor = true;

        [Tooltip("Allow spawning on wall cells (Air next to solid side).")]
        public bool allowOnWall = false;

        [Tooltip("Allow spawning on ceiling cells (Air below solid).")]
        public bool allowOnCeiling = false;

        [Header("Region Type (from VoxelSurfaceFlags)")]
        [Tooltip("Allow cells that belong to rooms.")]
        public bool allowInRooms = true;

        [Tooltip("Allow cells that belong to corridors.")]
        public bool allowInCorridors = true;

        [Tooltip("Allow cells that belong to generic caves (not room/corridor).")]
        public bool allowInGenericCaves = true;

        [Tooltip("Allow cells marked as exterior (outdoor).")]
        public bool allowInExterior = false;

        [Header("Height / Radius Limits")]
        [Tooltip("Minimum allowed world Y for the spawn position.")]
        public float minWorldY = -9999f;

        [Tooltip("Maximum allowed world Y for the spawn position.")]
        public float maxWorldY = 9999f;

        [Tooltip("If true, avoids the landing zone defined by landingRadius.")]
        public bool avoidLandingZone = true;

        [Tooltip("Minimum horizontal distance from planet center in world units.")]
        public float minRadiusFromCenter = 0f;

        [Tooltip("Maximum horizontal distance from planet center in world units.")]
        public float maxRadiusFromCenter = 999999f;
    }

    /// <summary>
    /// Base class for voxel spawn entries:
    /// - spawn counts (min/max)
    /// - filter (surface/region/height/radius)
    /// - placement details (offset, normal alignment, random yaw)
    /// </summary>
    [System.Serializable]
    public abstract class VoxelSpawnEntryBase
    {
        [Tooltip("Optional identifier used for debugging or editor tools.")]
        public string id;

        [Header("Count")]
        [Tooltip("Minimum number of instances to spawn.")]
        public int minCount = 0;

        [Tooltip("Maximum number of instances to spawn.")]
        public int maxCount = 10;

        [Header("Filter")]
        public VoxelSpawnFilter filter = new VoxelSpawnFilter();

        [Header("Placement")]
        [Tooltip("Extra world-space offset applied after snapping to the surface.")]
        public Vector3 extraOffset = Vector3.zero;

        [Tooltip("If true, the spawned object up-axis will align with the surface normal.")]
        public bool alignToSurfaceNormal = true;

        [Tooltip("If true, apply a random yaw rotation around the surface normal.")]
        public bool randomYawAroundNormal = true;
    }

    [System.Serializable]
    public class VoxelStructureSpawnEntry : VoxelSpawnEntryBase
    {
        [Header("Prefab")]
        [Tooltip("Prefab to spawn as a structure (rocks, props, stalactites, etc.).")]
        public GameObject prefab;
    }

    [System.Serializable]
    public class VoxelMonsterSpawnEntry : VoxelSpawnEntryBase
    {
        [Header("Prefab")]
        [Tooltip("Prefab to spawn as a monster or NPC.")]
        public GameObject prefab;
    }

    [System.Serializable]
    public class VoxelItemSpawnEntry : VoxelSpawnEntryBase
    {
        [Header("Item")]
        [Tooltip("ItemDefinition whose worldPrefab will be spawned.")]
        public ItemDefinition itemDefinition;
    }

    [Header("Voxel Structures (for caves / underground)")]
    [Tooltip("Voxel-based structure spawn rules. Each entry is controlled by VoxelSpawnFilter.")]
    public VoxelStructureSpawnEntry[] voxelStructures;

    [Header("Voxel Monsters (for caves / underground)")]
    [Tooltip("Voxel-based monster spawn rules.")]
    public VoxelMonsterSpawnEntry[] voxelMonsters;

    [Header("Voxel Items (for caves / underground)")]
    [Tooltip("Voxel-based item spawn rules.")]
    public VoxelItemSpawnEntry[] voxelItems;
}