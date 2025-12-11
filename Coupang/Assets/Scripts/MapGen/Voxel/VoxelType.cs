using UnityEngine;

/// <summary>
/// Voxel type used by the terrain.
/// Ground      = voxels at or above world Y 0 (surface mass).
/// Underground = solid crust below world Y 0.
/// Cave        = deep cave region using cave material.
/// Air         = empty space (no mesh).
/// </summary>
public enum VoxelType
{
    Air = 0,
    Ground = 1,
    Underground = 2,
    Cave = 3
}
