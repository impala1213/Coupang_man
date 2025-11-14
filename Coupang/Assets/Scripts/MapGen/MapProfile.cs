using UnityEngine;

[CreateAssetMenu(menuName = "Map/Map Profile")]
public class MapProfile : ScriptableObject
{
    [Header("Grid")]
    public int mapWidth = 64;
    public int mapLength = 64;
    public float tileSize = 2f;

    [Header("Height")]
    public float baseHeight = 0f;
    public float heightScale = 8f;
    public float noiseScale = 0.02f;
    public int noiseOctaves = 4;
    public float noiseLacunarity = 2f;
    public float noisePersistence = 0.45f;

    [Header("Landing Area")]
    public float landingRadius = 10f;
    public float landingFalloff = 6f;

    [Header("Landing Corridor")]
    public bool useLandingCorridor = true;
    public float corridorHalfWidth = 4f;
    public float corridorLength = 30f;
    public float corridorMaxHeightOffset = 4f;

    [Header("Visual")]
    public GameObject groundTilePrefab;
    public Material groundMaterialOverride;

    [Header("Terrain Logic")]
    public TerrainModule terrainModule;

    [System.Serializable]
    public class StructureEntry
    {
        public GameObject prefab;
        public int minCount = 0;
        public int maxCount = 10;
        public float minHeight = -999f;
        public float maxHeight = 999f;
        public float maxSlope = 30f;
        public bool avoidLandingZone = true;
        public float extraYOffset = 0f;
        public bool alignToTerrainNormal = false;
    }

    [Header("Structures")]
    public StructureEntry[] structures;

    [System.Serializable]
    public class MonsterEntry
    {
        public GameObject prefab;
        public int minCount = 0;
        public int maxCount = 10;
        public float minHeight = -999f;
        public float maxHeight = 999f;
        public float maxSlope = 35f;

        public bool avoidLandingZone = true;
        public float minRadiusFromCenter = 15f;
        public float maxRadiusFromCenter = 80f;

        public float extraYOffset = 0f;
        public bool alignToTerrainNormal = true;
    }

    [Header("Monsters")]
    public MonsterEntry[] monsters;

    [System.Serializable]
    public class ItemEntry
    {
        public ItemDefinition itemDefinition;
        public int minCount = 0;
        public int maxCount = 20;

        public float minHeight = -999f;
        public float maxHeight = 999f;
        public float maxSlope = 35f;

        public bool avoidLandingZone = true;
        public float minRadiusFromCenter = 5f;
        public float maxRadiusFromCenter = 80f;

        public float extraYOffset = 0f;
        public bool alignToTerrainNormal = false;
    }

    [Header("Items")]
    public ItemEntry[] items;
}
