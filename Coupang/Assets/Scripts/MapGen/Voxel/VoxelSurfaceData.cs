using UnityEngine;

/// <summary>
/// Bit flags that describe what kind of surface this voxel-air cell is:
/// - Floor: air cell that has solid voxel directly below (캐릭터가 설 수 있는 바닥)
/// - Wall:  air cell that is adjacent to solid on the side (벽 표면)
/// - Ceiling: air cell that has solid voxel directly above (천장 표면)
/// </summary>
[System.Flags]
public enum VoxelSurfaceFlags
{
    None = 0,
    Floor = 1 << 0,
    Wall = 1 << 1,
    Ceiling = 1 << 2,
}

/// <summary>
/// Bit flags that describe which region this surface belongs to:
/// - Room:        동굴 방 내부
/// - Corridor:    방과 방을 연결하는 통로
/// - GenericCave: 노이즈로 뚫린 기타 동굴/공동
/// - Exterior:    지상/야외 (하늘과 열린 표면)
/// 
/// 여러 개가 동시에 설정될 수도 있지만, 일반적으로는
/// 하나만 설정되는 식으로 사용한다.
/// </summary>
[System.Flags]
public enum VoxelRegionFlags
{
    None = 0,
    Room = 1 << 0,
    Corridor = 1 << 1,
    GenericCave = 1 << 2,
    Exterior = 1 << 3,
}

/// <summary>
/// Stores per-voxel surface classification (floor / wall / ceiling + region flags)
/// for a voxel chunk. This is completely independent from the actual mesh.
///
/// 사용 흐름:
/// 1) VoxelTerrainModule 가 VoxelChunk 를 생성
/// 2) VoxelSurfaceBuilder 가 이 컴포넌트를 찾아서 Initialize 호출 후
///    각 voxel 에 대해 flags 와 SurfaceCell[] 를 채운다.
/// 3) VoxelSpawnUtility 가 cells[] 를 돌면서 spawn filter 에 맞는 위치를 고른다.
/// </summary>
public class VoxelSurfaceData : MonoBehaviour
{
    // ─────────────────────────────────────────
    // Grid / world info
    // ─────────────────────────────────────────

    [Header("Voxel Dimensions")]
    public int sizeX;
    public int sizeY;
    public int sizeZ;

    [Header("Voxel World Info")]
    [Tooltip("World size of one voxel edge.")]
    public float voxelSize = 1f;

    [Tooltip("World Y of the bottom (y=0) of the voxel grid.")]
    public float worldBottomY;

    /// <summary>
    /// Per-voxel surface flags. Only meaningful where flags != None.
    /// Index order: [x, y, z].
    /// 
    /// 예) Floor 플래그가 달린 Air cell 은 "여기 바닥 표면 있음"을 의미.
    /// </summary>
    [Tooltip("Per-voxel surface flags. Only meaningful where flags != None. Index order: [x, y, z].")]
    public VoxelSurfaceFlags[,,] flags;

    // ─────────────────────────────────────────
    // Compact surface list for spawning
    // ─────────────────────────────────────────

    /// <summary>
    /// 하나의 "스폰 후보 지점"을 나타내는 데이터.
    /// - worldPosition: 실제 월드 좌표 (보통 표면 접점 위치)
    /// - normal: 표면 법선
    /// - surfaceFlags: Floor/Wall/Ceiling
    /// - regionFlags: Room/Corridor/GenericCave/Exterior
    /// - x,y,z: 해당 air cell 의 voxel 인덱스
    /// 
    /// VoxelSpawnUtility 는 이 배열을 돌면서 filter 를 적용하고 prefab 을 배치한다.
    /// </summary>
    [System.Serializable]
    public struct SurfaceCell
    {
        public int x;
        public int y;
        public int z;

        public Vector3 worldPosition;
        public Vector3 normal;

        public VoxelSurfaceFlags surfaceFlags;
        public VoxelRegionFlags regionFlags;
    }

    [Header("Surface Cells (for spawning)")]
    [Tooltip("Flattened list of surface cells used for spawning (built by VoxelSurfaceBuilder).")]
    public SurfaceCell[] cells;

    /// <summary>
    /// 현재 유효한 surface cell 개수 (cells 배열의 길이와 동일).
    /// 필요하다면 디버그용으로 사용할 수 있다.
    /// </summary>
    public int CellCount => (cells != null) ? cells.Length : 0;

    // ─────────────────────────────────────────
    // Initialization
    // ─────────────────────────────────────────

    /// <summary>
    /// Initialize the internal flags array and basic grid info.
    /// Cell 리스트(cells)는 VoxelSurfaceBuilder 에서 따로 채운다.
    /// </summary>
    public void Initialize(int sizeX, int sizeY, int sizeZ, float voxelSize, float worldBottomY)
    {
        this.sizeX = Mathf.Max(1, sizeX);
        this.sizeY = Mathf.Max(1, sizeY);
        this.sizeZ = Mathf.Max(1, sizeZ);

        this.voxelSize = Mathf.Max(0.0001f, voxelSize);
        this.worldBottomY = worldBottomY;

        flags = new VoxelSurfaceFlags[this.sizeX, this.sizeY, this.sizeZ];
        cells = null; // surface builder 가 나중에 채운다.
    }

    // ─────────────────────────────────────────
    // World position helpers
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the local-space position of the CENTER of voxel (x,y,z)
    /// relative to this transform. (y=0 이 worldBottomY 에 해당)
    /// </summary>
    private Vector3 GetVoxelCenterLocal(int x, int y, int z)
    {
        return new Vector3(
            (x + 0.5f) * voxelSize,
            (y + 0.5f) * voxelSize,
            (z + 0.5f) * voxelSize
        );
    }

    /// <summary>
    /// Returns the world position of the CENTER of voxel (x,y,z).
    /// This is useful if you want to spawn an object in the middle of an air cell.
    /// </summary>
    public Vector3 GetVoxelCenterWorld(int x, int y, int z)
    {
        Vector3 local = GetVoxelCenterLocal(x, y, z);
        return transform.TransformPoint(local);
    }

    /// <summary>
    /// Returns the world position of the FLOOR contact for this air voxel.
    /// Assumes this voxel actually has the Floor flag set.
    /// 바닥 접점 = 셀 중앙에서 반 voxelSize 만큼 아래.
    /// </summary>
    public Vector3 GetFloorContactWorld(int x, int y, int z)
    {
        Vector3 localCenter = GetVoxelCenterLocal(x, y, z);
        Vector3 localFloor = localCenter - Vector3.up * (0.5f * voxelSize);
        return transform.TransformPoint(localFloor);
    }

    /// <summary>
    /// Returns the world position of the CEILING contact for this air voxel.
    /// Assumes this voxel actually has the Ceiling flag set.
    /// 천장 접점 = 셀 중앙에서 반 voxelSize 만큼 위.
    /// </summary>
    public Vector3 GetCeilingContactWorld(int x, int y, int z)
    {
        Vector3 localCenter = GetVoxelCenterLocal(x, y, z);
        Vector3 localCeiling = localCenter + Vector3.up * (0.5f * voxelSize);
        return transform.TransformPoint(localCeiling);
    }

    // ─────────────────────────────────────────
    // Bounds / flags helpers
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns true if the given cell index is inside bounds.
    /// </summary>
    public bool InBounds(int x, int y, int z)
    {
        return (x >= 0 && x < sizeX &&
                y >= 0 && y < sizeY &&
                z >= 0 && z < sizeZ);
    }

    /// <summary>
    /// Helper to get flags safely. Returns VoxelSurfaceFlags.None if out of bounds.
    /// </summary>
    public VoxelSurfaceFlags GetFlags(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
            return VoxelSurfaceFlags.None;

        return flags[x, y, z];
    }

    /// <summary>
    /// Sets the surface flags for a given voxel, if in bounds.
    /// VoxelSurfaceBuilder 등에서 사용.
    /// </summary>
    public void SetFlags(int x, int y, int z, VoxelSurfaceFlags newFlags)
    {
        if (!InBounds(x, y, z))
            return;

        flags[x, y, z] = newFlags;
    }

    // ─────────────────────────────────────────
    // SurfaceCell 관리 (SurfaceBuilder에서 사용)
    // ─────────────────────────────────────────

    /// <summary>
    /// VoxelSurfaceBuilder 가 계산한 SurfaceCell 리스트를
    /// 최종 배열(cells)에 설정하는 용도.
    /// </summary>
    public void SetSurfaceCells(System.Collections.Generic.List<SurfaceCell> cellList)
    {
        if (cellList == null || cellList.Count == 0)
        {
            cells = null;
        }
        else
        {
            cells = cellList.ToArray();
        }
    }
}
