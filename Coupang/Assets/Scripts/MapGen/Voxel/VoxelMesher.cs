using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Greedy mesher for voxel data.
/// Builds a mesh with 3 submeshes:
///   0 = Ground
///   1 = Underground
///   2 = Cave
/// Voxels are assumed to be axis-aligned cubes of size voxelSize.
/// </summary>
public static class VoxelMesher
{
    /// <summary>
    /// Converts voxel array into a greedy block mesh.
    /// </summary>
    public static Mesh BuildMesh(VoxelType[,,] voxels, float voxelSize)
    {
        int sizeX = voxels.GetLength(0);
        int sizeY = voxels.GetLength(1);
        int sizeZ = voxels.GetLength(2);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var trianglesGround = new List<int>();      // submesh 0
        var trianglesUnderground = new List<int>(); // submesh 1
        var trianglesCave = new List<int>();        // submesh 2

        List<int>[] submeshTris = new List<int>[]
        {
            trianglesGround,
            trianglesUnderground,
            trianglesCave
        };

        // Greedy on X faces
        GreedyAlongX(voxels, sizeX, sizeY, sizeZ, voxelSize,
            vertices, normals, uvs, submeshTris);

        // Greedy on Y faces
        GreedyAlongY(voxels, sizeX, sizeY, sizeZ, voxelSize,
            vertices, normals, uvs, submeshTris);

        // Greedy on Z faces
        GreedyAlongZ(voxels, sizeX, sizeY, sizeZ, voxelSize,
            vertices, normals, uvs, submeshTris);

        Mesh mesh = new Mesh();
        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);

        mesh.subMeshCount = 3;
        mesh.SetTriangles(trianglesGround, 0, true);
        mesh.SetTriangles(trianglesUnderground, 1, true);
        mesh.SetTriangles(trianglesCave, 2, true);

        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Maps voxel type to submesh index:
    ///   Ground -> 0, Underground -> 1, Cave -> 2.
    /// Air or anything else returns -1.
    /// </summary>
    private static int GetSubmeshIndex(VoxelType type)
    {
        switch (type)
        {
            case VoxelType.Ground:      return 0;
            case VoxelType.Underground: return 1;
            case VoxelType.Cave:        return 2;
            default:                    return -1; // treat as empty
        }
    }

    // ------------------------------------------------------------------
    // Greedy meshing along X axis (faces with normal +/-X)
    // ------------------------------------------------------------------
    private static void GreedyAlongX(
        VoxelType[,,] voxels,
        int sizeX, int sizeY, int sizeZ,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        int dimU = sizeY; // along Y
        int dimV = sizeZ; // along Z
        int[] mask = new int[dimU * dimV];

        // q is "slice index" between x-1 and x, 0..sizeX
        for (int q = 0; q <= sizeX; q++)
        {
            // Build mask for this slice
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    VoxelType a = (q > 0)         ? voxels[q - 1, y, z] : VoxelType.Air;
                    VoxelType b = (q < sizeX)     ? voxels[q,     y, z] : VoxelType.Air;

                    int idA = GetSubmeshIndex(a);
                    int idB = GetSubmeshIndex(b);

                    int index = y + z * dimU;

                    // Only create faces between solid and air.
                    if (idA >= 0 && idB < 0)
                    {
                        // Solid on +X side, air on -X side -> face with normal +X.
                        mask[index] = idA + 1; // positive = +X
                    }
                    else if (idB >= 0 && idA < 0)
                    {
                        // Solid on -X side, air on +X side -> face with normal -X.
                        mask[index] = -(idB + 1); // negative = -X
                    }
                    else
                    {
                        // Both solid or both air -> no face.
                        mask[index] = 0;
                    }
                }
            }

            // Run 2D greedy on mask (Y vs Z)
            for (int v = 0; v < dimV; v++)
            {
                int n = v * dimU;
                int u = 0;

                while (u < dimU)
                {
                    int c = mask[n + u];
                    if (c != 0)
                    {
                        // Compute width
                        int width = 1;
                        while (u + width < dimU && mask[n + u + width] == c)
                        {
                            width++;
                        }

                        // Compute height
                        int height = 1;
                        bool done = false;
                        while (v + height < dimV)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (mask[(v + height) * dimU + u + k] != c)
                                {
                                    done = true;
                                    break;
                                }
                            }
                            if (done) break;
                            height++;
                        }

                        // Add quad for rectangle [u..u+width-1, v..v+height-1]
                        AddQuadX(
                            c,
                            q,
                            u,
                            v,
                            width,
                            height,
                            voxelSize,
                            vertices,
                            normals,
                            uvs,
                            submeshTris
                        );

                        // Clear area from mask
                        for (int j = 0; j < height; j++)
                        {
                            int row = (v + j) * dimU;
                            for (int i = 0; i < width; i++)
                            {
                                mask[row + u + i] = 0;
                            }
                        }

                        u += width;
                    }
                    else
                    {
                        u++;
                    }
                }
            }
        }
    }

    private static void AddQuadX(
        int maskVal,
        int xSlice,
        int yStart,
        int zStart,
        int width,
        int height,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        bool backFace = maskVal < 0;
        int submesh = Mathf.Abs(maskVal) - 1;
        if (submesh < 0 || submesh >= submeshTris.Length)
            return;

        List<int> tris = submeshTris[submesh];

        int vertBase = vertices.Count;

        float x = xSlice * voxelSize;
        float y0 = yStart * voxelSize;
        float y1 = (yStart + width) * voxelSize;
        float z0 = zStart * voxelSize;
        float z1 = (zStart + height) * voxelSize;

        Vector3 normal = backFace ? Vector3.left : Vector3.right;

        // Vertex order for +X normal (CCW when looking in +X)
        Vector3 v0 = new Vector3(x, y0, z0);
        Vector3 v1 = new Vector3(x, y1, z0);
        Vector3 v2 = new Vector3(x, y1, z1);
        Vector3 v3 = new Vector3(x, y0, z1);

        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        // Simple tiling UVs (1 unit per voxel)
        Vector2 uv0 = new Vector2(0f, 0f);
        Vector2 uv1 = new Vector2(0f, height);
        Vector2 uv2 = new Vector2(width, height);
        Vector2 uv3 = new Vector2(width, 0f);

        uvs.Add(uv0);
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);

        if (!backFace)
        {
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 1);
            tris.Add(vertBase + 2);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 3);
        }
        else
        {
            // Reverse winding for -X
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 1);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 3);
            tris.Add(vertBase + 2);
        }
    }

    // ------------------------------------------------------------------
    // Greedy meshing along Y axis (faces with normal +/-Y)
    // ------------------------------------------------------------------
    private static void GreedyAlongY(
        VoxelType[,,] voxels,
        int sizeX, int sizeY, int sizeZ,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        int dimU = sizeX; // along X
        int dimV = sizeZ; // along Z
        int[] mask = new int[dimU * dimV];

        for (int q = 0; q <= sizeY; q++)
        {
            // Build mask between y-1 and y
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    VoxelType a = (q > 0)         ? voxels[x, q - 1, z] : VoxelType.Air;
                    VoxelType b = (q < sizeY)     ? voxels[x, q,     z] : VoxelType.Air;

                    int idA = GetSubmeshIndex(a);
                    int idB = GetSubmeshIndex(b);

                    int index = x + z * dimU;

                    if (idA >= 0 && idB < 0)
                    {
                        // Solid above, air below -> face with normal +Y
                        mask[index] = idA + 1;
                    }
                    else if (idB >= 0 && idA < 0)
                    {
                        // Solid below, air above -> face with normal -Y
                        mask[index] = -(idB + 1);
                    }
                    else
                    {
                        mask[index] = 0;
                    }
                }
            }

            // Greedy 2D on XZ
            for (int v = 0; v < dimV; v++)
            {
                int n = v * dimU;
                int u = 0;

                while (u < dimU)
                {
                    int c = mask[n + u];
                    if (c != 0)
                    {
                        int width = 1;
                        while (u + width < dimU && mask[n + u + width] == c)
                        {
                            width++;
                        }

                        int height = 1;
                        bool done = false;
                        while (v + height < dimV)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (mask[(v + height) * dimU + u + k] != c)
                                {
                                    done = true;
                                    break;
                                }
                            }
                            if (done) break;
                            height++;
                        }

                        AddQuadY(
                            c,
                            q,
                            u,
                            v,
                            width,
                            height,
                            voxelSize,
                            vertices,
                            normals,
                            uvs,
                            submeshTris
                        );

                        for (int j = 0; j < height; j++)
                        {
                            int row = (v + j) * dimU;
                            for (int i = 0; i < width; i++)
                            {
                                mask[row + u + i] = 0;
                            }
                        }

                        u += width;
                    }
                    else
                    {
                        u++;
                    }
                }
            }
        }
    }

    private static void AddQuadY(
        int maskVal,
        int ySlice,
        int xStart,
        int zStart,
        int width,
        int height,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        bool backFace = maskVal < 0;
        int submesh = Mathf.Abs(maskVal) - 1;
        if (submesh < 0 || submesh >= submeshTris.Length)
            return;

        List<int> tris = submeshTris[submesh];

        int vertBase = vertices.Count;

        float y = ySlice * voxelSize;
        float x0 = xStart * voxelSize;
        float x1 = (xStart + width) * voxelSize;
        float z0 = zStart * voxelSize;
        float z1 = (zStart + height) * voxelSize;

        Vector3 normal = backFace ? Vector3.down : Vector3.up;

        // Vertex order for +Y normal (CCW from above)
        Vector3 v0 = new Vector3(x0, y, z0);
        Vector3 v1 = new Vector3(x1, y, z0);
        Vector3 v2 = new Vector3(x1, y, z1);
        Vector3 v3 = new Vector3(x0, y, z1);

        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        Vector2 uv0 = new Vector2(0f, 0f);
        Vector2 uv1 = new Vector2(width, 0f);
        Vector2 uv2 = new Vector2(width, height);
        Vector2 uv3 = new Vector2(0f, height);

        uvs.Add(uv0);
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);

        if (!backFace)
        {
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 1);
            tris.Add(vertBase + 2);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 3);
        }
        else
        {
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 1);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 3);
            tris.Add(vertBase + 2);
        }
    }

    // ------------------------------------------------------------------
    // Greedy meshing along Z axis (faces with normal +/-Z)
    // ------------------------------------------------------------------
    private static void GreedyAlongZ(
        VoxelType[,,] voxels,
        int sizeX, int sizeY, int sizeZ,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        int dimU = sizeX; // along X
        int dimV = sizeY; // along Y
        int[] mask = new int[dimU * dimV];

        for (int q = 0; q <= sizeZ; q++)
        {
            // Build mask between z-1 and z
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    VoxelType a = (q > 0)         ? voxels[x, y, q - 1] : VoxelType.Air;
                    VoxelType b = (q < sizeZ)     ? voxels[x, y, q]     : VoxelType.Air;

                    int idA = GetSubmeshIndex(a);
                    int idB = GetSubmeshIndex(b);

                    int index = x + y * dimU;

                    if (idA >= 0 && idB < 0)
                    {
                        // Solid towards +Z, air outside -> normal +Z
                        mask[index] = idA + 1;
                    }
                    else if (idB >= 0 && idA < 0)
                    {
                        // Solid towards -Z, air outside -> normal -Z
                        mask[index] = -(idB + 1);
                    }
                    else
                    {
                        mask[index] = 0;
                    }
                }
            }

            // Greedy on XY plane
            for (int v = 0; v < dimV; v++)
            {
                int n = v * dimU;
                int u = 0;

                while (u < dimU)
                {
                    int c = mask[n + u];
                    if (c != 0)
                    {
                        int width = 1;
                        while (u + width < dimU && mask[n + u + width] == c)
                        {
                            width++;
                        }

                        int height = 1;
                        bool done = false;
                        while (v + height < dimV)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (mask[(v + height) * dimU + u + k] != c)
                                {
                                    done = true;
                                    break;
                                }
                            }
                            if (done) break;
                            height++;
                        }

                        AddQuadZ(
                            c,
                            q,
                            u,
                            v,
                            width,
                            height,
                            voxelSize,
                            vertices,
                            normals,
                            uvs,
                            submeshTris
                        );

                        for (int j = 0; j < height; j++)
                        {
                            int row = (v + j) * dimU;
                            for (int i = 0; i < width; i++)
                            {
                                mask[row + u + i] = 0;
                            }
                        }

                        u += width;
                    }
                    else
                    {
                        u++;
                    }
                }
            }
        }
    }

    private static void AddQuadZ(
        int maskVal,
        int zSlice,
        int xStart,
        int yStart,
        int width,
        int height,
        float voxelSize,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int>[] submeshTris)
    {
        bool backFace = maskVal < 0;
        int submesh = Mathf.Abs(maskVal) - 1;
        if (submesh < 0 || submesh >= submeshTris.Length)
            return;

        List<int> tris = submeshTris[submesh];

        int vertBase = vertices.Count;

        float z = zSlice * voxelSize;
        float x0 = xStart * voxelSize;
        float x1 = (xStart + width) * voxelSize;
        float y0 = yStart * voxelSize;
        float y1 = (yStart + height) * voxelSize;

        Vector3 normal = backFace ? Vector3.back : Vector3.forward;

        // Vertex order for +Z normal
        Vector3 v0 = new Vector3(x0, y0, z);
        Vector3 v1 = new Vector3(x1, y0, z);
        Vector3 v2 = new Vector3(x1, y1, z);
        Vector3 v3 = new Vector3(x0, y1, z);

        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        Vector2 uv0 = new Vector2(0f, 0f);
        Vector2 uv1 = new Vector2(width, 0f);
        Vector2 uv2 = new Vector2(width, height);
        Vector2 uv3 = new Vector2(0f, height);

        uvs.Add(uv0);
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);

        if (!backFace)
        {
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 1);
            tris.Add(vertBase + 2);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 3);
        }
        else
        {
            tris.Add(vertBase + 0);
            tris.Add(vertBase + 2);
            tris.Add(vertBase + 1);

            tris.Add(vertBase + 0);
            tris.Add(vertBase + 3);
            tris.Add(vertBase + 2);
        }
    }
}
