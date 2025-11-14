using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Map/Terrain/Mesh Terrain")]
public class MeshTerrainModule : TerrainModule
{
    public override void GenerateTerrain(MapProfile profile, Rng rng, Transform parent)
    {
        if (profile == null)
        {
            Debug.LogError("MeshTerrainModule: profile is null.");
            return;
        }

        if (parent == null)
        {
            Debug.LogError("MeshTerrainModule: parent is null.");
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

        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        float blendWidth = Mathf.Max(0f, profile.landingFalloff);

        bool useCorridor = profile.useLandingCorridor;
        float corridorHalfWidth = Mathf.Max(0f, profile.corridorHalfWidth);
        float corridorLength = Mathf.Max(0f, profile.corridorLength);
        float corridorMaxOffset = Mathf.Max(0f, profile.corridorMaxHeightOffset);

        int vertCountX = width + 1;
        int vertCountZ = length + 1;
        int vertCount = vertCountX * vertCountZ;

        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[width * length * 6];

        float totalWidth = width * tileSize;
        float totalLength = length * tileSize;
        float halfWidth = totalWidth * 0.5f;
        float halfLength = totalLength * 0.5f;

        float offsetX = rng.NextFloat(-1000f, 1000f);
        float offsetZ = rng.NextFloat(-1000f, 1000f);

        int vertIndex = 0;
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                float localX = (x * tileSize) - halfWidth;
                float localZ = (z * tileSize) - halfLength;

                float amplitude = 1f;
                float frequency = noiseScale;
                float value = 0f;
                float maxValue = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (localX * frequency) + offsetX;
                    float sampleZ = (localZ * frequency) + offsetZ;
                    float n = Mathf.PerlinNoise(sampleX, sampleZ);
                    value += n * amplitude;
                    maxValue += amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (maxValue > 0f)
                    value /= maxValue;

                float noiseHeight = baseHeight + (value - 0.5f) * 2f * heightScale;

                float dist = Mathf.Sqrt(localX * localX + localZ * localZ);
                float finalHeight = noiseHeight;

                if (flatRadius > 0f || blendWidth > 0f)
                {
                    if (dist <= flatRadius)
                    {
                        finalHeight = baseHeight;
                    }
                    else if (blendWidth > 0f && dist <= flatRadius + blendWidth)
                    {
                        float t = Mathf.InverseLerp(flatRadius, flatRadius + blendWidth, dist);
                        finalHeight = Mathf.Lerp(baseHeight, noiseHeight, t);
                    }
                    else
                    {
                        finalHeight = noiseHeight;
                    }
                }

                if (useCorridor && corridorHalfWidth > 0f && corridorLength > 0f && corridorMaxOffset > 0f)
                {
                    bool inCorridorZ = localZ >= -corridorHalfWidth && localZ <= corridorHalfWidth;
                    bool inCorridorX = localX >= 0f && localX <= flatRadius + corridorLength;

                    if (inCorridorZ && inCorridorX)
                    {
                        float d = Mathf.Max(flatRadius, localX);
                        float tCorr = 0f;
                        if (Mathf.Abs(corridorLength) > 0.0001f)
                        {
                            float start = flatRadius;
                            float end = flatRadius + corridorLength;
                            tCorr = Mathf.InverseLerp(start, end, d);
                        }

                        float maxOffset = Mathf.Lerp(0f, corridorMaxOffset, tCorr);
                        float minH = baseHeight - maxOffset;
                        float maxH = baseHeight + maxOffset;
                        finalHeight = Mathf.Clamp(finalHeight, minH, maxH);
                    }
                }

                vertices[vertIndex] = new Vector3(localX, finalHeight, localZ);
                uvs[vertIndex] = new Vector2((float)x / width, (float)z / length);
                vertIndex++;
            }
        }

        int triIndex = 0;
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i0 = z * vertCountX + x;
                int i1 = i0 + 1;
                int i2 = i0 + vertCountX;
                int i3 = i2 + 1;

                triangles[triIndex + 0] = i0;
                triangles[triIndex + 1] = i2;
                triangles[triIndex + 2] = i1;

                triangles[triIndex + 3] = i1;
                triangles[triIndex + 4] = i2;
                triangles[triIndex + 5] = i3;

                triIndex += 6;
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = vertCount > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject terrainGO = new GameObject("ProceduralTerrainMesh");
        terrainGO.transform.SetParent(parent, false);
        terrainGO.transform.localPosition = Vector3.zero;
        terrainGO.transform.localRotation = Quaternion.identity;

        MeshFilter mf = terrainGO.AddComponent<MeshFilter>();
        MeshRenderer mr = terrainGO.AddComponent<MeshRenderer>();
        MeshCollider mc = terrainGO.AddComponent<MeshCollider>();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        Material mat = null;
        if (profile.groundMaterialOverride != null)
        {
            mat = profile.groundMaterialOverride;
        }
        else if (profile.groundTilePrefab != null)
        {
            var rend = profile.groundTilePrefab.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                mat = rend.sharedMaterial;
            }
        }

        if (mat != null)
        {
            mr.sharedMaterial = mat;
        }

        if (profile.structures != null && profile.structures.Length > 0)
        {
            GenerateStructures(profile, rng, parent, vertices, mesh.normals);
        }

        if (profile.items != null && profile.items.Length > 0)
        {
            GenerateItems(profile, rng, parent, vertices, mesh.normals);
        }

        if (profile.monsters != null && profile.monsters.Length > 0)
        {
            GenerateMonsters(profile, rng, parent, vertices, mesh.normals);
        }
    }

    private void GenerateStructures(
        MapProfile profile,
        Rng rng,
        Transform parent,
        Vector3[] vertices,
        Vector3[] normals)
    {
        if (profile.structures == null || profile.structures.Length == 0)
            return;

        Transform structuresParent = parent.Find("Structures");
        if (structuresParent == null)
        {
            GameObject go = new GameObject("Structures");
            structuresParent = go.transform;
            structuresParent.SetParent(parent, false);
        }
        else
        {
            for (int i = structuresParent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(structuresParent.GetChild(i).gameObject);
            }
        }

        List<MapProfile.StructureEntry> entries = new List<MapProfile.StructureEntry>();
        foreach (var entry in profile.structures)
        {
            if (entry == null || entry.prefab == null)
                continue;

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            if (maxCount <= 0)
                continue;

            entries.Add(entry);
        }

        if (entries.Count == 0)
            return;

        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        int vertCount = vertices.Length;

        for (int e = 0; e < entries.Count; e++)
        {
            MapProfile.StructureEntry entry = entries[e];

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            int targetCount = rng.NextInt(minCount, maxCount + 1);

            if (targetCount <= 0)
                continue;

            int spawnedForEntry = 0;
            int maxAttempts = targetCount * 15;

            for (int attempt = 0; attempt < maxAttempts && spawnedForEntry < targetCount; attempt++)
            {
                int idx = rng.NextInt(0, vertCount);
                Vector3 localPos = vertices[idx];
                Vector3 normal = normals[idx];

                float height = localPos.y;
                float slopeAngle = Vector3.Angle(normal, Vector3.up);
                float distFromCenter = new Vector2(localPos.x, localPos.z).magnitude;

                if (height < entry.minHeight || height > entry.maxHeight)
                    continue;

                if (slopeAngle > entry.maxSlope)
                    continue;

                if (entry.avoidLandingZone && distFromCenter < flatRadius + 2f)
                    continue;

                Vector3 worldPos = parent.TransformPoint(localPos);

                float yaw = rng.NextFloat(0f, 360f);
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                Quaternion finalRot = yawRot;

                if (entry.alignToTerrainNormal)
                {
                    Vector3 worldNormal = parent.TransformDirection(normal).normalized;
                    if (worldNormal.sqrMagnitude > 0.0001f)
                    {
                        Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, worldNormal);
                        finalRot = slopeRot * yawRot;
                    }
                }

                GameObject inst = Object.Instantiate(entry.prefab, worldPos, finalRot, structuresParent);

                Collider col = inst.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    float groundY = worldPos.y;
                    float bottomY = col.bounds.min.y;
                    float offsetY = groundY - bottomY + entry.extraYOffset;
                    inst.transform.position += Vector3.up * offsetY;
                }
                else if (Mathf.Abs(entry.extraYOffset) > 0.0001f)
                {
                    inst.transform.position += Vector3.up * entry.extraYOffset;
                }

                spawnedForEntry++;
            }
        }
    }

    private void GenerateMonsters(
        MapProfile profile,
        Rng rng,
        Transform parent,
        Vector3[] vertices,
        Vector3[] normals)
    {
        if (profile.monsters == null || profile.monsters.Length == 0)
            return;

        Transform monstersParent = parent.Find("Monsters");
        if (monstersParent == null)
        {
            GameObject go = new GameObject("Monsters");
            monstersParent = go.transform;
            monstersParent.SetParent(parent, false);
        }
        else
        {
            for (int i = monstersParent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(monstersParent.GetChild(i).gameObject);
            }
        }

        List<MapProfile.MonsterEntry> entries = new List<MapProfile.MonsterEntry>();
        foreach (var entry in profile.monsters)
        {
            if (entry == null || entry.prefab == null)
                continue;

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            if (maxCount <= 0)
                continue;

            entries.Add(entry);
        }

        if (entries.Count == 0)
            return;

        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        int vertCount = vertices.Length;

        for (int e = 0; e < entries.Count; e++)
        {
            MapProfile.MonsterEntry entry = entries[e];

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            int targetCount = rng.NextInt(minCount, maxCount + 1);

            if (targetCount <= 0)
                continue;

            int spawnedForEntry = 0;
            int maxAttempts = targetCount * 20;

            for (int attempt = 0; attempt < maxAttempts && spawnedForEntry < targetCount; attempt++)
            {
                int idx = rng.NextInt(0, vertCount);
                Vector3 localPos = vertices[idx];
                Vector3 normal = normals[idx];

                float height = localPos.y;
                float slopeAngle = Vector3.Angle(normal, Vector3.up);
                float distFromCenter = new Vector2(localPos.x, localPos.z).magnitude;

                if (height < entry.minHeight || height > entry.maxHeight)
                    continue;

                if (slopeAngle > entry.maxSlope)
                    continue;

                if (entry.avoidLandingZone && distFromCenter < flatRadius + 3f)
                    continue;

                if (distFromCenter < entry.minRadiusFromCenter || distFromCenter > entry.maxRadiusFromCenter)
                    continue;

                Vector3 worldPos = parent.TransformPoint(localPos);

                float yaw = rng.NextFloat(0f, 360f);
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                Quaternion finalRot = yawRot;

                if (entry.alignToTerrainNormal)
                {
                    Vector3 worldNormal = parent.TransformDirection(normal).normalized;
                    if (worldNormal.sqrMagnitude > 0.0001f)
                    {
                        Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, worldNormal);
                        finalRot = slopeRot * yawRot;
                    }
                }

                GameObject inst = Object.Instantiate(entry.prefab, worldPos, finalRot, monstersParent);

                Collider col = inst.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    float groundY = worldPos.y;
                    float bottomY = col.bounds.min.y;
                    float offsetY = groundY - bottomY + entry.extraYOffset;
                    inst.transform.position += Vector3.up * offsetY;
                }
                else if (Mathf.Abs(entry.extraYOffset) > 0.0001f)
                {
                    inst.transform.position += Vector3.up * entry.extraYOffset;
                }

                spawnedForEntry++;
            }
        }
    }

    private void GenerateItems(
        MapProfile profile,
        Rng rng,
        Transform parent,
        Vector3[] vertices,
        Vector3[] normals)
    {
        if (profile.items == null || profile.items.Length == 0)
            return;

        Transform itemsParent = parent.Find("Items");
        if (itemsParent == null)
        {
            GameObject go = new GameObject("Items");
            itemsParent = go.transform;
            itemsParent.SetParent(parent, false);
        }
        else
        {
            for (int i = itemsParent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(itemsParent.GetChild(i).gameObject);
            }
        }

        List<MapProfile.ItemEntry> entries = new List<MapProfile.ItemEntry>();
        foreach (var entry in profile.items)
        {
            if (entry == null || entry.itemDefinition == null)
                continue;

            if (entry.itemDefinition.worldPrefab == null)
            {
                Debug.LogWarning($"MeshTerrainModule: ItemDefinition '{entry.itemDefinition.name}' has no worldPrefab.");
                continue;
            }

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            if (maxCount <= 0)
                continue;

            entries.Add(entry);
        }

        if (entries.Count == 0)
            return;

        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        int vertCount = vertices.Length;

        for (int e = 0; e < entries.Count; e++)
        {
            MapProfile.ItemEntry entry = entries[e];

            int minCount = Mathf.Max(0, entry.minCount);
            int maxCount = Mathf.Max(minCount, entry.maxCount);
            int targetCount = rng.NextInt(minCount, maxCount + 1);

            if (targetCount <= 0)
                continue;

            int spawnedForEntry = 0;
            int maxAttempts = targetCount * 20;

            for (int attempt = 0; attempt < maxAttempts && spawnedForEntry < targetCount; attempt++)
            {
                int idx = rng.NextInt(0, vertCount);
                Vector3 localPos = vertices[idx];
                Vector3 normal = normals[idx];

                float height = localPos.y;
                float slopeAngle = Vector3.Angle(normal, Vector3.up);
                float distFromCenter = new Vector2(localPos.x, localPos.z).magnitude;

                if (height < entry.minHeight || height > entry.maxHeight)
                    continue;

                if (slopeAngle > entry.maxSlope)
                    continue;

                if (entry.avoidLandingZone && distFromCenter < flatRadius + 2f)
                    continue;

                if (distFromCenter < entry.minRadiusFromCenter || distFromCenter > entry.maxRadiusFromCenter)
                    continue;

                Vector3 worldPos = parent.TransformPoint(localPos);

                float yaw = rng.NextFloat(0f, 360f);
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                Quaternion finalRot = yawRot;

                if (entry.alignToTerrainNormal)
                {
                    Vector3 worldNormal = parent.TransformDirection(normal).normalized;
                    if (worldNormal.sqrMagnitude > 0.0001f)
                    {
                        Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, worldNormal);
                        finalRot = slopeRot * yawRot;
                    }
                }

                GameObject prefab = entry.itemDefinition.worldPrefab;
                GameObject inst = Object.Instantiate(prefab, worldPos, finalRot, itemsParent);

                Collider col = inst.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    float groundY = worldPos.y;
                    float bottomY = col.bounds.min.y;
                    float offsetY = groundY - bottomY + entry.extraYOffset;
                    inst.transform.position += Vector3.up * offsetY;
                }
                else if (Mathf.Abs(entry.extraYOffset) > 0.0001f)
                {
                    inst.transform.position += Vector3.up * entry.extraYOffset;
                }

                spawnedForEntry++;
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
