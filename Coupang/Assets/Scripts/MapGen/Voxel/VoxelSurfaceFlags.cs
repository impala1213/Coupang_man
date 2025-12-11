using System;

/// <summary>
/// Bitmask describing what kind of surface this air voxel represents,
/// and which logical region it belongs to.
/// 
/// - Floor / Wall / Ceiling: geometric orientation
/// - RegionXXXX: logical grouping (room / corridor / cave / interior / exterior)
/// 
/// You can freely combine flags, e.g.:
///   Floor | RegionRoom
///   Wall  | RegionCorridor | RegionCave
/// </summary>
[Flags]
public enum VoxelSurfaceFlags : byte
{
    None = 0,

    // Geometric orientation
    Floor = 1 << 0,    // solid below
    Wall = 1 << 1,    // solid on any horizontal side
    Ceiling = 1 << 2,    // solid above

    // Logical regions (you can extend or reinterpret as needed)
    RegionRoom = 1 << 3,
    RegionCorridor = 1 << 4,
    RegionCave = 1 << 5, // generic "underground cave space"
    RegionInterior = 1 << 6, // building interior 등
    RegionExterior = 1 << 7  // 바깥 영역 등
}
