using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Map/Terrain/Pit Terrain")]
public class CaveTerrainModule : TerrainModule
{
    [Header("Cave Surface Entrance")]
    [Tooltip("If false, this module will not search/flatten a special cave entrance area.")]
    public bool useCaveEntranceFlatten = true;

    [Tooltip("Radius of the flattened entrance area.")]
    public float caveEntranceRadius = 6f;

    [Tooltip("Blend width around the entrance radius for smooth transition.")]
    public float caveEntranceBlendWidth = 4f;

    [Tooltip("Minimum world Y for the entrance center. Use this to force the entrance into lower regions.")]
    public float caveEntranceMinHeight = -999f;

    [Tooltip("Maximum world Y for the entrance center.")]
    public float caveEntranceMaxHeight = 999f;

    // Stored after generation
    private bool _hasCaveEntrance;
    private Vector3 _caveEntranceWorldPos;

    public override void GenerateTerrain(MapProfile profile, Rng rng, Transform parent)
    {
        if (profile == null)
        {
            Debug.LogError("CaveTerrainModule: profile is null.");
            return;
        }

        if (parent == null)
        {
            Debug.LogError("CaveTerrainModule: parent is null.");
            return;
        }

        _hasCaveEntrance = false;
        _caveEntranceWorldPos = Vector3.zero;

        // Clear previous children under parent
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

        // Entrance candidate indices
        List<int> entranceCandidates = new List<int>();

        // 기준이 될 착륙 위치 (landing hint)
        Vector3 landingPos = GetLandingHint(profile, parent);
        Vector2 landingXZ = new Vector2(landingPos.x, landingPos.z);

        // 거리 범위: Profile에서만 설정
        float minDistEntrance = Mathf.Max(0f, profile.surfaceEntranceMinDistanceFromLanding);
        float maxDistEntrance = profile.surfaceEntranceMaxDistanceFromLanding;

        if (maxDistEntrance <= 0f)
        {
            // 프로필에 안 넣었으면 맵 크기 기준으로 적당한 기본값 사용
            float maxPossible = Mathf.Min(totalWidth, totalLength) * 0.5f;
            maxDistEntrance = maxPossible;
        }

        maxDistEntrance = Mathf.Max(minDistEntrance, maxDistEntrance);

        int vertIndex = 0;
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                float localX = (x * tileSize) - halfWidth;
                float localZ = (z * tileSize) - halfLength;

                // --- noise-based base height ---
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

                // --- landing area flattening ---
                float distCenter = Mathf.Sqrt(localX * localX + localZ * localZ);
                float finalHeight = noiseHeight;

                if (flatRadius > 0f || blendWidth > 0f)
                {
                    if (distCenter <= flatRadius)
                    {
                        finalHeight = baseHeight;
                    }
                    else if (blendWidth > 0f && distCenter <= flatRadius + blendWidth)
                    {
                        float t = Mathf.InverseLerp(flatRadius, flatRadius + blendWidth, distCenter);
                        finalHeight = Mathf.Lerp(baseHeight, noiseHeight, t);
                    }
                    else
                    {
                        finalHeight = noiseHeight;
                    }
                }

                // --- landing corridor clamping (optional) ---
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

                // 로컬 좌표로 정점 저장
                Vector3 localPos = new Vector3(localX, finalHeight, localZ);
                vertices[vertIndex] = localPos;
                uvs[vertIndex] = new Vector2((float)x / width, (float)z / length);

                // --- collect cave entrance candidates (distance from landingPos) ---
                if (useCaveEntranceFlatten)
                {
                    // 월드 좌표로 변환
                    Vector3 worldPos = parent.TransformPoint(localPos);
                    float worldY = worldPos.y;
                    Vector2 posXZ = new Vector2(worldPos.x, worldPos.z);

                    // 1. 높이 조건
                    if (worldY >= caveEntranceMinHeight && worldY <= caveEntranceMaxHeight)
                    {
                        // 2. "착륙지점" 기준 거리 조건
                        float distFromLanding = Vector2.Distance(posXZ, landingXZ);
                        if (distFromLanding >= minDistEntrance && distFromLanding <= maxDistEntrance)
                        {
                            entranceCandidates.Add(vertIndex);
                        }
                    }
                }

                vertIndex++;
            }
        }

        // --- pick one entrance candidate and flatten around it ---
        Vector2 entranceCenterLocalXZ = Vector2.zero;
        float entranceTargetHeight = 0f;

        if (useCaveEntranceFlatten && entranceCandidates.Count > 0)
        {
            int chosenIndex = entranceCandidates[rng.NextInt(0, entranceCandidates.Count)];
            Vector3 chosenLocal = vertices[chosenIndex];

            entranceCenterLocalXZ = new Vector2(chosenLocal.x, chosenLocal.z);
            entranceTargetHeight = chosenLocal.y;

            // Save world position for MapRunner (parent is the anchor)
            _caveEntranceWorldPos = parent.TransformPoint(chosenLocal);
            _hasCaveEntrance = true;

            float radius = Mathf.Max(0f, caveEntranceRadius);
            float blend = Mathf.Max(0f, caveEntranceBlendWidth);
            float outerR = radius + blend;
            float outerRSqr = outerR * outerR;

            // Second pass: flatten around the entrance position (로컬 기준)
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                Vector2 posXZ = new Vector2(v.x, v.z);
                float distSq = (posXZ - entranceCenterLocalXZ).sqrMagnitude;

                if (distSq <= outerRSqr)
                {
                    float d = Mathf.Sqrt(distSq);

                    float t;
                    if (d <= radius)
                    {
                        t = 0f; // fully flattened
                    }
                    else
                    {
                        t = Mathf.InverseLerp(radius, outerR, d);
                    }

                    float originalY = v.y;
                    float newY = Mathf.Lerp(entranceTargetHeight, originalY, t * t);
                    v.y = newY;
                    vertices[i] = v;
                }
            }
        }
        else
        {
            _hasCaveEntrance = false;
            _caveEntranceWorldPos = Vector3.zero;
        }

        // Triangles
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

        GameObject terrainGO = new GameObject("ProceduralTerrainMesh_Pit");
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
    }

    public override Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        if (parent == null)
            return Vector3.zero;

        return parent.position + Vector3.up * 2f;
    }

    public override bool TryGetCaveEntranceHint(MapProfile profile, Transform parent, out Vector3 pos)
    {
        if (_hasCaveEntrance)
        {
            pos = _caveEntranceWorldPos;
            return true;
        }

        pos = Vector3.zero;
        return false;
    }
}
