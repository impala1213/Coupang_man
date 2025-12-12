using System.Collections.Generic;
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
/// 이후 다른 스크립트에서 surfaceData.cells 를 참조해서
/// - 방 바닥에만 상자 스폰
/// - 통로 천장에만 종유석 스폰
/// - 동굴 벽에만 이끼 스폰
/// 같은 로직을 구현할 수 있다.
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

        // Get or add VoxelSurfaceData component on this chunk
        VoxelSurfaceData data = owner.GetComponent<VoxelSurfaceData>();
        if (data == null)
        {
            data = owner.AddComponent<VoxelSurfaceData>();
        }
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

        // Compact list of surface cells (spawn candidates)
        List<VoxelSurfaceData.SurfaceCell> cellList = new List<VoxelSurfaceData.SurfaceCell>();

        // Helper to treat any non-Air as solid
        bool IsSolid(VoxelType t) => t != VoxelType.Air;

        Transform tOwner = owner.transform;

        // Classification loop:
        // - Only look at Air cells (caves, rooms, corridors, etc.)
        // - For each Air cell, check solid below/above/sides to tag Floor/Wall/Ceiling
        // - Also set Region flags (Room / Corridor / GenericCave)
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                // y=0, y=sizeY-1 는 위아래 이웃 체크에서 out-of-bounds 피하기 위해 제외
                for (int y = 1; y < sizeY - 1; y++)
                {
                    if (voxels[x, y, z] == VoxelType.Air == false)
                        continue;

                    float worldYCenter = worldBottomY + (y + 0.5f) * voxelSize;
                    if (worldYCenter > maxYLimit)
                        continue;

                    // ───── Region classification ─────
                    VoxelRegionFlags region = VoxelRegionFlags.None;

                    bool isRoom = (roomMask != null && roomMask[x, y, z]);
                    bool isCorridor = (corridorMask != null && corridorMask[x, y, z]);

                    if (isRoom)
                    {
                        region |= VoxelRegionFlags.Room;
                    }
                    if (isCorridor)
                    {
                        region |= VoxelRegionFlags.Corridor;
                    }

                    // 방/통로가 아닌 지하 Air 셀은 GenericCave로 취급
                    if (region == VoxelRegionFlags.None)
                    {
                        // 이 빌더는 보통 지하(caveRegionMaxWorldY 이하)만 대상으로 쓰이므로
                        // 별도의 Exterior 구분 없이 GenericCave 로 묶는다.
                        region |= VoxelRegionFlags.GenericCave;
                    }

                    // ───── Geometric orientation (Floor / Wall / Ceiling) ─────
                    VoxelSurfaceFlags f = VoxelSurfaceFlags.None;

                    bool hasFloor = IsSolid(voxels[x, y - 1, z]);
                    bool hasCeiling = IsSolid(voxels[x, y + 1, z]);

                    if (hasFloor)
                        f |= VoxelSurfaceFlags.Floor;

                    if (hasCeiling)
                        f |= VoxelSurfaceFlags.Ceiling;

                    // Horizontal walls
                    bool hasWall = false;
                    if (x > 0 && IsSolid(voxels[x - 1, y, z])) hasWall = true;
                    else if (x < sizeX - 1 && IsSolid(voxels[x + 1, y, z])) hasWall = true;
                    else if (z > 0 && IsSolid(voxels[x, y, z - 1])) hasWall = true;
                    else if (z < sizeZ - 1 && IsSolid(voxels[x, y, z + 1])) hasWall = true;

                    if (hasWall)
                        f |= VoxelSurfaceFlags.Wall;

                    if (f == VoxelSurfaceFlags.None)
                    {
                        // 이 air cell 주변에 solid 가 없어서
                        // 바닥/벽/천장 표면으로 보지 않음 → 스폰 후보 제외
                        continue;
                    }

                    // 3D flags 배열에 기록 (방향 정보만)
                    flags[x, y, z] = f;

                    // ───── SurfaceCell 생성 (worldPosition + normal 등) ─────
                    VoxelSurfaceData.SurfaceCell cell = new VoxelSurfaceData.SurfaceCell();
                    cell.x = x;
                    cell.y = y;
                    cell.z = z;
                    cell.surfaceFlags = f;
                    cell.regionFlags = region;

                    // 로컬 기준 좌표
                    Vector3 localCenter = new Vector3(
                        (x + 0.5f) * voxelSize,
                        (y + 0.5f) * voxelSize,
                        (z + 0.5f) * voxelSize
                    );

                    Vector3 localPos = localCenter;   // 접점 위치
                    Vector3 localNormal = Vector3.zero; // 표면 법선

                    // Floor / Ceiling
                    if (hasFloor)
                    {
                        localNormal += Vector3.up;
                        // 바닥 접점은 아래쪽 반 voxel
                        localPos = localCenter - Vector3.up * (0.5f * voxelSize);
                    }

                    if (hasCeiling)
                    {
                        localNormal += Vector3.down;

                        // 만약 Floor 가 없고 Ceiling만 있는 경우, 접점을 천장 쪽으로
                        if (!hasFloor)
                        {
                            localPos = localCenter + Vector3.up * (0.5f * voxelSize);
                        }
                    }

                    // Walls: 주변 solid 방향을 기반으로 법선 누적
                    if (hasWall)
                    {
                        if (x > 0 && IsSolid(voxels[x - 1, y, z]))
                            localNormal += Vector3.right; // solid 왼쪽 ⇒ normal +X

                        if (x < sizeX - 1 && IsSolid(voxels[x + 1, y, z]))
                            localNormal += Vector3.left;  // solid 오른쪽 ⇒ normal -X

                        if (z > 0 && IsSolid(voxels[x, y, z - 1]))
                            localNormal += Vector3.forward; // solid 뒤 ⇒ normal +Z

                        if (z < sizeZ - 1 && IsSolid(voxels[x, y, z + 1]))
                            localNormal += Vector3.back;    // solid 앞 ⇒ normal -Z

                        // 만약 Floor/Ceiling이 없고 순수 벽만 있는 경우,
                        // 벽 접점을 solid-공기 경계면으로 옮겨준다.
                        if (!hasFloor && !hasCeiling)
                        {
                            if (localNormal.sqrMagnitude > 1e-6f)
                            {
                                localNormal.Normalize();
                                // solid→air 방향의 normal 이므로,
                                // 경계면은 중심에서 -normal * halfVoxel 만큼 이동한 위치.
                                localPos = localCenter - localNormal * (0.5f * voxelSize);
                            }
                        }
                    }

                    // localNormal 이 비정상이면 기본 up 으로
                    if (localNormal.sqrMagnitude < 1e-6f)
                    {
                        localNormal = Vector3.up;
                    }
                    else
                    {
                        localNormal.Normalize();
                    }

                    // 월드 공간으로 변환
                    cell.worldPosition = tOwner.TransformPoint(localPos);
                    cell.normal = tOwner.TransformDirection(localNormal).normalized;

                    cellList.Add(cell);
                }
            }
        }

        // SurfaceCell 리스트를 데이터에 저장
        data.SetSurfaceCells(cellList);

        return data;
    }
}
