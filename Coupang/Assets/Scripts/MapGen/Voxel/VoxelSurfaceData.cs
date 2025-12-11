using UnityEngine;

/// <summary>
/// Stores per-voxel surface classification (floor / wall / ceiling + region flags)
/// for a voxel chunk. This is completely independent from the actual mesh.
/// 
/// - Attach this to the same GameObject as VoxelChunk (e.g. "VoxelChunk_Main").
/// - Filled by VoxelSurfaceBuilder.
/// - Later systems (spawn managers, decoration, etc.) can read this data to
///   decide where to place objects (e.g., stalactites on RoomCeiling only).
/// </summary>
public class VoxelSurfaceData : MonoBehaviour
{
    [Header("Voxel Dimensions")]
    public int sizeX;
    public int sizeY;
    public int sizeZ;

    [Header("Voxel World Info")]
    [Tooltip("World size of one voxel edge.")]
    public float voxelSize = 1f;

    [Tooltip("World Y of the bottom (y=0) of the voxel grid.")]
    public float worldBottomY;

    [Tooltip("Per-voxel surface flags. Only meaningful where flags != None.\nIndex order: [x, y, z].")]
    public VoxelSurfaceFlags[,,] flags;

    /// <summary>
    /// Initialize the internal array and basic info.
    /// Call this once before filling in the flags.
    /// </summary>
    public void Initialize(int sizeX, int sizeY, int sizeZ, float voxelSize, float worldBottomY)
    {
        this.sizeX = Mathf.Max(1, sizeX);
        this.sizeY = Mathf.Max(1, sizeY);
        this.sizeZ = Mathf.Max(1, sizeZ);

        this.voxelSize = Mathf.Max(0.0001f, voxelSize);
        this.worldBottomY = worldBottomY;

        flags = new VoxelSurfaceFlags[this.sizeX, this.sizeY, this.sizeZ];
    }

    /// <summary>
    /// Returns the world position of the CENTER of voxel (x,y,z).
    /// This is useful if you want to spawn an object in the middle of an air cell.
    /// </summary>
    public Vector3 GetVoxelCenterWorld(int x, int y, int z)
    {
        Vector3 local = new Vector3(
            (x + 0.5f) * voxelSize,
            (y + 0.5f) * voxelSize,
            (z + 0.5f) * voxelSize
        );

        return transform.TransformPoint(local);
    }

    /// <summary>
    /// Returns the world position of the FLOOR contact for this air voxel.
    /// Assumes this voxel actually has the Floor flag set.
    /// </summary>
    public Vector3 GetFloorContactWorld(int x, int y, int z)
    {
        Vector3 localCenter = new Vector3(
            (x + 0.5f) * voxelSize,
            (y + 0.5f) * voxelSize,
            (z + 0.5f) * voxelSize
        );

        // Floor plane is half a voxel below the center of the air cell.
        Vector3 localFloor = localCenter - Vector3.up * (0.5f * voxelSize);
        return transform.TransformPoint(localFloor);
    }

    /// <summary>
    /// Returns the world position of the CEILING contact for this air voxel.
    /// Assumes this voxel actually has the Ceiling flag set.
    /// </summary>
    public Vector3 GetCeilingContactWorld(int x, int y, int z)
    {
        Vector3 localCenter = new Vector3(
            (x + 0.5f) * voxelSize,
            (y + 0.5f) * voxelSize,
            (z + 0.5f) * voxelSize
        );

        // Ceiling plane is half a voxel above the center.
        Vector3 localCeiling = localCenter + Vector3.up * (0.5f * voxelSize);
        return transform.TransformPoint(localCeiling);
    }

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
}
