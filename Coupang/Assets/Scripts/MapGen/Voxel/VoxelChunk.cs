using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class VoxelChunk : MonoBehaviour
{
    [Header("Voxel Dimensions (in voxels)")]
    [Tooltip("Number of voxels along X axis.")]
    public int sizeX;

    [Tooltip("Number of voxels along Y axis (vertical).")]
    public int sizeY;

    [Tooltip("Number of voxels along Z axis.")]
    public int sizeZ;

    [Tooltip("Voxel data array. Index order: [x, y, z].")]
    public VoxelType[,,] voxels;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    [Header("Mesh Appearance")]
    [Tooltip("If true, merge shared vertices and recalculate normals for smooth shading.")]
    public bool smoothNormals = true;

    [Tooltip("If true, slightly jitter vertices along their normals using Perlin noise to break perfect cube shapes.")]
    public bool jitterSurface = true;

    [Tooltip("Maximum offset distance along the normal for jitter. Recommended: 0.05 ~ 0.3 * voxelSize.")]
    public float jitterAmplitude = 0.2f;

    [Tooltip("Frequency of the Perlin noise used for jitter. Larger = more frequent bumps.")]
    public float jitterFrequency = 0.3f;

    /// <summary>
    /// Initializes internal data and components.
    /// Must be called before writing to the voxel array.
    /// </summary>
    public void Initialize(int sizeX, int sizeY, int sizeZ,
                           Material groundMaterial,
                           Material undergroundMaterial,
                           Material caveMaterial)
    {
        this.sizeX = Mathf.Max(1, sizeX);
        this.sizeY = Mathf.Max(1, sizeY);
        this.sizeZ = Mathf.Max(1, sizeZ);

        voxels = new VoxelType[this.sizeX, this.sizeY, this.sizeZ];

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        if (groundMaterial == null)
        {
            Debug.LogWarning("VoxelChunk.Initialize: groundMaterial is null. Mesh will be invisible.");
        }

        if (undergroundMaterial == null)
        {
            undergroundMaterial = groundMaterial;
        }

        if (caveMaterial == null)
        {
            caveMaterial = undergroundMaterial;
        }

        // Three materials: 0 = Ground, 1 = Underground, 2 = Cave
        meshRenderer.sharedMaterials = new Material[]
        {
            groundMaterial,
            undergroundMaterial,
            caveMaterial
        };
    }

    /// <summary>
    /// Returns voxel type at the given index.
    /// If outside bounds, returns VoxelType.Air.
    /// </summary>
    public VoxelType GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x >= sizeX ||
            y < 0 || y >= sizeY ||
            z < 0 || z >= sizeZ)
        {
            return VoxelType.Air;
        }

        return voxels[x, y, z];
    }

    /// <summary>
    /// Builds and assigns a mesh from the current voxel data.
    /// </summary>
    /// <param name="voxelSize">World size of one voxel edge (usually MapProfile.tileSize).</param>
    public void BuildMesh(float voxelSize)
    {
        // 1) Build basic blocky mesh (current VoxelMesher À¯Áö)
        Mesh mesh = MarchingCubesMesher.BuildMesh(voxels, voxelSize, 0.5f);

        // 2) Optional post-processing: smooth normals + jitter along normal
        if (mesh != null && (smoothNormals || jitterSurface))
        {
            VoxelMeshPostProcessor.Process(
                mesh,
                smoothNormals,
                jitterSurface,
                jitterAmplitude,
                jitterFrequency
            );
        }

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;
    }
}
