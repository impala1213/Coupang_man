using UnityEngine;

/// <summary>
/// Static helper that analyzes a voxel field and builds surface classification
/// (floor / wall / ceiling + region flags) into a VoxelSurfaceData component.
/// 
/// Typical use:
///   var surfaceData = VoxelSurfaceBuilder.BuildSurfaceData(
///       chunkGO,
///       chunk.voxels,
///       voxelSize,
///       worldBottomY,
///       roomMask: roomMask,
///       corridorMask: corridorMask,
///       maxWorldY: caveRegionMaxWorldY
///   );
/// 
/// 이후 다른 스크립트에서 surfaceData.flags[x,y,z]를 참조해서
/// - 방 바닥에만 상자 스폰
/// - 통로 천장에만 종유석 스폰
/// - 동굴 벽에만 이끼 스폰
/// 같은 로직 구현 가능.
/// </summary>
public static class VoxelSurfaceBuilder
{
    /// <summary>
    /// Build and attach VoxelSurfaceData to the given chunk GameObject.
    /// </summary>
    /// <param name="owner">
    /// GameObject that represents the voxel chunk (same one that has VoxelChunk).
    /// </param>
    /// <param name="voxels">
    /// VoxelType 3D array (Air / Ground / Underground / Cave ...).
    /// </param>
    /// <param name="voxelSize">
    /// World size of one voxel edge.
    /// </param>
    /// <param name="worldBottomY">
    /// World Y at voxel y=0.
    /// </param>
    /// <param name="roomMask">
    /// Optional mask indicating which air cells belong to "room" region.
    /// If null, no Room region flags are set.
    /// </param>
    /// <param name="corridorMask">
    /// Optional mask indicating which air cells belong to "corridor" region.
    /// If null, no Corridor region flags are set.
    /// </param>
    /// <param name="maxWorldY">
    /// Optional max world Y for classification. Any air cells whose center Y
    /// is above this will be ignored (useful to only classify underground part).
    /// Pass null to classify all heights.
    /// </param>
    public static VoxelSurfaceData BuildSurfaceData(
        GameObject owner,
        VoxelType[,,] voxels,
        float voxelSize,
        float worldBottomY,
        bool[,,] roomMask = null,
        bool[,,] corridorMask = null,
        float? maxWorldY = null)
    {
        if (owner == null || voxels == null)
        {
            Debug.LogError("VoxelSurfaceBuilder.BuildSurfaceData: owner or voxels is null.");
            return null;
        }

        int sizeX = voxels.GetLength(0);
        int sizeY = voxels.GetLength(1);
        int sizeZ = voxels.GetLength(2);

        // Create and initialize component
        VoxelSurfaceData data = owner.AddComponent<VoxelSurfaceData>();
        data.Initialize(sizeX, sizeY, sizeZ, voxelSize, worldBottomY);

        VoxelSurfaceFlags[,,] flags = data.flags;

        // Safety: if masks are given, they must match dimensions
        if (roomMask != null &&
            (roomMask.GetLength(0) != sizeX ||
             roomMask.GetLength(1) != sizeY ||
             roomMask.GetLength(2) != sizeZ))
        {
            Debug.LogWarning("VoxelSurfaceBuilder: roomMask dimension mismatch. Ignoring roomMask.");
            roomMask = null;
        }

        if (corridorMask != null &&
            (corridorMask.GetLength(0) != sizeX ||
             corridorMask.GetLength(1) != sizeY ||
             corridorMask.GetLength(2) != sizeZ))
        {
            Debug.LogWarning("VoxelSurfaceBuilder: corridorMask dimension mismatch. Ignoring corridorMask.");
            corridorMask = null;
        }

        float maxYLimit = float.MaxValue;
        if (maxWorldY.HasValue)
            maxYLimit = maxWorldY.Value + voxelSize * 0.5f;

        // Classification loop:
        // - We only look at Air cells (caves, rooms, corridors, etc.)
        // - For each Air cell, check solid below/above/sides to tag Floor/Wall/Ceiling
        // - Also set Region flags based on masks.
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 1; y < sizeY - 1; y++) // avoid top/bottom border issues
                {
                    if (voxels[x, y, z] != VoxelType.Air)
                        continue;

                    float worldYCenter = worldBottomY + (y + 0.5f) * voxelSize;
                    if (worldYCenter > maxYLimit)
                        continue;

                    VoxelSurfaceFlags f = VoxelSurfaceFlags.None;

                    // Region classification (optional masks)
                    bool isRoom = roomMask != null && roomMask[x, y, z];
                    bool isCorridor = corridorMask != null && corridorMask[x, y, z];

                    if (isRoom)
                    {
                        f |= VoxelSurfaceFlags.RegionRoom;
                        // 동굴 안의 방이라는 의미로 RegionCave 같이 쓸 수 있음
                        f |= VoxelSurfaceFlags.RegionCave;
                        f |= VoxelSurfaceFlags.RegionInterior; // 필요 없으면 나중에 빼도 됨
                    }

                    if (isCorridor)
                    {
                        f |= VoxelSurfaceFlags.RegionCorridor;
                        f |= VoxelSurfaceFlags.RegionCave;
                        f |= VoxelSurfaceFlags.RegionInterior;
                    }

                    // 만약 방/통로 개념이 없는 환경이라면,
                    // 나중에 호출쪽에서 roomMask/corridorMask 대신
                    // "이 영역은 Interior다" 이런 식으로 따로 플래그를 후처리해도 됨.

                    if (f == VoxelSurfaceFlags.None)
                    {
                        // 이 셀은 어떤 region에도 속하지 않는 Air -> 굳이 표면 분류 안 해도 됨.
                        continue;
                    }

                    // Floor: solid directly below
                    if (voxels[x, y - 1, z] != VoxelType.Air)
                    {
                        f |= VoxelSurfaceFlags.Floor;
                    }

                    // Ceiling: solid directly above
                    if (voxels[x, y + 1, z] != VoxelType.Air)
                    {
                        f |= VoxelSurfaceFlags.Ceiling;
                    }

                    // Walls: any horizontal neighbor is solid
                    bool hasWall = false;

                    if (x > 0 && voxels[x - 1, y, z] != VoxelType.Air) hasWall = true;
                    else if (x < sizeX - 1 && voxels[x + 1, y, z] != VoxelType.Air) hasWall = true;
                    else if (z > 0 && voxels[x, y, z - 1] != VoxelType.Air) hasWall = true;
                    else if (z < sizeZ - 1 && voxels[x, y, z + 1] != VoxelType.Air) hasWall = true;

                    if (hasWall)
                    {
                        f |= VoxelSurfaceFlags.Wall;
                    }

                    flags[x, y, z] = f;
                }
            }
        }

        return data;
    }
}
