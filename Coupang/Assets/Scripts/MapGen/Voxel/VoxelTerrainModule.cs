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
    [Tooltip("World Y of the TOP of the underground slab.\n" +
             "For example, -20 with thickness 40 -> slab from -60 to -20.")]
    public float undergroundBottomY = -20f;

    [Tooltip("Slab thickness downward in world units.\n" +
             "For example, thickness 40 -> slabBottom = undergroundBottomY - 40.")]
    public float undergroundThickness = 20f;

    [Header("Cave Safety")]
    [Tooltip("Safety margin (world units) below the top of the underground slab where NO noise caves are carved.\n" +
             "Only entrance tunnels may pass through this zone.")]
    public float caveSafeMarginFromTop = 8f;

    // ─────────────────────────────────────
    // Materials
    // ─────────────────────────────────────
    [Header("Materials")]
    [Tooltip("Default material for ground voxels (world Y >= 0).")]
    public Material defaultGroundMaterial;

    [Tooltip("Default material for underground voxels (world Y < 0).\n" +
             "Used for solid crust and boundary shell.")]
    public Material defaultUndergroundMaterial;

    [Tooltip("Material for deep cave voxels.\n" +
             "Underground voxels with worldY <= caveMaterialStartY will use this.")]
    public Material defaultCaveMaterial;

    [Header("Cave Material Depth Threshold")]
    [Tooltip("World Y threshold below which underground voxels will use the Cave material instead of Underground.\n" +
             "For example, -30 means all underground voxels with worldY <= -30 become Cave.")]
    public float caveMaterialStartY = -30f;

    // ─────────────────────────────────────
    // Noise caves (small-scale carving)
    // ─────────────────────────────────────
    [Header("Noise Caves")]
    [Tooltip("Enable cave generation using 3D pseudo-Perlin noise inside the underground slab.")]
    public bool enableCaves = true;

    [Tooltip("Base frequency for cave noise. Larger values → more frequent/smaller caves.")]
    public float caveNoiseScale = 0.05f;

    [Tooltip("Number of octaves for cave noise.")]
    public int caveOctaves = 3;

    [Tooltip("Frequency multiplier per octave for cave noise.")]
    public float caveLacunarity = 2f;

    [Tooltip("Amplitude multiplier per octave (0-1) for cave noise.")]
    [Range(0f, 1f)]
    public float cavePersistence = 0.5f;

    [Tooltip("Threshold in [0,1] controlling how open the noise caves are.\nHigher threshold → fewer/smaller caves.")]
    [Range(0f, 1f)]
    public float caveThreshold = 0.55f;

    [Tooltip("How strongly noise caves affect terrain density (world units).\nHigher value → deeper tunnels and more open caves.")]
    public float caveStrength = 8f;

    // ─────────────────────────────────────
    // Path caves (large tunnels + rooms)
    // ─────────────────────────────────────
    [Header("Path Caves (Large Tunnels)")]
    [Tooltip("If true, generates a few large cave paths carved through the underground slab.")]
    public bool enablePathCaves = true;

    [Tooltip("How many main cave paths to carve in total.")]
    public int pathCaveCount = 3;

    [Tooltip("Maximum number of steps for each path. Larger value → longer tunnels.")]
    public int pathMaxSteps = 260;

    [Tooltip("Step length in voxel units along the path. 1.0 means roughly one voxel per step.")]
    public float pathStepLength = 1.5f;

    [Tooltip("Base radius of the tunnel in voxels.")]
    public float pathBaseRadius = 2.5f;

    [Tooltip("Random variation added to the base radius (in voxels).")]
    public float pathRadiusJitter = 1.0f;

    [Tooltip("How strongly the direction is perturbed each step. Higher value → more winding tunnels.")]
    public float pathTurnRate = 0.25f;

    [Tooltip("Chance per step to create a larger room at this position.")]
    [Range(0f, 1f)]
    public float roomChance = 0.08f;

    [Header("Path Cave Rooms Shape")]
    [Tooltip("Average multiplier for room radius relative to the tunnel radius.")]
    public float roomRadiusMultiplier = 2.5f;

    [Tooltip("Randomness around the average room radius multiplier.\n0 = all rooms the same size, 0.5 = ±50% variation.")]
    public float roomRadiusRandomness = 0.5f;

    [Tooltip("Base frequency for noise used to make room shapes irregular (lumpy).")]
    public float roomShapeNoiseFrequency = 0.35f;

    [Tooltip("Noise influence on room shape. 0 = perfect sphere, 1 = very lumpy.")]
    [Range(0f, 1f)]
    public float roomShapeNoiseAmplitude = 0.4f;

    [Tooltip("Number of octaves for room shape noise.")]
    public int roomShapeNoiseOctaves = 2;

    // ─────────────────────────────────────
    // Entrances (caves ↔ surface)
    // ─────────────────────────────────────
    [Header("Wall Entrances (Caves to Surface)")]
    [Tooltip("Minimum number of cave entrances that connect caves to the surface.\n" +
             "Entrances start on cave walls, not ceilings.\n" +
             "※ Only between-room corridors will be used (not rooms themselves).")]
    public int minSurfaceEntrances = 2;

    [Tooltip("Radius of entrance tunnel (in voxels).")]
    public float entranceRadius = 2.0f;

    [Tooltip("Slope angle (degrees) of the entrance tunnel measured from horizontal.\n30 = gentle ramp the player can walk.")]
    [Range(5f, 80f)]
    public float entranceSlopeDegrees = 30f;

    [Tooltip("Step length (in voxels) along the entrance tunnel path.")]
    public float entranceStepLength = 1.2f;

    [Tooltip("How wiggly the entrance tunnel is. 0 = perfectly straight, 1 = very noisy.")]
    [Range(0f, 1f)]
    public float entranceJitter = 0.35f;

    [Tooltip("How much below the entrance starting floor the tunnel is allowed to carve (world units).\n0 = never digs below the starting floor height.")]
    public float entranceFloorToleranceDown = 0.8f;

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
    // Helper struct for wall entrance candidates
    // ─────────────────────────────────────
    private struct WallCandidate
    {
        public int x, y, z;
        public Vector3Int wallDir; // from cave cell towards solid wall
    }

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
        {
            Object.Destroy(parent.GetChild(i).gameObject);
        }

        int width = Mathf.Max(1, profile.mapWidth);
        int length = Mathf.Max(1, profile.mapLength);

        float voxelSize = voxelSizeOverride > 0f
            ? voxelSizeOverride
            : Mathf.Max(0.1f, profile.tileSize);

        // Surface height noise parameters
        float baseHeight = profile.baseHeight;
        float heightScale = profile.heightScale;
        float noiseScale = Mathf.Max(0.0001f, profile.noiseScale);
        int octaves = Mathf.Max(1, profile.noiseOctaves);
        float lacunarity = Mathf.Max(1f, profile.noiseLacunarity);
        float persistence = Mathf.Clamp01(profile.noisePersistence);

        // Landing area parameters
        float flatRadius = Mathf.Max(0f, profile.landingRadius);
        float blendWidth = Mathf.Max(0f, profile.landingFalloff);

        // Random offsets for surface noise
        float offsetX = rng.NextFloat(-1000f, 1000f);
        float offsetZ = rng.NextFloat(-1000f, 1000f);

        // Random offsets for noise caves
        float caveOffsetX = rng.NextFloat(-1000f, 1000f);
        float caveOffsetY = rng.NextFloat(-1000f, 1000f);
        float caveOffsetZ = rng.NextFloat(-1000f, 1000f);

        // Precompute surface height map from noise
        float[,] heightMap = new float[width, length];
        float maxSurfaceHeight = float.MinValue;

        float totalWidth = width * voxelSize;
        float totalLength = length * voxelSize;
        float halfWidth = totalWidth * 0.5f;
        float halfLength = totalLength * 0.5f;

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
                    float sampleX = (localX * frequency) + offsetX;
                    float sampleZ = (localZ * frequency) + offsetZ;
                    float n = Mathf.PerlinNoise(sampleX, sampleZ);
                    value += n * amplitude;
                    maxValue += amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (maxValue > 0f)
                    value /= maxValue; // 0..1

                float noiseHeight = baseHeight + (value - 0.5f) * 2f * heightScale;

                // Landing zone flattening
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
                }

                heightMap[x, z] = finalHeight;
                if (finalHeight > maxSurfaceHeight)
                    maxSurfaceHeight = finalHeight;
            }
        }

        // ─────────────────────────────────────
        // Underground slab range (downward)
        // ─────────────────────────────────────
        float thickness = Mathf.Max(0f, undergroundThickness);
        float slabTopY = undergroundBottomY;              // e.g. -20 (top of underground slab)
        float slabBottomY = undergroundBottomY - thickness;

        // "No caves zone" immediately below slab top
        float safeMargin = Mathf.Max(0f, caveSafeMarginFromTop);
        float caveSafeMaxY = slabTopY - safeMargin;      // caves will not go above this Y

        if (caveSafeMaxY < slabBottomY)
        {
            // If margin is too big, collapse it to full slab
            caveSafeMaxY = slabBottomY;
        }

        // Chunk bottom at slab bottom
        float worldBottomY = slabBottomY;

        // Create chunk GameObject
        GameObject chunkGO = new GameObject("VoxelChunk_Main");
        chunkGO.transform.SetParent(parent, false);
        chunkGO.transform.localPosition = new Vector3(-halfWidth, worldBottomY, -halfLength);
        chunkGO.transform.localRotation = Quaternion.identity;

        VoxelChunk chunk = chunkGO.AddComponent<VoxelChunk>();

        int verticalVoxels = Mathf.Max(1, voxelHeight);
        GetTerrainMaterials(profile, out Material groundMat, out Material undergroundMat, out Material caveMat);
        chunk.Initialize(width, verticalVoxels, length, groundMat, undergroundMat, caveMat);

        // Masks for path cave classification
        // corridorMask = main tunnels
        // roomMask     = larger chambers
        bool[,,] corridorMask = null;
        bool[,,] roomMask = null;

        if (enablePathCaves)
        {
            corridorMask = new bool[width, verticalVoxels, length];
            roomMask = new bool[width, verticalVoxels, length];
        }

        // Fill voxels (slab + surface)
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float surfaceHeight = heightMap[x, z];

                float localX = (x * voxelSize) - halfWidth;
                float localZ = (z * voxelSize) - halfLength;

                for (int y = 0; y < verticalVoxels; y++)
                {
                    float worldY = worldBottomY + y * voxelSize;

                    // Base density:
                    //  - worldY < slabBottomY: empty
                    //  - slabBottomY <= worldY <= slabTopY: solid underground slab
                    //  - slabTopY < worldY <= surfaceHeight: solid ground
                    //  - worldY  > surfaceHeight: empty
                    float baseDensity;

                    if (worldY < slabBottomY)
                    {
                        baseDensity = -1f;
                    }
                    else if (worldY <= slabTopY)
                    {
                        baseDensity = 1f;  // underground slab
                    }
                    else if (worldY <= surfaceHeight)
                    {
                        baseDensity = 1f;  // ground above slab
                    }
                    else
                    {
                        baseDensity = -1f; // air above surface
                    }

                    float finalDensity = baseDensity;

                    // Noise caves only inside underground slab,
                    // and NOT in the "safe zone" near the top (worldY > caveSafeMaxY).
                    if (enableCaves &&
                        worldY >= slabBottomY &&
                        worldY <= caveSafeMaxY &&
                        baseDensity > 0f)
                    {
                        float nx = localX + caveOffsetX;
                        float ny = worldY + caveOffsetY;
                        float nz = localZ + caveOffsetZ;

                        float caveValue = NoiseUtils.FractalPseudoPerlin3D(
                            nx, ny, nz,
                            Mathf.Max(1, caveOctaves),
                            Mathf.Max(0.0001f, caveNoiseScale),
                            Mathf.Max(1f, caveLacunarity),
                            Mathf.Clamp01(cavePersistence));

                        float delta = (caveValue - caveThreshold) * caveStrength;
                        finalDensity -= delta;
                    }

                    if (finalDensity > 0f)
                    {
                        if (worldY >= 0f)
                            chunk.voxels[x, y, z] = VoxelType.Ground;
                        else
                            chunk.voxels[x, y, z] = VoxelType.Underground;
                    }
                    else
                    {
                        chunk.voxels[x, y, z] = VoxelType.Air;
                    }
                }
            }
        }

        // ─────────────────────────────────────
        // Path-based caves: long tunnels + rooms
        // ─────────────────────────────────────
        if (enablePathCaves)
        {
            Rng pathRng = rng.Split(123456);

            CarvePathCaves(
                chunk.voxels,
                corridorMask,
                roomMask,
                width,
                verticalVoxels,
                length,
                voxelSize,
                pathRng,
                slabBottomY,
                slabTopY,
                worldBottomY,
                caveSafeMaxY  // caves cannot go above this (buffer under surface)
            );
        }

        // ─────────────────────────────────────
        // Boundary shell (underground only, noisy thickness)
        // ─────────────────────────────────────
        if (enableBoundaryShell)
        {
            Rng shellRng = rng.Split(98765);
            SealOuterShell(
                chunk.voxels,
                width,
                verticalVoxels,
                length,
                worldBottomY,
                voxelSize,
                shellRng
            );
        }

        // ─────────────────────────────────────
        // Mark deep underground as Cave (depth threshold)
        // ─────────────────────────────────────
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

        // ─────────────────────────────────────
        // Create entrances only from corridor walls (not rooms) to surface
        // ─────────────────────────────────────
        if (minSurfaceEntrances > 0 && enablePathCaves && corridorMask != null && roomMask != null)
        {
            Rng entranceRng = rng.Split(424242);

            CreateWallEntrances(
                chunk.voxels,
                corridorMask,
                roomMask,
                width,
                verticalVoxels,
                length,
                voxelSize,
                worldBottomY,
                heightMap,
                maxSurfaceHeight,
                caveSafeMaxY,
                entranceRng,
                minSurfaceEntrances
            );
        }

        // ─────────────────────────────────────
        // Build final mesh
        // ─────────────────────────────────────
        chunk.BuildMesh(voxelSize);
    }

    // ─────────────────────────────────────
    // Material helpers
    // ─────────────────────────────────────
    private void GetTerrainMaterials(
        MapProfile profile,
        out Material groundMat,
        out Material undergroundMat,
        out Material caveMat)
    {
        if (profile != null && profile.groundMaterialOverride != null)
            groundMat = profile.groundMaterialOverride;
        else
            groundMat = defaultGroundMaterial;

        undergroundMat = defaultUndergroundMaterial != null
            ? defaultUndergroundMaterial
            : groundMat;

        caveMat = defaultCaveMaterial != null
            ? defaultCaveMaterial
            : undergroundMat;
    }

    public override Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        if (parent == null)
            return Vector3.zero;

        // Rough center; LandingHelper will raycast down from here.
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
                    {
                        voxels[x, y, z] = VoxelType.Cave;
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────
    // Path cave carving (internal networks)
    // ─────────────────────────────────────
    private void CarvePathCaves(
        VoxelType[,,] voxels,
        bool[,,] corridorMask,
        bool[,,] roomMask,
        int sizeX,
        int sizeY,
        int sizeZ,
        float voxelSize,
        Rng rng,
        float slabBottomY,
        float slabTopY,
        float worldBottomY,
        float maxCarveWorldY)
    {
        if (sizeX <= 1 || sizeY <= 1 || sizeZ <= 1)
            return;

        if (corridorMask == null || roomMask == null)
            return;

        int totalPaths = Mathf.Max(0, pathCaveCount);
        if (totalPaths == 0)
            return;

        int maxSteps = Mathf.Max(1, pathMaxSteps);
        float stepLen = Mathf.Max(0.1f, pathStepLength);
        float baseRadius = Mathf.Max(0.5f, pathBaseRadius);
        float radiusJitter = Mathf.Max(0f, pathRadiusJitter);
        float turn = Mathf.Clamp01(pathTurnRate);

        float roomRadiusRand = Mathf.Max(0f, roomRadiusRandomness);
        float avgRoomMul = Mathf.Max(0.1f, roomRadiusMultiplier);

        int slabMinY = Mathf.Clamp(
            Mathf.FloorToInt((slabBottomY - worldBottomY) / voxelSize),
            0, sizeY - 1);
        int slabMaxY = Mathf.Clamp(
            Mathf.CeilToInt((slabTopY - worldBottomY) / voxelSize),
            0, sizeY - 1);

        if (slabMaxY <= slabMinY)
            return;

        int verticalMargin = Mathf.Max(1, (slabMaxY - slabMinY) / 10);
        int minYIndex = Mathf.Clamp(slabMinY + verticalMargin, 0, sizeY - 1);
        int maxYIndex = Mathf.Clamp(slabMaxY - verticalMargin, 0, sizeY - 1);

        if (maxYIndex <= minYIndex)
            return;

        for (int p = 0; p < totalPaths; p++)
        {
            float startX = rng.NextInt(2, sizeX - 2) + 0.5f;
            float startZ = rng.NextInt(2, sizeZ - 2) + 0.5f;
            int startYInt = rng.NextInt(minYIndex, maxYIndex + 1);
            float startY = startYInt + 0.5f;

            Vector3 startPos = new Vector3(startX, startY, startZ);

            Rng pathRng = rng.Split(1000 + p);
            CarveSinglePath(
                voxels,
                corridorMask,
                roomMask,
                sizeX,
                sizeY,
                sizeZ,
                startPos,
                maxSteps,
                stepLen,
                baseRadius,
                radiusJitter,
                turn,
                minYIndex,
                maxYIndex,
                pathRng,
                roomChance,
                avgRoomMul,
                roomRadiusRand,
                worldBottomY,
                voxelSize,
                maxCarveWorldY
            );
        }
    }

    private void CarveSinglePath(
        VoxelType[,,] voxels,
        bool[,,] corridorMask,
        bool[,,] roomMask,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3 startPos,
        int maxSteps,
        float stepLen,
        float baseRadius,
        float radiusJitter,
        float turn,
        int minYIndex,
        int maxYIndex,
        Rng rng,
        float roomChance,
        float avgRoomMul,
        float roomRadiusRand,
        float worldBottomY,
        float voxelSize,
        float maxCarveWorldY)
    {
        Vector3 pos = startPos;

        Vector3 dir = new Vector3(
            rng.NextFloat(-1f, 1f),
            rng.NextFloat(-0.3f, 0.3f),
            rng.NextFloat(-1f, 1f)
        ).normalized;

        for (int step = 0; step < maxSteps; step++)
        {
            float r = baseRadius + rng.NextFloat(-radiusJitter, radiusJitter);
            r = Mathf.Max(0.5f, r);

            // Main tunnel (corridor)
            CarveSphere(
                voxels,
                corridorMask,
                roomMask,
                sizeX,
                sizeY,
                sizeZ,
                pos,
                r,
                worldBottomY,
                voxelSize,
                true,
                maxCarveWorldY
            );

            // Occasionally create a bigger room
            if (rng.NextFloat() < roomChance)
            {
                float mulFactor = 1f + rng.NextFloat(-roomRadiusRand, roomRadiusRand);
                float roomMul = avgRoomMul * Mathf.Max(0.1f, mulFactor);
                float roomRadius = r * roomMul;

                Rng roomRng = rng.Split(step + 1);
                CarveIrregularRoom(
                    voxels,
                    corridorMask,
                    roomMask,
                    sizeX,
                    sizeY,
                    sizeZ,
                    pos,
                    roomRadius,
                    roomRng,
                    worldBottomY,
                    voxelSize,
                    maxCarveWorldY
                );
            }

            // Update direction
            Vector3 delta = new Vector3(
                rng.NextFloat(-1f, 1f),
                rng.NextFloat(-0.4f, 0.4f),
                rng.NextFloat(-1f, 1f)
            ) * turn;

            dir = (dir + delta).normalized;

            // Step forward
            pos += dir * stepLen;

            // XZ bounds bounce
            BounceAtHorizontalBounds(sizeX, sizeZ, ref pos, ref dir);

            // Y bounds bounce within slab
            if (pos.y < minYIndex + 0.5f || pos.y > maxYIndex + 0.5f)
            {
                dir.y = -dir.y;
                pos.y = Mathf.Clamp(pos.y, minYIndex + 0.5f, maxYIndex + 0.5f);
            }
        }
    }

    private void BounceAtHorizontalBounds(
        int sizeX,
        int sizeZ,
        ref Vector3 pos,
        ref Vector3 dir)
    {
        float minX = 1f;
        float maxX = sizeX - 2f;
        float minZ = 1f;
        float maxZ = sizeZ - 2f;

        bool bounced = false;

        if (pos.x < minX)
        {
            pos.x = minX;
            dir.x = Mathf.Abs(dir.x);
            bounced = true;
        }
        else if (pos.x > maxX)
        {
            pos.x = maxX;
            dir.x = -Mathf.Abs(dir.x);
            bounced = true;
        }

        if (pos.z < minZ)
        {
            pos.z = minZ;
            dir.z = Mathf.Abs(dir.z);
            bounced = true;
        }
        else if (pos.z > maxZ)
        {
            pos.z = maxZ;
            dir.z = -Mathf.Abs(dir.z);
            bounced = true;
        }

        if (bounced)
        {
            dir = dir.normalized;
        }
    }

    // ─────────────────────────────────────
    // Basic sphere carving (for path tunnels)
    // ─────────────────────────────────────
    private void CarveSphere(
        VoxelType[,,] voxels,
        bool[,,] corridorMask,
        bool[,,] roomMask,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3 center,
        float radius,
        float worldBottomY,
        float voxelSize,
        bool clampToMaxWorldY,
        float maxWorldY)
    {
        float r2 = radius * radius;

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int maxX = Mathf.Min(sizeX - 1, Mathf.CeilToInt(center.x + radius));

        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int maxY = Mathf.Min(sizeY - 1, Mathf.CeilToInt(center.y + radius));

        int minZ = Mathf.Max(0, Mathf.FloorToInt(center.z - radius));
        int maxZ = Mathf.Min(sizeZ - 1, Mathf.CeilToInt(center.z + radius));

        for (int z = minZ; z <= maxZ; z++)
        {
            float dz = (z + 0.5f) - center.z;
            float dz2 = dz * dz;

            for (int y = minY; y <= maxY; y++)
            {
                if (clampToMaxWorldY)
                {
                    float worldYCenter = worldBottomY + (y + 0.5f) * voxelSize;
                    if (worldYCenter > maxWorldY)
                        continue;
                }

                float dy = (y + 0.5f) - center.y;
                float dy2 = dy * dy;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - center.x;
                    float dx2 = dx * dx;

                    if (dx2 + dy2 + dz2 <= r2)
                    {
                        voxels[x, y, z] = VoxelType.Air;

                        // Mark as corridor unless later overridden by a room
                        if (corridorMask != null)
                            corridorMask[x, y, z] = true;

                        // Do not touch roomMask here
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sphere carving with optional min/max world-Y clamps.
    /// (Used for entrance tunnels; does not mark corridor/room masks.)
    /// </summary>
    private void CarveSphereWithVerticalClamp(
        VoxelType[,,] voxels,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3 center,
        float radius,
        float worldBottomY,
        float voxelSize,
        bool clampMinWorldY,
        float minWorldY,
        bool clampMaxWorldY,
        float maxWorldY)
    {
        float r2 = radius * radius;

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int maxX = Mathf.Min(sizeX - 1, Mathf.CeilToInt(center.x + radius));

        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int maxY = Mathf.Min(sizeY - 1, Mathf.CeilToInt(center.y + radius));

        int minZ = Mathf.Max(0, Mathf.FloorToInt(center.z - radius));
        int maxZ = Mathf.Min(sizeZ - 1, Mathf.CeilToInt(center.z + radius));

        for (int z = minZ; z <= maxZ; z++)
        {
            float dz = (z + 0.5f) - center.z;
            float dz2 = dz * dz;

            for (int y = minY; y <= maxY; y++)
            {
                float worldYCenter = worldBottomY + (y + 0.5f) * voxelSize;
                if (clampMinWorldY && worldYCenter < minWorldY)
                    continue;
                if (clampMaxWorldY && worldYCenter > maxWorldY)
                    continue;

                float dy = (y + 0.5f) - center.y;
                float dy2 = dy * dy;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - center.x;
                    float dx2 = dx * dx;

                    if (dx2 + dy2 + dz2 <= r2)
                    {
                        voxels[x, y, z] = VoxelType.Air;
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────
    // Irregular room carving (lumpy sphere) for path caves
    // ─────────────────────────────────────
    private void CarveIrregularRoom(
        VoxelType[,,] voxels,
        bool[,,] corridorMask,
        bool[,,] roomMask,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3 center,
        float baseRadius,
        Rng rng,
        float worldBottomY,
        float voxelSize,
        float maxWorldY)
    {
        float shapeAmp = Mathf.Clamp01(roomShapeNoiseAmplitude);
        float searchRadius = baseRadius * (1f + shapeAmp);

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - searchRadius));
        int maxX = Mathf.Min(sizeX - 1, Mathf.CeilToInt(center.x + searchRadius));

        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - searchRadius));
        int maxY = Mathf.Min(sizeY - 1, Mathf.CeilToInt(center.y + searchRadius));

        int minZ = Mathf.Max(0, Mathf.FloorToInt(center.z - searchRadius));
        int maxZ = Mathf.Min(sizeZ - 1, Mathf.CeilToInt(center.z + searchRadius));

        float offX = rng.NextFloat(-1000f, 1000f);
        float offY = rng.NextFloat(-1000f, 1000f);
        float offZ = rng.NextFloat(-1000f, 1000f);

        int oct = Mathf.Max(1, roomShapeNoiseOctaves);
        float freq = Mathf.Max(0.0001f, roomShapeNoiseFrequency);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                float worldYCenter = worldBottomY + (y + 0.5f) * voxelSize;
                if (worldYCenter > maxWorldY)
                    continue;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - center.x;
                    float dy = (y + 0.5f) - center.y;
                    float dz = (z + 0.5f) - center.z;

                    float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    float normalized = dist / Mathf.Max(0.0001f, baseRadius);

                    float nx = (x + offX) * freq;
                    float ny = (y + offY) * freq;
                    float nz = (z + offZ) * freq;

                    float n = NoiseUtils.FractalPseudoPerlin3D(
                        nx, ny, nz,
                        oct,
                        1f,
                        2f,
                        0.5f
                    );

                    float signedNoise = (n - 0.5f) * 2f;
                    float threshold = 1f + shapeAmp * signedNoise;

                    if (normalized <= threshold)
                    {
                        voxels[x, y, z] = VoxelType.Air;

                        // Mark this region as room and clear corridor flag
                        if (roomMask != null)
                            roomMask[x, y, z] = true;

                        if (corridorMask != null)
                            corridorMask[x, y, z] = false;
                    }
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

        int bottomY = 0;

        // Bottom: always solid Underground
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                voxels[x, bottomY, z] = VoxelType.Underground;
            }
        }

        float offA = rng.NextFloat(-1000f, 1000f);
        float offB = rng.NextFloat(-1000f, 1000f);
        float offC = rng.NextFloat(-1000f, 1000f);
        float offD = rng.NextFloat(-1000f, 1000f);

        int maxThick = Mathf.Max(1, maxBoundaryThickness);
        int maxAllowedThickX = Mathf.Max(1, sizeX / 4);
        int maxAllowedThickZ = Mathf.Max(1, sizeZ / 4);

        float ns = Mathf.Max(0.0001f, boundaryNoiseScale);

        // Shell only below undergroundBottomY (no walls in surface region)
        float shellStartWorldY = undergroundBottomY;

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
                {
                    voxels[x, y, z] = VoxelType.Underground;
                }

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
                {
                    int z = i;
                    voxels[x, y, z] = VoxelType.Underground;
                }

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
    // Create entrances (cave walls → surface) - only near corridors, not rooms
    // ─────────────────────────────────────
    private void CreateWallEntrances(
        VoxelType[,,] voxels,
        bool[,,] corridorMask,
        bool[,,] roomMask,
        int sizeX,
        int sizeY,
        int sizeZ,
        float voxelSize,
        float worldBottomY,
        float[,] heightMap,
        float maxSurfaceY,
        float maxCaveWorldY,
        Rng rng,
        int desiredEntrances)
    {
        if (desiredEntrances <= 0)
            return;
        if (corridorMask == null || roomMask == null)
            return;

        List<WallCandidate> candidates = new List<WallCandidate>();

        float minWorldYForAnchor = worldBottomY + 2f * voxelSize;
        float maxWorldYForAnchor = maxCaveWorldY - 1f * voxelSize;

        // Find wall candidates where:
        //  - current cell is Air
        //  - below is solid (floor)
        //  - above is Air (headroom)
        //  - one side is solid (wall), opposite side is Air (cave interior)
        //  - the cave interior cell is:
        //        corridorMask == true AND roomMask == false
        //    → only corridors, not rooms
        for (int z = 2; z < sizeZ - 2; z++)
        {
            for (int x = 2; x < sizeX - 2; x++)
            {
                for (int y = 1; y < sizeY - 3; y++)
                {
                    if (voxels[x, y, z] != VoxelType.Air)
                        continue;

                    float worldY = worldBottomY + (y + 0.5f) * voxelSize;
                    if (worldY < minWorldYForAnchor || worldY > maxWorldYForAnchor)
                        continue;

                    // Depth to surface: too shallow → would look like a short vertical shaft
                    float surfaceHere = heightMap[x, z];
                    float depthToSurface = surfaceHere - worldY;
                    float minDepthWorld = Mathf.Max(entranceRadius * 2.5f * voxelSize, 2f * voxelSize);
                    if (depthToSurface < minDepthWorld)
                        continue;

                    // Floor: below must be solid
                    if (voxels[x, y - 1, z] == VoxelType.Air)
                        continue;

                    // Headroom: above must be Air (exclude ceiling-tight spaces)
                    if (voxels[x, y + 1, z] != VoxelType.Air)
                        continue;

                    Vector3Int[] dirs =
                    {
                        new Vector3Int( 1, 0, 0),
                        new Vector3Int(-1, 0, 0),
                        new Vector3Int( 0, 0, 1),
                        new Vector3Int( 0, 0,-1)
                    };

                    Vector3Int bestDir = Vector3Int.zero;

                    for (int i = 0; i < 4; i++)
                    {
                        Vector3Int d = dirs[i];
                        int nx = x + d.x;
                        int nz = z + d.z;

                        // This side should be solid (the wall)
                        VoxelType neighbor = voxels[nx, y, nz];
                        if (neighbor == VoxelType.Air)
                            continue;

                        // Opposite side: cave interior (Air) that MUST be corridor, not room
                        int ixInterior = x - d.x;
                        int izInterior = z - d.z;
                        VoxelType opp = voxels[ixInterior, y, izInterior];
                        if (opp != VoxelType.Air)
                            continue;

                        if (!corridorMask[ixInterior, y, izInterior])
                            continue;

                        if (roomMask[ixInterior, y, izInterior])
                            continue;

                        bestDir = d;
                        break;
                    }

                    if (bestDir == Vector3Int.zero)
                        continue;

                    WallCandidate c;
                    c.x = x;
                    c.y = y;
                    c.z = z;
                    c.wallDir = bestDir;

                    candidates.Add(c);
                }
            }
        }

        if (candidates.Count == 0)
            return;

        int made = 0;
        int safety = desiredEntrances * 4;

        while (made < desiredEntrances && safety-- > 0 && candidates.Count > 0)
        {
            int idx = rng.NextInt(0, candidates.Count);
            WallCandidate c = candidates[idx];

            // remove from list (swap-back)
            candidates[idx] = candidates[candidates.Count - 1];
            candidates.RemoveAt(candidates.Count - 1);

            if (CarveEntranceFromWall(
                    voxels,
                    sizeX,
                    sizeY,
                    sizeZ,
                    voxelSize,
                    worldBottomY,
                    heightMap,
                    maxSurfaceY,
                    maxCaveWorldY,
                    rng,
                    c))
            {
                made++;
            }
        }
    }

    /// <summary>
    /// Carves a diagonal ramp (~entranceSlopeDegrees) from a cave wall up toward the surface.
    /// Entrances are now only created from corridor walls (not room walls).
    /// No stair logic here; player climbability is handled by slope & voxel size.
    /// </summary>
    private bool CarveEntranceFromWall(
        VoxelType[,,] voxels,
        int sizeX,
        int sizeY,
        int sizeZ,
        float voxelSize,
        float worldBottomY,
        float[,] heightMap,
        float maxSurfaceY,
        float maxCaveWorldY,
        Rng rng,
        WallCandidate candidate)
    {
        float slopeDeg = Mathf.Clamp(entranceSlopeDegrees, 5f, 80f);
        float slopeRad = slopeDeg * Mathf.Deg2Rad;
        float tanSlope = Mathf.Tan(slopeRad);

        float stepLen = Mathf.Max(0.3f, entranceStepLength);
        float radius = Mathf.Max(1f, entranceRadius);
        float jitterAmp = radius * entranceJitter;

        // Starting position (center of anchor cell)
        Vector3 startPos = new Vector3(candidate.x + 0.5f, candidate.y + 0.5f, candidate.z + 0.5f);
        float startWorldY = worldBottomY + startPos.y * voxelSize;
        float minWorldY = startWorldY - Mathf.Abs(entranceFloorToleranceDown);

        // Horizontal direction: from cave interior toward the solid wall
        Vector3 dirXZ = new Vector3(candidate.wallDir.x, 0f, candidate.wallDir.z);
        if (dirXZ.sqrMagnitude < 1e-4f)
            dirXZ = new Vector3(1f, 0f, 0f);
        dirXZ.Normalize();

        // Upward slope
        Vector3 baseDir = new Vector3(dirXZ.x, tanSlope, dirXZ.z);
        Vector3 dir = baseDir.normalized;

        // For jitter around the path
        Vector3 side1 = Vector3.Cross(dir, Vector3.up);
        if (side1.sqrMagnitude < 1e-4f)
            side1 = Vector3.Cross(dir, Vector3.right);
        side1.Normalize();
        Vector3 side2 = Vector3.Cross(dir, side1).normalized;

        Vector3 pos = startPos;

        // Estimate needed steps
        float verticalRange = (maxSurfaceY - startWorldY) + 8f;
        float verticalPerStep = dir.y * stepLen * voxelSize;
        if (verticalPerStep <= 0.0001f)
            verticalPerStep = 0.25f;

        int maxSteps = Mathf.Clamp(Mathf.CeilToInt(verticalRange / verticalPerStep) + 8, 12, 400);

        for (int step = 0; step < maxSteps; step++)
        {
            float worldYCenter = worldBottomY + pos.y * voxelSize;
            if (worldYCenter > maxSurfaceY + 4f)
                break;

            if (pos.x < 1f || pos.x > sizeX - 2f ||
                pos.z < 1f || pos.z > sizeZ - 2f)
                break;

            int ix = Mathf.Clamp(Mathf.RoundToInt(pos.x), 0, sizeX - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt(pos.z), 0, sizeZ - 1);
            float surfaceY = heightMap[ix, iz];

            float localMaxWorldY = Mathf.Max(maxCaveWorldY + 0.5f * voxelSize, surfaceY + voxelSize);

            Vector3 carveCenter = pos;

            // Jitter: stronger deeper down, slightly reduced near the surface
            float heightT = Mathf.InverseLerp(startWorldY, surfaceY, worldYCenter);
            float currentJitterAmp = Mathf.Lerp(jitterAmp, jitterAmp * 0.6f, Mathf.Clamp01(heightT));

            if (currentJitterAmp > 0f)
            {
                float j1 = rng.NextFloat(-1f, 1f);
                float j2 = rng.NextFloat(-1f, 1f);
                Vector3 jitter = (side1 * j1 + side2 * j2) * currentJitterAmp;
                carveCenter += jitter;
            }

            CarveSphereWithVerticalClamp(
                voxels,
                sizeX,
                sizeY,
                sizeZ,
                carveCenter,
                radius,
                worldBottomY,
                voxelSize,
                true,
                minWorldY,
                true,
                localMaxWorldY
            );

            // If we reached near the surface, finalize
            if (worldYCenter >= surfaceY - voxelSize * 0.5f)
            {
                // Extra shaping near the mouth; clamp to just above the surface
                float mouthMaxWorldY = surfaceY + 0.25f * voxelSize;

                for (int extra = 0; extra < 3; extra++)
                {
                    pos += dir * stepLen;
                    worldYCenter = worldBottomY + pos.y * voxelSize;

                    carveCenter = pos;

                    // Decrease jitter near the entrance opening
                    float tExtra = (extra + 1) / 3f;
                    float extraJitter = Mathf.Lerp(currentJitterAmp, currentJitterAmp * 0.3f, tExtra);

                    if (extraJitter > 0f)
                    {
                        float j1 = rng.NextFloat(-1f, 1f);
                        float j2 = rng.NextFloat(-1f, 1f);
                        Vector3 jitter = (side1 * j1 + side2 * j2) * extraJitter;
                        carveCenter += jitter;
                    }

                    CarveSphereWithVerticalClamp(
                        voxels,
                        sizeX,
                        sizeY,
                        sizeZ,
                        carveCenter,
                        radius,
                        worldBottomY,
                        voxelSize,
                        true,
                        minWorldY,
                        true,
                        mouthMaxWorldY
                    );
                }

                return true;
            }

            // Slight direction noise
            Vector3 noiseDir = new Vector3(
                rng.NextFloat(-1f, 1f),
                rng.NextFloat(-0.2f, 0.4f),
                rng.NextFloat(-1f, 1f)
            ) * entranceJitter * 0.35f;
            dir = (dir + noiseDir).normalized;

            // Advance
            pos += dir * stepLen;

            // Do not let ramp dig below starting floor too much
            if (worldBottomY + pos.y * voxelSize < minWorldY - voxelSize)
            {
                pos.y = (minWorldY - worldBottomY) / voxelSize + 1f;
            }
        }

        return false;
    }
}
