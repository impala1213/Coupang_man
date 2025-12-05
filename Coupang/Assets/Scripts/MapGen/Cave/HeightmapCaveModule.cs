using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Map/Cave/Heightmap Cave")]
public class HeightmapCaveModule : CaveModule
{
    [Header("Grid")]
    [Tooltip("If true, use profile.mapWidth/mapLength/tileSize. Otherwise use custom values below.")]
    public bool useProfileGrid = true;

    public int gridWidth = 64;
    public int gridLength = 64;
    public float cellSize = 4f;

    [Header("Floor Height Noise")]
    public float floorBaseHeight = -12f;
    public float floorHeightScale = 6f;
    public float floorNoiseScale = 0.02f;
    public int floorOctaves = 4;
    public float floorLacunarity = 2f;
    public float floorPersistence = 0.45f;

    [Header("Floor Domain Warp")]
    [Tooltip("If true, additional noise is used to warp the floor pattern, making it less grid-like.")]
    public bool useDomainWarp = true;
    public float warpNoiseScale = 0.03f;
    public float warpStrength = 12f;

    [Header("Edge Walls (outer border)")]
    [Tooltip("If true, raise the floor near the outer border to create natural walls.")]
    public bool useEdgeWalls = true;
    public float edgeWallWidth = 15f;
    public float edgeWallHeight = 8f;
    [Tooltip("1 = linear, >1 = sharper near border.")]
    public float edgeWallPower = 2f;

    [Tooltip("Cavity height (floor -> ceiling) at the outermost edge.")]
    public float edgeMinCavityHeight = 0.2f;

    [Header("Rooms (cave chambers)")]
    public bool useRooms = true;
    public int minRoomsPerLayer = 2;
    public int maxRoomsPerLayer = 5;

    [Tooltip("Inner radius of rooms (open area).")]
    public float roomMinRadius = 18f;
    public float roomMaxRadius = 40f;

    [Tooltip("Ring width around inner radius that becomes wall.")]
    public float roomRingWidthMin = 8f;
    public float roomRingWidthMax = 18f;

    [Tooltip("Base raise amount for room walls.")]
    public float roomWallRaiseMin = 4f;
    public float roomWallRaiseMax = 9f;

    [Tooltip("Entrance angular size in degrees (gap in the ring wall).")]
    public float roomEntranceAngleMin = 50f;
    public float roomEntranceAngleMax = 90f;

    [Header("Room Shape / Rockiness")]
    [Tooltip("Angular noise for room outline (avoids perfect circles).")]
    public float roomRadiusNoiseScaleMin = 1.5f;
    public float roomRadiusNoiseScaleMax = 3.5f;
    public float roomRadiusNoiseStrengthMin = 0.2f;
    public float roomRadiusNoiseStrengthMax = 0.45f;

    [Tooltip("2D noise for wall rockiness (inner+outer both).")]
    public float roomInteriorNoiseScaleMin = 0.2f;
    public float roomInteriorNoiseScaleMax = 0.4f;
    public float roomInteriorNoiseStrengthMin = 2.0f;
    public float roomInteriorNoiseStrengthMax = 4.0f;

    [Range(0f, 1f)]
    [Tooltip("0 = 벽 두께가 방 중심까지, 1 = innerRadius 근처만 얇게 벽 생성")]
    public float roomInteriorInnerFalloff = 0.4f;

    [Header("Room Wall Lateral Displacement (XZ)")]
    [Tooltip("Perlin noise scale used to laterally displace room walls in XZ.")]
    public float roomWallDisplaceScale = 0.15f;

    [Tooltip("Maximum lateral displacement (in world units) for room walls.")]
    public float roomWallDisplaceAmount = 2.0f;

    [Header("Ceiling")]
    [Tooltip("Base vertical distance between floor and ceiling.")]
    public float baseThickness = 6f;
    [Tooltip("Additional thickness variation from noise.")]
    public float thicknessNoiseAmount = 2f;
    public float thicknessNoiseScale = 0.03f;

    [Header("Layers")]
    public int caveLevels = 1;
    public float caveLevelSpacing = 10f;

    [Header("Entrance Hint")]
    [Tooltip("Vertical offset above floor for the entrance hint position.")]
    public float entranceOffsetY = 1.5f;

    [Header("Rendering")]
    public Material caveMaterial;

    // ---------------------------------------------------------------------
    // Room definition
    // ---------------------------------------------------------------------

    private struct Room
    {
        public Vector2 centerLocal;
        public float innerRadius;
        public float outerRadius;
        public float wallRaise;

        public float entranceAngleDeg;
        public float entranceHalfAngleDeg;

        // Angular outline noise
        public float radiusNoiseScale;
        public float radiusNoiseStrength;
        public float radiusNoiseOffset;

        // Wall rock noise (2D)
        public float interiorNoiseScale;
        public float interiorNoiseStrength;
        public Vector2 interiorNoiseOffset;
    }

    // Store entrance per layer
    private readonly List<Vector3> _layerEntrances = new List<Vector3>();

    // =====================================================================
    // Public API
    // =====================================================================

    public override void GenerateCaves(MapProfile profile, Rng rng, Transform parent)
    {
        if (parent == null)
        {
            Debug.LogError("HeightmapCaveModule: parent is null.");
            return;
        }

        _layerEntrances.Clear();

        // Create/clear cave root
        Transform caveRoot = parent.Find("Caves");
        if (caveRoot == null)
        {
            GameObject rootGo = new GameObject("Caves");
            caveRoot = rootGo.transform;
            caveRoot.SetParent(parent, false);
        }
        else
        {
            for (int i = caveRoot.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(caveRoot.GetChild(i).gameObject);
            }
        }

        // Grid size
        int width, length;
        float tileSize;
        if (useProfileGrid && profile != null)
        {
            width = Mathf.Max(2, profile.mapWidth);
            length = Mathf.Max(2, profile.mapLength);
            tileSize = Mathf.Max(0.5f, profile.tileSize);
        }
        else
        {
            width = Mathf.Max(2, gridWidth);
            length = Mathf.Max(2, gridLength);
            tileSize = Mathf.Max(0.5f, cellSize);
        }

        int levels = Mathf.Max(1, caveLevels);
        float spacing = Mathf.Max(0.1f, caveLevelSpacing);

        for (int level = 0; level < levels; level++)
        {
            float layerBaseY = floorBaseHeight - level * spacing;
            Rng layerRng = rng.Split(100 + level);
            BuildLayerMesh(profile, layerRng, caveRoot, width, length, tileSize, layerBaseY, level);
        }
    }

    public override int GetLayerCount(MapProfile profile) => _layerEntrances.Count;

    public override Vector3 GetLayerEntranceHint(MapProfile profile, Transform parent, int layerIndex)
    {
        if (layerIndex >= 0 && layerIndex < _layerEntrances.Count)
            return _layerEntrances[layerIndex];

        return GetEntranceHint(profile, parent);
    }

    public override Vector3 GetEntranceHint(MapProfile profile, Transform parent)
    {
        if (_layerEntrances.Count > 0)
            return _layerEntrances[0];

        return parent != null ? parent.position : Vector3.zero;
    }

    // =====================================================================
    // Internal mesh build
    // =====================================================================

    private void BuildLayerMesh(
        MapProfile profile,
        Rng rng,
        Transform caveRoot,
        int width,
        int length,
        float tileSize,
        float baseY,
        int levelIndex)
    {
        int vertCountX = width + 1;
        int vertCountZ = length + 1;
        int floorVertexCount = vertCountX * vertCountZ;

        var vertices = new List<Vector3>(floorVertexCount * 2);
        var uvs = new List<Vector2>(floorVertexCount * 2);
        var triangles = new List<int>();

        float totalWidth = width * tileSize;
        float totalLength = length * tileSize;
        float halfWidth = totalWidth * 0.5f;
        float halfLength = totalLength * 0.5f;

        Vector3 rootPos = caveRoot.position;

        // Noise offsets
        float floorOffX = rng.NextFloat(-1000f, 1000f);
        float floorOffZ = rng.NextFloat(-1000f, 1000f);
        float floorDetailOffX = rng.NextFloat(-1000f, 1000f);
        float floorDetailOffZ = rng.NextFloat(-1000f, 1000f);

        float thickOffX = rng.NextFloat(-1000f, 1000f);
        float thickOffZ = rng.NextFloat(-1000f, 1000f);

        float warpOffX1 = rng.NextFloat(-1000f, 1000f);
        float warpOffZ1 = rng.NextFloat(-1000f, 1000f);
        float warpOffX2 = rng.NextFloat(-1000f, 1000f);
        float warpOffZ2 = rng.NextFloat(-1000f, 1000f);

        float[,] floorHeights = new float[vertCountX, vertCountZ];
        float[,] ceilHeights = new float[vertCountX, vertCountZ];

        // Rooms
        List<Room> rooms = GenerateRoomsForLayer(
            rng, width, length, tileSize, halfWidth, halfLength, baseY);

        // ---------------- Floor vertices ----------------
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                float localX = x * tileSize - halfWidth;
                float localZ = z * tileSize - halfLength;
                Vector2 localXZ = new Vector2(localX, localZ);

                // base world pos(도메인 워프, 높이 계산용)
                float worldX0 = rootPos.x + localX;
                float worldZ0 = rootPos.z + localZ;

                // Domain warp for height sampling
                float warpedX = worldX0;
                float warpedZ = worldZ0;

                if (useDomainWarp && warpNoiseScale > 0f)
                {
                    float w1 = Mathf.PerlinNoise(worldX0 * warpNoiseScale + warpOffX1,
                                                 worldZ0 * warpNoiseScale + warpOffZ1);
                    float w2 = Mathf.PerlinNoise(worldX0 * warpNoiseScale + warpOffX2,
                                                 worldZ0 * warpNoiseScale + warpOffZ2);

                    float strength = warpStrength;
                    warpedX += (w1 - 0.5f) * strength;
                    warpedZ += (w2 - 0.5f) * strength;
                }

                // 1) Base floor from noise
                float baseFloor = SampleFloorHeight(
                    worldX0, worldZ0,
                    warpedX, warpedZ,
                    baseY,
                    floorOffX, floorOffZ,
                    floorDetailOffX, floorDetailOffZ
                );

                // 2) Edge wall weight + floor raise
                float edgeWeight = 0f;
                if (useEdgeWalls && edgeWallWidth > 0f)
                {
                    float distToEdgeX = halfWidth - Mathf.Abs(localX);
                    float distToEdgeZ = halfLength - Mathf.Abs(localZ);
                    float distToEdge = Mathf.Min(distToEdgeX, distToEdgeZ); // 0 at border

                    edgeWeight = Mathf.InverseLerp(edgeWallWidth, 0f, distToEdge);
                    edgeWeight = Mathf.Clamp01(edgeWeight);
                    if (edgeWallPower != 1f)
                        edgeWeight = Mathf.Pow(edgeWeight, edgeWallPower);

                    float targetEdgeFloor = baseY + edgeWallHeight;
                    baseFloor = Mathf.Lerp(baseFloor, targetEdgeFloor, edgeWeight);
                }

                // 3) Thickness
                float thickness = SampleThickness(
                    worldX0, worldZ0,
                    baseThickness,
                    thicknessNoiseAmount,
                    thicknessNoiseScale,
                    thickOffX, thickOffZ
                );

                // 4) Shrink cavity near edge so floor/ceiling almost touch
                if (edgeWeight > 0f)
                {
                    float minCavity = Mathf.Max(0.01f, edgeMinCavityHeight);
                    thickness = Mathf.Lerp(thickness, minCavity, edgeWeight);
                }

                float baseCeil = baseFloor + thickness;

                // 5) Apply rooms: 높이 올리기 (내벽/외벽 모두 바위 느낌)
                float finalFloor;
                ApplyRoomsToSample(localXZ, baseFloor, rooms, out finalFloor);

                floorHeights[x, z] = finalFloor;
                ceilHeights[x, z] = baseCeil;

                // 6) 방 벽에 한해서 XZ 방향으로도 노이즈 displacement 적용
                Vector2 wallOffset = ComputeRoomWallOffset(localXZ, rooms);
                float displacedLocalX = localX + wallOffset.x;
                float displacedLocalZ = localZ + wallOffset.y;

                float worldX = rootPos.x + displacedLocalX;
                float worldZ = rootPos.z + displacedLocalZ;

                vertices.Add(new Vector3(worldX, finalFloor, worldZ));

                // UV는 위에서 본 좌표 기준으로 (displacedLocalX/Z 사용하면 텍스처도 같이 휘어짐)
                float u = (displacedLocalX + halfWidth) / totalWidth;
                float v = (displacedLocalZ + halfLength) / totalLength;
                uvs.Add(new Vector2(u, v));
            }
        }

        // Entrance hint at grid center (임시 – 필요하면 나중에 착륙 지점과 연동)
        ComputeAndStoreEntrance(
            profile,
            caveRoot,
            floorHeights,
            width,
            length,
            tileSize,
            halfWidth,
            halfLength,
            baseY,
            levelIndex
        );

        // ---------------- Ceiling vertices ----------------
        for (int z = 0; z < vertCountZ; z++)
        {
            for (int x = 0; x < vertCountX; x++)
            {
                int floorIndex = z * vertCountX + x;
                Vector3 bottom = vertices[floorIndex];
                float ceilY = ceilHeights[x, z];

                vertices.Add(new Vector3(bottom.x, ceilY, bottom.z));
                uvs.Add(uvs[floorIndex]); // same UV as floor (XZ 기준)
            }
        }

        int ceilingStartIndex = floorVertexCount;

        // ---------------- Floor + Ceiling triangles ----------------
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i0 = z * vertCountX + x;
                int i1 = i0 + 1;
                int i2 = i0 + vertCountX;
                int i3 = i2 + 1;

                // Floor
                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i1);

                triangles.Add(i1);
                triangles.Add(i2);
                triangles.Add(i3);

                // Ceiling (flip winding)
                int c0 = ceilingStartIndex + i0;
                int c1 = ceilingStartIndex + i1;
                int c2 = ceilingStartIndex + i2;
                int c3 = ceilingStartIndex + i3;

                triangles.Add(c0);
                triangles.Add(c3);
                triangles.Add(c2);

                triangles.Add(c0);
                triangles.Add(c1);
                triangles.Add(c3);
            }
        }

        if (triangles.Count == 0)
        {
            Debug.LogWarning($"HeightmapCaveModule: no triangles generated for layer {levelIndex}.");
            return;
        }

        // ---------------- Create mesh object ----------------
        Mesh mesh = new Mesh();
        mesh.indexFormat = (vertices.Count > 65000)
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject goMesh = new GameObject($"HeightmapCaveMesh_L{levelIndex}");
        goMesh.transform.SetParent(caveRoot, false);

        MeshFilter mf = goMesh.AddComponent<MeshFilter>();
        MeshRenderer mr = goMesh.AddComponent<MeshRenderer>();
        MeshCollider mc = goMesh.AddComponent<MeshCollider>();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        if (caveMaterial != null)
            mr.sharedMaterial = caveMaterial;
    }

    // =====================================================================
    // Helper methods
    // =====================================================================

    private List<Room> GenerateRoomsForLayer(
        Rng rng,
        int width,
        int length,
        float tileSize,
        float halfWidth,
        float halfLength,
        float baseY)
    {
        var rooms = new List<Room>();
        if (!useRooms) return rooms;

        int minRooms = Mathf.Max(0, minRoomsPerLayer);
        int maxRooms = Mathf.Max(minRooms, maxRoomsPerLayer);
        if (maxRooms <= 0) return rooms;

        int roomCount = rng.NextInt(minRooms, maxRooms + 1);
        if (roomCount <= 0) return rooms;

        float rMin = Mathf.Max(4f, Mathf.Min(roomMinRadius, roomMaxRadius));
        float rMax = Mathf.Max(rMin, Mathf.Max(roomMinRadius, roomMaxRadius));

        float ringMin = Mathf.Max(2f, Mathf.Min(roomRingWidthMin, roomRingWidthMax));
        float ringMax = Mathf.Max(ringMin, Mathf.Max(roomRingWidthMin, roomRingWidthMax));

        float raiseMin = roomWallRaiseMin;
        float raiseMax = Mathf.Max(raiseMin, roomWallRaiseMax);

        float angSpanMin = Mathf.Max(5f, Mathf.Min(roomEntranceAngleMin, roomEntranceAngleMax));
        float angSpanMax = Mathf.Max(angSpanMin, Mathf.Max(roomEntranceAngleMin, roomEntranceAngleMax));

        float maxOuterR = rMax + ringMax;
        float margin = maxOuterR + tileSize * 2f;

        float minX = -halfWidth + margin;
        float maxX = halfWidth - margin;
        float minZ = -halfLength + margin;
        float maxZ = halfLength - margin;

        if (minX > maxX)
        {
            minX = -halfWidth * 0.5f;
            maxX = halfWidth * 0.5f;
        }
        if (minZ > maxZ)
        {
            minZ = -halfLength * 0.5f;
            maxZ = halfLength * 0.5f;
        }

        for (int i = 0; i < roomCount; i++)
        {
            float innerR = rng.NextFloat(rMin, rMax);
            float ringW = rng.NextFloat(ringMin, ringMax);
            float outerR = innerR + ringW;

            float cx = rng.NextFloat(minX, maxX);
            float cz = rng.NextFloat(minZ, maxZ);

            float wallRaise = rng.NextFloat(raiseMin, raiseMax);

            // Entrance direction ~ towards center
            Vector2 toCenter = new Vector2(-cx, -cz);
            float baseAngleDeg = Mathf.Atan2(toCenter.y, toCenter.x) * Mathf.Rad2Deg;
            float randomOffset = rng.NextFloat(-25f, 25f);
            float entranceAngleDeg = baseAngleDeg + randomOffset;

            float span = rng.NextFloat(angSpanMin, angSpanMax);
            float halfSpan = span * 0.5f;

            Room room = new Room
            {
                centerLocal = new Vector2(cx, cz),
                innerRadius = innerR,
                outerRadius = outerR,
                wallRaise = wallRaise,
                entranceAngleDeg = entranceAngleDeg,
                entranceHalfAngleDeg = halfSpan,

                radiusNoiseScale = rng.NextFloat(roomRadiusNoiseScaleMin, roomRadiusNoiseScaleMax),
                radiusNoiseStrength = rng.NextFloat(roomRadiusNoiseStrengthMin, roomRadiusNoiseStrengthMax),
                radiusNoiseOffset = rng.NextFloat(0f, 1000f),

                interiorNoiseScale = rng.NextFloat(roomInteriorNoiseScaleMin, roomInteriorNoiseScaleMax),
                interiorNoiseStrength = rng.NextFloat(roomInteriorNoiseStrengthMin, roomInteriorNoiseStrengthMax),
                interiorNoiseOffset = new Vector2(
                    rng.NextFloat(0f, 1000f),
                    rng.NextFloat(0f, 1000f))
            };

            rooms.Add(room);
        }

        return rooms;
    }

    /// <summary>
    /// 방의 벽 전체를 두께 기반으로 올려서, 내벽/외벽 모두 바위 덩어리처럼 만든다.
    /// 단순히 innerRadius에만 계단처럼 붙는 게 아니라,
    /// innerRadius 근처부터 outerRadius까지 부드러운 "언덕" 단면이 되도록 보간한다.
    /// </summary>
    private void ApplyRoomsToSample(
        Vector2 localXZ,
        float baseFloor,
        List<Room> rooms,
        out float outFloor)
    {
        if (rooms == null || rooms.Count == 0)
        {
            outFloor = baseFloor;
            return;
        }

        float bestRaise = 0f;
        float innerFalloff = Mathf.Clamp01(roomInteriorInnerFalloff);

        for (int i = 0; i < rooms.Count; i++)
        {
            Room room = rooms[i];

            Vector2 toPoint = localXZ - room.centerLocal;
            float dist = toPoint.magnitude;
            if (dist < 0.0001f)
                dist = 0.0001f;

            // 입구 각도는 벽 생성 안 함
            float angle = Mathf.Atan2(toPoint.y, toPoint.x);
            float angleDeg = angle * Mathf.Rad2Deg;
            float deltaAngle = Mathf.Abs(Mathf.DeltaAngle(angleDeg, room.entranceAngleDeg));
            if (deltaAngle <= room.entranceHalfAngleDeg)
                continue;

            float innerR = room.innerRadius;
            float outerR = room.outerRadius;

            // 벽이 시작되는 방 내부 쪽 반지름 (조금 더 안쪽부터 서서히 올라가기 시작)
            float innerStart = innerR * innerFalloff;      // 예: innerR * 0.4
            // 벽 높이가 가장 높은 지점(대략 벽 두께의 1/3 지점 쯤)
            float midR = Mathf.Lerp(innerR, outerR, 0.35f);
            // 벽이 끝나는 바깥쪽 반지름
            float endR = outerR;

            if (dist < innerStart || dist > endR)
                continue;

            // raiseWeight: 0(바닥) → 1(벽 중앙) → 0(바깥) 형태의 언덕 프로파일
            float raiseWeight;
            if (dist <= midR)
            {
                float tIn = Mathf.InverseLerp(innerStart, midR, dist);
                raiseWeight = Mathf.SmoothStep(0f, 1f, tIn);
            }
            else
            {
                float tOut = Mathf.InverseLerp(midR, endR, dist);
                raiseWeight = Mathf.SmoothStep(1f, 0f, tOut);
            }

            // 2D 노이즈로 벽 전체를 울퉁불퉁하게 (내벽/외벽 동일)
            float rn = Mathf.PerlinNoise(
                localXZ.x * room.interiorNoiseScale + room.interiorNoiseOffset.x,
                localXZ.y * room.interiorNoiseScale + room.interiorNoiseOffset.y
            );

            float centered = rn - 0.35f;
            float rockOffset = centered * room.interiorNoiseStrength;

            float raise = (room.wallRaise + rockOffset) * raiseWeight;

            if (raise > bestRaise)
                bestRaise = raise;
        }

        outFloor = baseFloor + bestRaise;
    }

    /// <summary>
    /// 방의 벽 영역에서 XZ 위치도 노이즈 기반으로 옆으로 밀어줘서
    /// 내벽/외벽 모두 수직 절벽처럼 보이지 않고 바위처럼 울퉁불퉁하게 만든다.
    /// </summary>
    private Vector2 ComputeRoomWallOffset(Vector2 localXZ, List<Room> rooms)
    {
        if (rooms == null ||
            rooms.Count == 0 ||
            roomWallDisplaceAmount <= 0f ||
            roomWallDisplaceScale <= 0f)
        {
            return Vector2.zero;
        }

        float innerFalloff = Mathf.Clamp01(roomInteriorInnerFalloff);
        Vector2 accum = Vector2.zero;
        float accumWeight = 0f;

        for (int i = 0; i < rooms.Count; i++)
        {
            Room room = rooms[i];

            Vector2 toPoint = localXZ - room.centerLocal;
            float dist = toPoint.magnitude;
            if (dist < 0.0001f)
                continue;

            // 입구 방향은 통로 유지 위해 displacement 적용 X
            float angle = Mathf.Atan2(toPoint.y, toPoint.x);
            float angleDeg = angle * Mathf.Rad2Deg;
            float deltaAngle = Mathf.Abs(Mathf.DeltaAngle(angleDeg, room.entranceAngleDeg));
            if (deltaAngle <= room.entranceHalfAngleDeg)
                continue;

            float innerR = room.innerRadius;
            float outerR = room.outerRadius;

            float innerStart = innerR * innerFalloff;
            float midR = Mathf.Lerp(innerR, outerR, 0.35f);
            float endR = outerR;

            if (dist < innerStart || dist > endR)
                continue;

            // 높이에서 쓴 것과 같은 언덕형 weight 사용
            float weight;
            if (dist <= midR)
            {
                float tIn = Mathf.InverseLerp(innerStart, midR, dist);
                weight = Mathf.SmoothStep(0f, 1f, tIn);
            }
            else
            {
                float tOut = Mathf.InverseLerp(midR, endR, dist);
                weight = Mathf.SmoothStep(1f, 0f, tOut);
            }

            if (weight <= 0f)
                continue;

            // Perlin 기반 signed displacement
            float sx = localXZ.x * roomWallDisplaceScale + room.interiorNoiseOffset.x * 0.37f;
            float sz = localXZ.y * roomWallDisplaceScale + room.interiorNoiseOffset.y * 0.53f;
            float n = Mathf.PerlinNoise(sx, sz);  // 0..1
            float signed = (n - 0.5f) * 2f;       // -1..1

            // 방향: 대부분 방 중심(반경) 방향 + 약간의 접선 방향 섞기
            Vector2 radial = toPoint.normalized;
            Vector2 tangent = new Vector2(-radial.y, radial.x);

            float tangentialBias = 0.4f;
            Vector2 dir = (radial * (1f - tangentialBias) +
                           tangent * tangentialBias * Mathf.Sign(signed)).normalized;

            Vector2 offset = dir * (Mathf.Abs(signed) *
                                    roomWallDisplaceAmount *
                                    weight);

            accum += offset;
            accumWeight += weight;
        }

        if (accumWeight > 0f)
            accum /= accumWeight;

        return accum;
    }

    private float SampleFloorHeight(
        float worldX,
        float worldZ,
        float warpedX,
        float warpedZ,
        float baseY,
        float offsetX,
        float offsetZ,
        float detailOffsetX,
        float detailOffsetZ)
    {
        int octaves = Mathf.Max(1, floorOctaves);
        float lacunarity = Mathf.Max(1f, floorLacunarity);
        float persistence = Mathf.Clamp01(floorPersistence);

        float amplitude = 1f;
        float frequency = Mathf.Max(0.0001f, floorNoiseScale);
        float value = 0f;
        float maxValue = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float sx = warpedX * frequency + offsetX;
            float sz = warpedZ * frequency + offsetZ;
            float n = Mathf.PerlinNoise(sx, sz);
            value += n * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (maxValue > 0f)
            value /= maxValue;

        float large = (value - 0.5f) * 2f * floorHeightScale;

        // small-scale detail
        float dx = worldX * 0.15f + detailOffsetX;
        float dz = worldZ * 0.15f + detailOffsetZ;
        float d = Mathf.PerlinNoise(dx, dz);
        float detail = (d - 0.5f) * floorHeightScale * 0.25f;

        // ridge noise
        float rx = warpedX * floorNoiseScale * 0.7f + offsetX * 1.37f;
        float rz = warpedZ * floorNoiseScale * 0.7f + offsetZ * 1.91f;
        float r = Mathf.PerlinNoise(rx, rz);
        float ridge = (1f - Mathf.Abs(2f * r - 1f)) * floorHeightScale * 0.35f;

        return baseY + large + detail + ridge;
    }

    private float SampleThickness(
        float worldX,
        float worldZ,
        float baseThick,
        float noiseAmount,
        float noiseScale,
        float offX,
        float offZ)
    {
        float sx = worldX * noiseScale + offX;
        float sz = worldZ * noiseScale + offZ;
        float n = Mathf.PerlinNoise(sx, sz);

        float variation = (n - 0.5f) * 2f * noiseAmount;
        return Mathf.Max(0.5f, baseThick + variation);
    }

    private void ComputeAndStoreEntrance(
        MapProfile profile,
        Transform caveRoot,
        float[,] floorHeights,
        int width,
        int length,
        float tileSize,
        float halfWidth,
        float halfLength,
        float baseY,
        int levelIndex)
    {
        int vertCountX = width + 1;
        int vertCountZ = length + 1;

        int ix = vertCountX / 2;
        int iz = vertCountZ / 2;

        ix = Mathf.Clamp(ix, 0, vertCountX - 1);
        iz = Mathf.Clamp(iz, 0, vertCountZ - 1);

        float floorY = floorHeights[ix, iz];

        Vector3 rootPos = caveRoot.position;
        float worldX = rootPos.x + (ix * tileSize - halfWidth);
        float worldZ = rootPos.z + (iz * tileSize - halfLength);

        Vector3 entrance = new Vector3(worldX, floorY + entranceOffsetY, worldZ);

        if (_layerEntrances.Count == levelIndex)
        {
            _layerEntrances.Add(entrance);
        }
        else if (_layerEntrances.Count > levelIndex)
        {
            _layerEntrances[levelIndex] = entrance;
        }
        else
        {
            while (_layerEntrances.Count < levelIndex)
                _layerEntrances.Add(entrance);

            _layerEntrances.Add(entrance);
        }
    }
}
