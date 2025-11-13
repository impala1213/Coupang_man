using UnityEngine;

[CreateAssetMenu(menuName = "Map/Map Profile")]
public class MapProfile : ScriptableObject
{
    [Header("Grid")]
    public int mapWidth = 64;
    public int mapLength = 64;
    public float tileSize = 4f;

    [Header("Height")]
    public float baseHeight = 0f;
    public float heightScale = 6f;
    public float noiseScale = 0.1f;
    public int noiseOctaves = 1;
    public float noiseLacunarity = 2f;
    public float noisePersistence = 0.5f;

    [Header("Landing Area")]
    public float landingRadius = 8f;
    public float landingFalloff = 4f;

    [Header("Visual")]
    public GameObject groundTilePrefab;
    public Material groundMaterialOverride;

    [Header("Terrain Logic")]
    public TerrainModule terrainModule;
}
