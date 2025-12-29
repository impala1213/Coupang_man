using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Map/Terrain/Voxel Terrain")]
public class VoxelTerrainModule : TerrainModule
{
    // ─────────────────────────────────────
    // Voxel layout
    // ─────────────────────────────────────
    [Header("Voxel Settings")]
    [Tooltip("Number of vertical voxel layers for the terrain chunk.")]
    public int voxelHeight = 64;

    [Tooltip("If > 0, overrides MapProfile.tileSize as voxel size. Otherwise MapProfile.tileSize is used.")]
    public float voxelSizeOverride = 0f;

    // ─────────────────────────────────────
    // Underground slab (planet crust)
    // ─────────────────────────────────────
    [Header("Underground Slab")]
    [Tooltip("World Y of the TOP of the underground slab.\nFor example, -20 with thickness 40 -> slab from -60 to -20.")]
    public float undergroundBottomY = -20f;

    [Tooltip("Slab thickness downward in world units.\nFor example, thickness 40 -> slabBottom = undergroundBottomY - 40.")]
    public float undergroundThickness = 20f;

    // ─────────────────────────────────────
    // Materials
    // ─────────────────────────────────────
    [Header("Materials")]
    [Tooltip("Default material for ground voxels (world Y >= 0).")]
    public Material defaultGroundMaterial;

    [Tooltip("Default material for underground voxels (world Y < 0).\nUsed for solid crust and boundary shell.")]
    public Material defaultUndergroundMaterial;

    [Tooltip("Material for deep cave voxels.\nUnderground voxels with worldY <= caveMaterialStartY will use this.")]
    public Material defaultCaveMaterial;

    [Header("Cave Material Depth Threshold")]
    [Tooltip("World Y threshold below which underground voxels will use the Cave material instead of Underground.\nFor example, -30 means all underground voxels with worldY <= -30 become Cave.")]
    public float caveMaterialStartY = -30f;

    // ─────────────────────────────────────
    // Boundary shell (underground walls)
    // ─────────────────────────────────────
    [Header("Boundary Shell (Underground Only)")]
    [Tooltip("If true, creates an underground shell at the map border so the player cannot see outside the world.")]
    public bool enableBoundaryShell = true;

    [Tooltip("Maximum extra shell thickness (in voxels) from the border inward.\nActual thickness per column will be 1..maxBoundaryThickness.")]
    public int maxBoundaryThickness = 3;

    [Tooltip("2D noise scale used to vary shell thickness along the border.")]
    public float boundaryNoiseScale = 0.25f;

    // ─────────────────────────────────────
    // Debug
    // ─────────────────────────────────────
    [Header("Debug")]
    [Tooltip("If true, logs a short summary of floor/wall/ceiling counts after surface data is built.")]
    public bool debugLogSurfaceSummary = false;

    // ─────────────────────────────────────
    // Main entry
    // ─────────────────────────────────────
    public override void GenerateTerrain(MapProfile profile, Rng rng, Transform parent)
    {
        if (profile == null)
        {
            Debug.LogError("VoxelTerrainModule: profile is null.");
            return;
        }

        if (parent == null)
        {
            Debug.LogError("VoxelTerrainModule: parent is null.");
            return;
        }

        // Clear previous terrain
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.Destroy(parent.GetChild(i).gameObject);

        int width = Mathf.Max(1, profile.mapWidth);
        int length = Mathf.Max(1, profile.mapLength);

        float voxelSize = voxelSizeOverride > 0f ? voxelSizeOverride : Mathf.Max(0.1f, profile.tileSize);

        // Surface noise params
        float baseHeight = profile.baseHeight;
        float heightScale = profile.heightScale;
        float noiseScale = Mathf.Max(0.0001f, profile.noiseScale);
        int octaves = Mathf.Max(1, profile.noiseOctaves);
        float lacunarity = Mathf.Max(1f, profile.noiseLacunarity);
        float persistence = Mathf.Clamp01(profile.noisePersistence);

        // Landing flatten
        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        float blendWidth = Mathf.Max(0f, profile.landingFalloff);

        // Offsets
        float offsetX = rng.NextFloat(-1000f, 1000f);
        float offsetZ = rng.NextFloat(-1000f, 1000f);

        float totalWidth = width * voxelSize;
        float totalLength = length * voxelSize;
        float halfWidth = totalWidth * 0.5f;
        float halfLength = totalLength * 0.5f;

        // 1) Raw noise-only height map (feature planning uses this)
        float[,] rawHeightMap = new float[width, length];
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float localX = (x * voxelSize) - halfWidth;
                float localZ = (z * voxelSize) - halfLength;

                float amplitude = 1f;
                float frequency = noiseScale;
                float value = 0f;
                float maxValue = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (localX + offsetX) * frequency;
                    float sampleZ = (localZ + offsetZ) * frequency;

                    float perlin = Mathf.PerlinNoise(sampleX, sampleZ); // 0..1
                    value += perlin * amplitude;
                    maxValue += amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (maxValue > 0f)
                    value /= maxValue;

                float noiseHeight = baseHeight + (value - 0.5f) * 2f * heightScale;
                rawHeightMap[x, z] = noiseHeight;
            }
        }

        // 2) Feature plan (decide placements BEFORE finalizing height)
        List<FeaturePlacement> plannedFeatures = new List<FeaturePlacement>();
        if (profile.enableFeatureRegions && profile.featureDirectory != null)
        {
            plannedFeatures = FeaturePlanner.Plan(
                profile,
                profile.featureDirectory,
                rng.Split(9001),
                rawHeightMap,
                voxelSize,
                halfWidth,
                halfLength
            );
        }

        // 3) Final height map: landing flatten + feature flatten
        float[,] heightMap = new float[width, length];
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float localX = (x * voxelSize) - halfWidth;
                float localZ = (z * voxelSize) - halfLength;

                float noiseHeight = rawHeightMap[x, z];

                float finalHeight = ApplyLandingFlatten(localX, localZ, noiseHeight, baseHeight, flatRadius, blendWidth);
                finalHeight = ApplyFeatureFlatten(plannedFeatures, localX, localZ, finalHeight);

                heightMap[x, z] = finalHeight;
            }
        }

        // Underground slab range
        float thickness = Mathf.Max(0f, undergroundThickness);
        float slabTopY = undergroundBottomY;
        float slabBottomY = undergroundBottomY - thickness;

        float worldBottomY = slabBottomY;

        // Create chunk
        GameObject chunkGO = new GameObject("VoxelChunk_Main");
        chunkGO.transform.SetParent(parent, false);
        chunkGO.transform.localPosition = new Vector3(-halfWidth, worldBottomY, -halfLength);
        chunkGO.transform.localRotation = Quaternion.identity;

        VoxelChunk chunk = chunkGO.AddComponent<VoxelChunk>();

        int verticalVoxels = Mathf.Max(1, voxelHeight);
        GetTerrainMaterials(profile, out Material groundMat, out Material undergroundMat, out Material caveMat);
        chunk.Initialize(width, verticalVoxels, length, groundMat, undergroundMat, caveMat);

        // Fill voxels (surface + slab)
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float surfaceHeight = heightMap[x, z];

                for (int y = 0; y < verticalVoxels; y++)
                {
                    float worldY = worldBottomY + y * voxelSize;

                    float baseDensity;
                    if (worldY <= slabTopY)
                        baseDensity = 1f; // slab
                    else if (worldY <= surfaceHeight)
                        baseDensity = 1f; // above slab up to surface
                    else
                        baseDensity = -1f; // air

                    if (baseDensity > 0f)
                        chunk.voxels[x, y, z] = (worldY >= 0f) ? VoxelType.Ground : VoxelType.Underground;
                    else
                        chunk.voxels[x, y, z] = VoxelType.Air;
                }
            }
        }

        // Boundary shell (underground only)
        if (enableBoundaryShell)
        {
            SealOuterShell(
                chunk.voxels,
                width,
                verticalVoxels,
                length,
                worldBottomY,
                voxelSize,
                rng.Split(98765)
            );
        }

        // Mark deep underground as Cave material
        MarkCaveWalls(
            chunk.voxels,
            width,
            verticalVoxels,
            length,
            worldBottomY,
            voxelSize,
            slabBottomY,
            slabTopY
        );

        // Apply feature clearance / foundation before meshing
        ApplyFeatureClearanceAndFoundation(
            chunk.voxels,
            width,
            verticalVoxels,
            length,
            plannedFeatures,
            voxelSize,
            halfWidth,
            halfLength,
            worldBottomY
        );

        chunk.BuildMesh(voxelSize);

        // Surface classification
        VoxelSurfaceData surfaceData = VoxelSurfaceBuilder.BuildSurfaceData(
            chunkGO,
            chunk.voxels,
            voxelSize,
            worldBottomY,
            null,
            null,
            maxWorldY: null
        );

        if (debugLogSurfaceSummary && surfaceData != null)
            LogSurfaceSummary(surfaceData);

        // Spawn voxel rules (structures/monsters/items)
        VoxelSpawnUtility.SpawnFromProfile(
            profile,
            rng.Split(7777),
            parent,
            chunk,
            surfaceData
        );

        // Spawn feature contents (prefabs / caves)
        SpawnFeatureContent(parent, plannedFeatures, rng.Split(99001));
    }

    // ─────────────────────────────────────
    // Materials / Landing hint
    // ─────────────────────────────────────
    private void GetTerrainMaterials(MapProfile profile, out Material groundMat, out Material undergroundMat, out Material caveMat)
    {
        if (profile != null && profile.groundMaterialOverride != null)
            groundMat = profile.groundMaterialOverride;
        else
            groundMat = defaultGroundMaterial;

        undergroundMat = defaultUndergroundMaterial != null ? defaultUndergroundMaterial : groundMat;
        caveMat = defaultCaveMaterial != null ? defaultCaveMaterial : undergroundMat;
    }

    public override Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        if (parent == null) return Vector3.zero;
        return parent.position + Vector3.up * 2f;
    }

    // ─────────────────────────────────────
    // Mark cave material by depth
    // ─────────────────────────────────────
    private void MarkCaveWalls(
        VoxelType[,,] voxels,
        int sizeX,
        int sizeY,
        int sizeZ,
        float worldBottomY,
        float voxelSize,
        float slabBottomY,
        float slabTopY)
    {
        for (int y = 0; y < sizeY; y++)
        {
            float worldY = worldBottomY + y * voxelSize;

            if (worldY < slabBottomY || worldY > slabTopY)
                continue;

            if (worldY > caveMaterialStartY)
                continue;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    if (voxels[x, y, z] == VoxelType.Underground)
                        voxels[x, y, z] = VoxelType.Cave;
                }
            }
        }
    }

    // ─────────────────────────────────────
    // Boundary shell (underground only)
    // ─────────────────────────────────────
    private void SealOuterShell(
        VoxelType[,,] voxels,
        int sizeX,
        int sizeY,
        int sizeZ,
        float worldBottomY,
        float voxelSize,
        Rng rng)
    {
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            return;

        int maxX = sizeX - 1;
        int maxZ = sizeZ - 1;

        // Bottom always solid
        int bottomY = 0;
        for (int z = 0; z < sizeZ; z++)
            for (int x = 0; x < sizeX; x++)
                voxels[x, bottomY, z] = VoxelType.Underground;

        float offA = rng.NextFloat(-1000f, 1000f);
        float offB = rng.NextFloat(-1000f, 1000f);
        float offC = rng.NextFloat(-1000f, 1000f);
        float offD = rng.NextFloat(-1000f, 1000f);

        int maxThick = Mathf.Max(1, maxBoundaryThickness);
        int maxAllowedThickX = Mathf.Max(1, sizeX / 4);
        int maxAllowedThickZ = Mathf.Max(1, sizeZ / 4);
        float ns = Mathf.Max(0.0001f, boundaryNoiseScale);

        float shellStartWorldY = undergroundBottomY; // only below slab top

        // Left / right walls
        for (int y = 0; y < sizeY; y++)
        {
            float worldY = worldBottomY + y * voxelSize;
            if (worldY > shellStartWorldY)
                continue;

            for (int z = 0; z < sizeZ; z++)
            {
                float nL = Mathf.PerlinNoise((y + offA) * ns, (z + offB) * ns);
                int thickL = 1 + Mathf.FloorToInt(nL * maxThick);
                thickL = Mathf.Clamp(thickL, 1, maxAllowedThickX);

                for (int x = 0; x < thickL; x++)
                    voxels[x, y, z] = VoxelType.Underground;

                float nR = Mathf.PerlinNoise((y + offC) * ns, (z + offD) * ns);
                int thickR = 1 + Mathf.FloorToInt(nR * maxThick);
                thickR = Mathf.Clamp(thickR, 1, maxAllowedThickX);

                for (int i = 0; i < thickR; i++)
                {
                    int x = maxX - i;
                    voxels[x, y, z] = VoxelType.Underground;
                }
            }
        }

        // Front / back walls
        for (int y = 0; y < sizeY; y++)
        {
            float worldY = worldBottomY + y * voxelSize;
            if (worldY > shellStartWorldY)
                continue;

            for (int x = 0; x < sizeX; x++)
            {
                float nF = Mathf.PerlinNoise((y + offB) * ns, (x + offC) * ns);
                int thickF = 1 + Mathf.FloorToInt(nF * maxThick);
                thickF = Mathf.Clamp(thickF, 1, maxAllowedThickZ);

                for (int i = 0; i < thickF; i++)
                    voxels[x, y, i] = VoxelType.Underground;

                float nB = Mathf.PerlinNoise((y + offD) * ns, (x + offA) * ns);
                int thickB = 1 + Mathf.FloorToInt(nB * maxThick);
                thickB = Mathf.Clamp(thickB, 1, maxAllowedThickZ);

                for (int i = 0; i < thickB; i++)
                {
                    int z = maxZ - i;
                    voxels[x, y, z] = VoxelType.Underground;
                }
            }
        }
    }

    // ─────────────────────────────────────
    // Debug: surface summary
    // ─────────────────────────────────────
    private void LogSurfaceSummary(VoxelSurfaceData data)
    {
        if (data == null || data.flags == null)
            return;

        var flags = data.flags;
        int sizeX = data.sizeX;
        int sizeY = data.sizeY;
        int sizeZ = data.sizeZ;

        int floorCount = 0;
        int wallCount = 0;
        int ceilingCount = 0;

        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    VoxelSurfaceFlags f = flags[x, y, z];
                    if (f == VoxelSurfaceFlags.None)
                        continue;

                    if ((f & VoxelSurfaceFlags.Floor) != 0) floorCount++;
                    if ((f & VoxelSurfaceFlags.Wall) != 0) wallCount++;
                    if ((f & VoxelSurfaceFlags.Ceiling) != 0) ceilingCount++;
                }
            }
        }

        Debug.Log($"[VoxelTerrainModule] Surface summary on '{data.gameObject.name}': Floors={floorCount}, Walls={wallCount}, Ceilings={ceilingCount}");
    }

    // ─────────────────────────────────────
    // Feature flatten helpers
    // ─────────────────────────────────────
    private static float ApplyLandingFlatten(float localX, float localZ, float noiseHeight, float baseHeight, float flatRadius, float blendWidth)
    {
        float dist = Mathf.Sqrt(localX * localX + localZ * localZ);

        if (flatRadius <= 0f && blendWidth <= 0f)
            return noiseHeight;

        if (dist <= flatRadius)
            return baseHeight;

        if (blendWidth > 0f && dist <= flatRadius + blendWidth)
        {
            float t = Mathf.InverseLerp(flatRadius, flatRadius + blendWidth, dist);
            return Mathf.Lerp(baseHeight, noiseHeight, t);
        }

        return noiseHeight;
    }

    private static float ApplyFeatureFlatten(List<FeaturePlacement> placements, float localX, float localZ, float currentHeight)
    {
        if (placements == null || placements.Count == 0)
            return currentHeight;

        float h = currentHeight;

        for (int i = 0; i < placements.Count; i++)
        {
            FeaturePlacement p = placements[i];
            FeatureDefinition f = p.feature;
            if (f == null || !f.applyFlattenToBaseTerrain)
                continue;

            float dx = localX - p.localPosition.x;
            float dz = localZ - p.localPosition.z;

            float w = GetFootprintBlendWeight(f, dx, dz);
            if (w <= 0f)
                continue;

            h = Mathf.Lerp(h, p.targetSurfaceWorldY, w);
        }

        return h;
    }

    private static float GetFootprintBlendWeight(FeatureDefinition f, float dx, float dz)
    {
        if (f == null)
            return 0f;

        float falloff = Mathf.Max(0.0001f, f.flattenFalloff);

        if (f.footprintShape == FeatureDefinition.FootprintShape.Circle)
        {
            float r = Mathf.Max(0f, f.footprintRadius);
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist <= r)
                return 1f;

            if (dist <= r + falloff)
                return 1f - Mathf.InverseLerp(r, r + falloff, dist);

            return 0f;
        }
        else
        {
            float hx = Mathf.Max(0f, f.footprintBoxSize.x * 0.5f);
            float hz = Mathf.Max(0f, f.footprintBoxSize.y * 0.5f);

            float ax = Mathf.Abs(dx);
            float az = Mathf.Abs(dz);

            float ox = Mathf.Max(0f, ax - hx);
            float oz = Mathf.Max(0f, az - hz);

            float outsideDist = Mathf.Sqrt(ox * ox + oz * oz);

            if (outsideDist <= 0.0001f)
                return 1f;

            if (outsideDist <= falloff)
                return 1f - (outsideDist / falloff);

            return 0f;
        }
    }

    private static bool IsInsideFootprint(FeatureDefinition f, float dx, float dz)
    {
        if (f == null) return false;

        if (f.footprintShape == FeatureDefinition.FootprintShape.Circle)
        {
            float r = Mathf.Max(0f, f.footprintRadius);
            return (dx * dx + dz * dz) <= (r * r);
        }
        else
        {
            float hx = Mathf.Max(0f, f.footprintBoxSize.x * 0.5f);
            float hz = Mathf.Max(0f, f.footprintBoxSize.y * 0.5f);
            return Mathf.Abs(dx) <= hx && Mathf.Abs(dz) <= hz;
        }
    }

    private void ApplyFeatureClearanceAndFoundation(
        VoxelType[,,] voxels,
        int sizeX,
        int sizeY,
        int sizeZ,
        List<FeaturePlacement> placements,
        float voxelSize,
        float halfWidth,
        float halfLength,
        float worldBottomY)
    {
        if (voxels == null || placements == null || placements.Count == 0)
            return;

        // 1) Clearance volumes
        for (int i = 0; i < placements.Count; i++)
        {
            FeaturePlacement p = placements[i];
            FeatureDefinition f = p.feature;
            if (f == null || !f.applyClearanceVolume)
                continue;

            Vector3 center = p.localPosition + f.clearanceCenterOffset;
            Vector3 size = f.clearanceSize;

            float minWX = center.x - size.x * 0.5f;
            float maxWX = center.x + size.x * 0.5f;

            float minWY = center.y - size.y * 0.5f;
            float maxWY = center.y + size.y * 0.5f;

            float minWZ = center.z - size.z * 0.5f;
            float maxWZ = center.z + size.z * 0.5f;

            int minX = Mathf.Clamp(Mathf.FloorToInt((minWX + halfWidth) / voxelSize), 0, sizeX - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((maxWX + halfWidth) / voxelSize), 0, sizeX - 1);

            int minZ = Mathf.Clamp(Mathf.FloorToInt((minWZ + halfLength) / voxelSize), 0, sizeZ - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt((maxWZ + halfLength) / voxelSize), 0, sizeZ - 1);

            int minY = Mathf.Clamp(Mathf.FloorToInt((minWY - worldBottomY) / voxelSize), 0, sizeY - 1);
            int maxY = Mathf.Clamp(Mathf.FloorToInt((maxWY - worldBottomY) / voxelSize), 0, sizeY - 1);

            for (int z = minZ; z <= maxZ; z++)
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                        voxels[x, y, z] = VoxelType.Air;
        }

        // 2) Foundation reinforcement (below footprint)
        for (int i = 0; i < placements.Count; i++)
        {
            FeaturePlacement p = placements[i];
            FeatureDefinition f = p.feature;
            if (f == null || !f.applyFoundation || f.foundationDepthVoxels <= 0)
                continue;

            float hx = f.GetFootprintHalfWidth();
            float hz = f.GetFootprintHalfLength();

            float minWX = p.localPosition.x - hx;
            float maxWX = p.localPosition.x + hx;
            float minWZ = p.localPosition.z - hz;
            float maxWZ = p.localPosition.z + hz;

            int minX = Mathf.Clamp(Mathf.FloorToInt((minWX + halfWidth) / voxelSize), 0, sizeX - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((maxWX + halfWidth) / voxelSize), 0, sizeX - 1);

            int minZ = Mathf.Clamp(Mathf.FloorToInt((minWZ + halfLength) / voxelSize), 0, sizeZ - 1);
            int maxZ = Mathf.Clamp(Mathf.FloorToInt((maxWZ + halfLength) / voxelSize), 0, sizeZ - 1);

            int topY = Mathf.Clamp(Mathf.FloorToInt((p.targetSurfaceWorldY - worldBottomY) / voxelSize), 0, sizeY - 1);
            int depth = Mathf.Clamp(f.foundationDepthVoxels, 0, sizeY);

            for (int z = minZ; z <= maxZ; z++)
            {
                float cellLocalZ = (z * voxelSize) - halfLength;

                for (int x = minX; x <= maxX; x++)
                {
                    float cellLocalX = (x * voxelSize) - halfWidth;

                    float dx = cellLocalX - p.localPosition.x;
                    float dz = cellLocalZ - p.localPosition.z;

                    if (!IsInsideFootprint(f, dx, dz))
                        continue;

                    for (int y = topY; y >= Mathf.Max(0, topY - depth); y--)
                    {
                        if (voxels[x, y, z] == VoxelType.Air)
                        {
                            float worldY = worldBottomY + y * voxelSize;
                            voxels[x, y, z] = (worldY >= 0f) ? VoxelType.Ground : VoxelType.Underground;
                        }
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────
    // Feature content spawning (prefabs + modular caves)
    // ─────────────────────────────────────
    private void SpawnFeatureContent(Transform terrainParent, List<FeaturePlacement> placements, Rng rng)
    {
        if (terrainParent == null || placements == null || placements.Count == 0)
            return;

        Transform worldRoot = ResolveWorldRoot(terrainParent);
        if (worldRoot == null)
            worldRoot = terrainParent;

        Transform regionsRoot = GetOrCreateChild(worldRoot, "SpawnedRegions");
        Transform cavesRoot = GetOrCreateChild(regionsRoot, "CaveInteriors");

        int caveInstanceIndex = 0;

        for (int i = 0; i < placements.Count; i++)
        {
            FeaturePlacement p = placements[i];
            FeatureDefinition f = p.feature;
            if (f == null || !f.spawnContent)
                continue;

            if (f is CaveFeatureDefinition caveDef)
            {
                SpawnCaveFeatureContent(
                    terrainParent,
                    regionsRoot,
                    cavesRoot,
                    caveDef,
                    p,
                    (rng != null) ? rng.Split(70000 + caveInstanceIndex * 37) : null,
                    caveInstanceIndex
                );

                caveInstanceIndex++;
                continue;
            }

            if (f.contentType == FeatureDefinition.ContentType.Prefab && f.prefab != null)
            {
                Vector3 localPos = p.localPosition + f.contentOffset;
                Vector3 worldPos = terrainParent.TransformPoint(localPos);

                Quaternion yaw = Quaternion.Euler(0f, p.yawDegrees, 0f);
                Quaternion worldRot = terrainParent.rotation * yaw;

                GameObject go = Object.Instantiate(f.prefab, worldPos, worldRot, regionsRoot);
                go.name = $"{f.id}_{i}";
            }
            else if (f.contentType == FeatureDefinition.ContentType.Scene)
            {
                Debug.Log($"Feature '{f.id}' planned as Scene: '{f.sceneName}' (not loaded by runtime yet).");
            }
        }
    }

    private static Transform ResolveWorldRoot(Transform terrainParent)
    {
        if (terrainParent == null)
            return null;

        Transform t = terrainParent;
        Transform last = t;

        while (t != null)
        {
            last = t;
            if (t.name == "WorldRoot")
                return t;

            t = t.parent;
        }

        return last;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private void SpawnCaveFeatureContent(
        Transform terrainParent,
        Transform regionsRoot,
        Transform cavesRoot,
        CaveFeatureDefinition caveDef,
        FeaturePlacement placement,
        Rng rng,
        int instanceIndex)
    {
        if (terrainParent == null || cavesRoot == null || caveDef == null)
            return;

        // 1) Outside entrance
        Vector3 localPos = placement.localPosition + caveDef.entranceOffset;
        Vector3 entranceWorldPos = terrainParent.TransformPoint(localPos);

        Quaternion yaw = Quaternion.Euler(0f, placement.yawDegrees, 0f);
        Quaternion entranceWorldRot = terrainParent.rotation * yaw;

        if (caveDef.entrancePrefab == null)
        {
            Debug.LogWarning($"[VoxelTerrainModule] CaveFeatureDefinition '{caveDef.name}' has no entrancePrefab.");
            return;
        }

        Transform caveEntrancesRoot = GetOrCreateChild(regionsRoot, "CaveEntrances");

        GameObject entranceGO = Object.Instantiate(caveDef.entrancePrefab, entranceWorldPos, entranceWorldRot, caveEntrancesRoot);
        entranceGO.name = $"{caveDef.id}_Entrance_{instanceIndex}";

        // Outside return point (for inside exit to target)
        GameObject outsidePointGO = new GameObject("OutsideReturnPoint");
        outsidePointGO.transform.SetParent(entranceGO.transform, false);
        outsidePointGO.transform.position = entranceGO.transform.position;
        outsidePointGO.transform.rotation = entranceGO.transform.rotation;

        // 2) Interior origin
        float interiorSpacing = instanceIndex * Mathf.Max(0f, caveDef.interiorInstanceSpacing);
        Vector3 interiorOrigin;

        if (caveDef.interiorPlacementMode == CaveFeatureDefinition.InteriorPlacementMode.ManualOffset)
        {
            interiorOrigin = terrainParent.position + caveDef.interiorBaseOffset + Vector3.right * interiorSpacing;
        }
        else
        {
            float y = placement.targetSurfaceWorldY - Mathf.Max(0f, caveDef.interiorDepthBelowSurface);
            interiorOrigin = new Vector3(entranceWorldPos.x, y, entranceWorldPos.z);
            interiorOrigin += caveDef.interiorPlanarOffset;
            interiorOrigin += Vector3.right * interiorSpacing;
        }

        string caveName = string.IsNullOrEmpty(caveDef.id) ? "Cave" : caveDef.id;

        var genRes = CaveDungeonGenerator.Generate(
            caveDef,
            rng ?? new Rng(12345 + instanceIndex),
            interiorOrigin,
            cavesRoot,
            instanceName: $"{caveName}_Interior_{instanceIndex}"
        );

        Transform insideEntry = genRes.insideEntryPoint != null ? genRes.insideEntryPoint : genRes.root;

        // 3) Wire outside entrance teleport -> inside entry
        CaveEntranceTeleport outsideTele = entranceGO.GetComponentInChildren<CaveEntranceTeleport>(true);
        if (outsideTele == null)
            outsideTele = entranceGO.AddComponent<CaveEntranceTeleport>();

        outsideTele.teleportTarget = insideEntry;
        outsideTele.returnPoint = outsidePointGO.transform;

        // 4) Wire inside exit teleport -> outside return
        bool startFromExitPiece = caveDef.useExitPrefabAsStartPiece && caveDef.exitPrefab != null;

        if (startFromExitPiece)
        {
            Transform exitRoot = genRes.startPieceRoot != null ? genRes.startPieceRoot : genRes.root;
            if (exitRoot != null)
            {
                CaveEntranceTeleport insideTele = exitRoot.GetComponentInChildren<CaveEntranceTeleport>(true);
                if (insideTele == null)
                    insideTele = exitRoot.gameObject.AddComponent<CaveEntranceTeleport>();

                insideTele.teleportTarget = outsidePointGO.transform;
                insideTele.returnPoint = insideEntry;
            }
        }
        else
        {
            GameObject exitPrefab = caveDef.exitPrefab != null ? caveDef.exitPrefab : caveDef.entrancePrefab;
            if (exitPrefab != null && insideEntry != null)
            {
                Transform exitParent = genRes.root != null ? genRes.root : cavesRoot;
                GameObject exitGO = Object.Instantiate(exitPrefab, insideEntry.position, insideEntry.rotation, exitParent);
                exitGO.name = $"{caveDef.id}_Exit_{instanceIndex}";

                CaveEntranceTeleport insideTele = exitGO.GetComponentInChildren<CaveEntranceTeleport>(true);
                if (insideTele == null)
                    insideTele = exitGO.AddComponent<CaveEntranceTeleport>();

                insideTele.teleportTarget = outsidePointGO.transform;
                insideTele.returnPoint = insideEntry;
            }
        }

        // Optional: store links on instance
        if (genRes.instance != null)
        {
            genRes.instance.outsideReturnPoint = outsidePointGO.transform;
            genRes.instance.insideEntryPoint = insideEntry;
        }
    }
}
