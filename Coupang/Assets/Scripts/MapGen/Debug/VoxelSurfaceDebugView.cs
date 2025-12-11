using UnityEngine;

/// <summary>
/// Debug visualizer for all VoxelSurfaceData components under a given root.
/// Attach this to any persistent object in the scene (e.g. MapRunner, LandingHelper),
/// then set 'root' to the parent that owns generated chunks.
/// It will find VoxelSurfaceData in children and draw gizmos for floor / wall / ceiling.
/// </summary>
[ExecuteAlways]
public class VoxelSurfaceDebugView : MonoBehaviour
{
    [Header("Search Root")]
    [Tooltip("Root transform under which voxel chunks are created at runtime.\n" +
             "If null, this GameObject's transform is used.")]
    public Transform root;

    [Header("Draw Settings")]
    [Tooltip("Skip cells by this stride (1 = every cell, 2 = every second cell, etc.).")]
    public int debugStride = 2;

    [Tooltip("Size of gizmo cubes in world units.")]
    public float gizmoSize = 0.5f;

    [Header("Which surfaces to show")]
    public bool showFloors = true;
    public bool showWalls = true;
    public bool showCeilings = true;

    [Header("Colors")]
    public Color floorColor = new Color(0.2f, 1.0f, 0.2f, 0.9f);  // green
    public Color wallColor = new Color(1.0f, 0.5f, 0.2f, 0.9f);  // orange
    public Color ceilingColor = new Color(0.2f, 0.5f, 1.0f, 0.9f);  // blue

    private bool _loggedOnce = false;

    private void OnDrawGizmos()
    {
        Transform searchRoot = root != null ? root : transform;

        // Find all surface data components under root (including children)
        var surfaces = searchRoot.GetComponentsInChildren<VoxelSurfaceData>(true);
        if (surfaces == null || surfaces.Length == 0)
            return;

        if (!_loggedOnce)
        {
            _loggedOnce = true;
            foreach (var s in surfaces)
            {
                LogFlagCounts(s);
            }
        }

        foreach (var s in surfaces)
        {
            DrawSurfaceGizmos(s);
        }
    }

    private void LogFlagCounts(VoxelSurfaceData data)
    {
        if (data.flags == null) return;

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

        Debug.Log(
            $"[VoxelSurfaceDebugView] '{data.gameObject.name}' Surface flags: " +
            $"Floors={floorCount}, Walls={wallCount}, Ceilings={ceilingCount}");
    }

    private void DrawSurfaceGizmos(VoxelSurfaceData data)
    {
        if (data.flags == null)
            return;

        var flags = data.flags;
        int sizeX = data.sizeX;
        int sizeY = data.sizeY;
        int sizeZ = data.sizeZ;

        int stride = Mathf.Max(1, debugStride);

        for (int z = 0; z < sizeZ; z += stride)
        {
            for (int x = 0; x < sizeX; x += stride)
            {
                for (int y = 1; y < sizeY - 1; y += stride)
                {
                    VoxelSurfaceFlags f = flags[x, y, z];
                    if (f == VoxelSurfaceFlags.None)
                        continue;

                    bool isFloor = (f & VoxelSurfaceFlags.Floor) != 0;
                    bool isWall = (f & VoxelSurfaceFlags.Wall) != 0;
                    bool isCeiling = (f & VoxelSurfaceFlags.Ceiling) != 0;

                    Color? color = null;

                    if (isFloor && showFloors)
                        color = floorColor;
                    else if (isCeiling && showCeilings)
                        color = ceilingColor;
                    else if (isWall && showWalls)
                        color = wallColor;

                    if (!color.HasValue)
                        continue;

                    Gizmos.color = color.Value;

                    Vector3 pos = data.GetVoxelCenterWorld(x, y, z);
                    Vector3 size = Vector3.one * gizmoSize;

                    Gizmos.DrawCube(pos, size);
                }
            }
        }
    }
}
