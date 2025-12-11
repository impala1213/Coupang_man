using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Map/Planet Profile")]
public class MapProfile : ScriptableObject
{
    // ─────────────────────────────────────
    // Planet / Biome meta
    // ─────────────────────────────────────
    [Header("Planet Info")]
    [Tooltip("Display name of this planet/biome.")]
    public string planetName = "Unnamed Planet";

    [Tooltip("Name of the biome. One biome per planet in this design.")]
    public string biomeName = "Default Biome";

    [Tooltip("Optional per-planet seed offset. Will be XORed with the base seed in MapRunner.")]
    public int seedOffset = 0;

    // ─────────────────────────────────────
    // Grid / voxel layout
    // ─────────────────────────────────────
    [Header("Grid / Voxel Layout")]
    [Tooltip("Number of tiles/voxels along the X axis.")]
    public int mapWidth = 64;

    [Tooltip("Number of tiles/voxels along the Z axis.")]
    public int mapLength = 64;

    [Tooltip("World size of one tile/voxel edge in meters.")]
    public float tileSize = 2f;

    // ─────────────────────────────────────
    // Height noise (used by terrain modules)
    // ─────────────────────────────────────
    [Header("Height Noise")]
    [Tooltip("Base height of the terrain in world units.")]
    public float baseHeight = 0f;

    [Tooltip("Maximum vertical variation produced by noise.")]
    public float heightScale = 16f;

    [Tooltip("Base frequency for Perlin noise sampling.")]
    public float noiseScale = 0.02f;

    [Tooltip("Number of octaves for fractal noise.")]
    public int noiseOctaves = 4;

    [Tooltip("Frequency multiplier per octave.")]
    public float noiseLacunarity = 2f;

    [Tooltip("Amplitude multiplier per octave (0-1).")]
    [Range(0f, 1f)]
    public float noisePersistence = 0.45f;

    // ─────────────────────────────────────
    // Landing zone (one biome per planet이지만,
    // 행성 중심에 착륙/시작 구역은 필요)
    // ─────────────────────────────────────
    [Header("Landing Area")]
    [Tooltip("Radius around the planet center that should be relatively flat.")]
    public float landingRadius = 10f;

    [Tooltip("Blend width between flat landing area and normal noise terrain.")]
    public float landingFalloff = 6f;

    // ─────────────────────────────────────
    // (Optional) Landing corridor - 현재 voxel 모듈에서는 아직 미사용.
    // MeshTerrainModule / NoiseTerrainModule가 필요하면 사용 가능.
    // ─────────────────────────────────────
    [Header("Landing Corridor (optional, currently not used by VoxelTerrainModule)")]
    [Tooltip("Enable corridor shaping for modules that support it.")]
    public bool useLandingCorridor = true;

    [Tooltip("Half width of the landing corridor.")]
    public float corridorHalfWidth = 4f;

    [Tooltip("Length of the landing corridor starting from landing radius.")]
    public float corridorLength = 30f;

    [Tooltip("Maximum height offset allowed inside corridor.")]
    public float corridorMaxHeightOffset = 4f;

    // ─────────────────────────────────────
    // Visual / palette
    // ─────────────────────────────────────
    [Header("Visual / Palette")]
    [Tooltip("Optional material that overrides the default terrain material.")]
    public Material groundMaterialOverride;

    [Tooltip("Optional gradient that represents terrain color over height (min -> max). Can be used in shaders.")]
    public Gradient terrainHeightGradient;

    [Tooltip("Sky color for this planet. Used by visual controllers, not by terrain itself.")]
    public Color skyColor = new Color(0.4f, 0.7f, 1f);

    [Tooltip("Fog color for this planet.")]
    public Color fogColor = new Color(0.5f, 0.6f, 0.7f);

    [Tooltip("Ambient light color for this planet.")]
    public Color ambientColor = Color.white;

    // ─────────────────────────────────────
    // Terrain logic
    // ─────────────────────────────────────
    [Header("Terrain Logic")]
    [Tooltip("Terrain module used to generate this planet. For voxel planets, assign a VoxelTerrainModule.")]
    public TerrainModule terrainModule;

    // ─────────────────────────────────────
    // Structures / Creatures / Items
    // 현재 VoxelTerrainModule에서는 사용하지 않지만,
    // 향후 voxel 표면 스폰 시스템에서 그대로 재사용할 예정.
    // ─────────────────────────────────────

    [Serializable]
    public class StructureEntry
    {
        [Header("Prefab & Count")]
        [Tooltip("Structure prefab to spawn on the terrain (rocks, trees, buildings, etc.).")]
        public GameObject prefab;

        [Tooltip("Minimum number of instances to spawn for this structure type.")]
        public int minCount = 0;

        [Tooltip("Maximum number of instances to spawn for this structure type.")]
        public int maxCount = 10;

        [Header("Height & Slope Conditions")]
        [Tooltip("Minimum world height where this structure can appear.")]
        public float minHeight = -999f;

        [Tooltip("Maximum world height where this structure can appear.")]
        public float maxHeight = 999f;

        [Tooltip("Maximum allowed slope angle in degrees (0 = flat, 90 = vertical).")]
        public float maxSlope = 30f;

        [Header("Distance Conditions")]
        [Tooltip("If true, the structure will avoid the landing zone around the origin.")]
        public bool avoidLandingZone = true;

        [Tooltip("Extra safe distance added around landing radius when avoidLandingZone is true.")]
        public float landingSafeMargin = 2f;

        [Header("Placement Offsets / Orientation")]
        [Tooltip("Additional Y offset applied after collider-based ground alignment.")]
        public float extraYOffset = 0f;

        [Tooltip("If true, rotate the structure to align with the terrain normal.")]
        public bool alignToTerrainNormal = false;
    }

    [Header("Structures")]
    [Tooltip("Structure spawn settings for this planet (single biome).")]
    public StructureEntry[] structures;

    [Serializable]
    public class MonsterEntry
    {
        [Header("Prefab & Count")]
        [Tooltip("Monster/creature prefab to spawn on the terrain.")]
        public GameObject prefab;

        [Tooltip("Minimum number of instances to spawn for this monster type.")]
        public int minCount = 0;

        [Tooltip("Maximum number of instances to spawn for this monster type.")]
        public int maxCount = 10;

        [Header("Height & Slope Conditions")]
        [Tooltip("Minimum world height where this monster can appear.")]
        public float minHeight = -999f;

        [Tooltip("Maximum world height where this monster can appear.")]
        public float maxHeight = 999f;

        [Tooltip("Maximum allowed slope angle in degrees.")]
        public float maxSlope = 35f;

        [Header("Distance Conditions")]
        [Tooltip("If true, the monster will avoid the landing zone around the origin.")]
        public bool avoidLandingZone = true;

        [Tooltip("Minimum radius from the planet center where this monster can spawn.")]
        public float minRadiusFromCenter = 15f;

        [Tooltip("Maximum radius from the planet center where this monster can spawn.")]
        public float maxRadiusFromCenter = 80f;

        [Header("Placement Offsets / Orientation")]
        [Tooltip("Additional Y offset applied after collider-based ground alignment.")]
        public float extraYOffset = 0f;

        [Tooltip("If true, rotate the monster to align with the terrain normal.")]
        public bool alignToTerrainNormal = true;
    }

    [Header("Monsters")]
    [Tooltip("Monster spawn settings for this planet (single biome).")]
    public MonsterEntry[] monsters;

    [Serializable]
    public class ItemEntry
    {
        [Header("Item & Count")]
        [Tooltip("Item definition that will be spawned on the terrain.")]
        public ItemDefinition itemDefinition;

        [Tooltip("Minimum number of instances to spawn for this item type.")]
        public int minCount = 0;

        [Tooltip("Maximum number of instances to spawn for this item type.")]
        public int maxCount = 20;

        [Header("Height & Slope Conditions")]
        [Tooltip("Minimum world height where this item can appear.")]
        public float minHeight = -999f;

        [Tooltip("Maximum world height where this item can appear.")]
        public float maxHeight = 999f;

        [Tooltip("Maximum allowed slope angle in degrees.")]
        public float maxSlope = 35f;

        [Header("Distance Conditions")]
        [Tooltip("If true, the item will avoid the landing zone around the origin.")]
        public bool avoidLandingZone = true;

        [Tooltip("Minimum radius from the planet center where this item can spawn.")]
        public float minRadiusFromCenter = 5f;

        [Tooltip("Maximum radius from the planet center where this item can spawn.")]
        public float maxRadiusFromCenter = 80f;

        [Header("Placement Offsets / Orientation")]
        [Tooltip("Additional Y offset applied after collider-based ground alignment.")]
        public float extraYOffset = 0f;

        [Tooltip("If true, rotate the item to align with the terrain normal.")]
        public bool alignToTerrainNormal = false;
    }

    [Header("Items")]
    [Tooltip("Item spawn settings for this planet (single biome).")]
    public ItemEntry[] items;
}
