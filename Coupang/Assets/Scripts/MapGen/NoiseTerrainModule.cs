using UnityEngine;

[CreateAssetMenu(menuName = "Map/Terrain/Noise Terrain")]
public class NoiseTerrainModule : TerrainModule
{
    public override void GenerateTerrain(MapProfile profile, Rng rng, Transform parent)
    {
        if (profile == null)
        {
            Debug.LogError("NoiseTerrainModule: profile is null.");
            return;
        }

        if (parent == null)
        {
            Debug.LogError("NoiseTerrainModule: parent is null.");
            return;
        }

        GameObject tilePrefab = profile.groundTilePrefab;
        if (tilePrefab == null)
        {
            Debug.LogError("NoiseTerrainModule: profile.groundTilePrefab is not assigned.");
            return;
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(parent.GetChild(i).gameObject);
        }

        int width = Mathf.Max(1, profile.mapWidth);
        int length = Mathf.Max(1, profile.mapLength);
        float tileSize = Mathf.Max(0.1f, profile.tileSize);

        float baseHeight = profile.baseHeight;
        float heightScale = profile.heightScale;
        float noiseScale = Mathf.Max(0.0001f, profile.noiseScale);
        int octaves = Mathf.Max(1, profile.noiseOctaves);
        float lacunarity = Mathf.Max(1f, profile.noiseLacunarity);
        float persistence = Mathf.Clamp01(profile.noisePersistence);

        float offsetX = rng.NextFloat(-1000f, 1000f);
        float offsetZ = rng.NextFloat(-1000f, 1000f);

        Vector3 origin = parent.position;

        float halfWidth = width * 0.5f;
        float halfLength = length * 0.5f;

        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float localX = (x - halfWidth) * tileSize;
                float localZ = (z - halfLength) * tileSize;

                float worldX = origin.x + localX;
                float worldZ = origin.z + localZ;

                float amplitude = 1f;
                float frequency = noiseScale;
                float value = 0f;
                float maxValue = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (x * frequency) + offsetX;
                    float sampleZ = (z * frequency) + offsetZ;
                    float n = Mathf.PerlinNoise(sampleX, sampleZ);
                    value += n * amplitude;
                    maxValue += amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (maxValue > 0f)
                    value /= maxValue;

                float height = baseHeight + (value - 0.5f) * 2f * heightScale;

                float dist = Mathf.Sqrt(localX * localX + localZ * localZ);
                if (dist < profile.landingRadius)
                {
                    float t = Mathf.InverseLerp(profile.landingRadius, profile.landingRadius - profile.landingFalloff, dist);
                    t = Mathf.Clamp01(t);
                    height = Mathf.Lerp(baseHeight, height, t);
                }

                Vector3 pos = new Vector3(worldX, height, worldZ);
                GameObject tile = Object.Instantiate(tilePrefab, pos, Quaternion.identity, parent);

                Vector3 scale = tile.transform.localScale;
                scale.x = tileSize;
                scale.z = tileSize;
                tile.transform.localScale = scale;

                if (profile.groundMaterialOverride != null)
                {
                    var renderer = tile.GetComponentInChildren<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = profile.groundMaterialOverride;
                    }
                }
            }
        }
    }

    public override Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        if (parent == null)
            return Vector3.zero;

        return parent.position + Vector3.up * 2f;
    }
}
