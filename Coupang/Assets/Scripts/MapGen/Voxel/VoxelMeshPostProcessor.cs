using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Post-processing utilities for voxel meshes:
/// - Optionally welds shared vertices to get smooth shading.
/// - Optionally jitters vertices along their normals using Perlin noise,
///   to break perfect cube shapes and make terrain look more organic.
/// </summary>
public static class VoxelMeshPostProcessor
{
    /// <summary>
    /// Processes the given mesh in-place.
    /// </summary>
    /// <param name="mesh">Mesh to modify.</param>
    /// <param name="smoothNormals">If true, welds vertices and recalculates normals.</param>
    /// <param name="jitterSurface">If true, jitters vertices along normals using Perlin noise.</param>
    /// <param name="jitterAmplitude">Maximum offset distance along normal.</param>
    /// <param name="jitterFrequency">Perlin noise frequency.</param>
    public static void Process(
        Mesh mesh,
        bool smoothNormals,
        bool jitterSurface,
        float jitterAmplitude,
        float jitterFrequency)
    {
        if (mesh == null)
            return;

        // 1) Optionally weld vertices so faces share vertices,
        //    which allows smooth normals across cube edges.
        if (smoothNormals)
        {
            WeldVertices(mesh);
        }

        // 2) First normal calculation (needed if we want to jitter along existing normals)
        mesh.RecalculateNormals();

        if (jitterSurface && jitterAmplitude > 0f)
        {
            ApplyNormalJitter(mesh, jitterAmplitude, jitterFrequency);
        }

        // 3) Final normals / bounds
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Merges vertices with identical positions (exact float match).
    /// This reduces vertex count and allows normals to be smoothed across faces.
    /// </summary>
    private static void WeldVertices(Mesh mesh)
    {
        Vector3[] oldVerts = mesh.vertices;
        Vector2[] oldUVs = mesh.uv;

        int vertexCount = oldVerts.Length;
        if (vertexCount == 0)
            return;

        // Map from position -> new index
        Dictionary<Vector3, int> map = new Dictionary<Vector3, int>(vertexCount);
        List<Vector3> newVerts = new List<Vector3>(vertexCount);
        List<Vector2> newUVs = new List<Vector2>(vertexCount);

        int[] remap = new int[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 v = oldVerts[i];

            if (map.TryGetValue(v, out int existingIndex))
            {
                remap[i] = existingIndex;
            }
            else
            {
                int newIndex = newVerts.Count;
                map.Add(v, newIndex);
                newVerts.Add(v);

                if (oldUVs != null && oldUVs.Length > 0)
                {
                    newUVs.Add(oldUVs[i]);
                }

                remap[i] = newIndex;
            }
        }

        // Remap triangles for each submesh
        int subMeshCount = mesh.subMeshCount;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] tris = mesh.GetTriangles(sub);
            for (int t = 0; t < tris.Length; t++)
            {
                tris[t] = remap[tris[t]];
            }

            mesh.SetTriangles(tris, sub);
        }

        mesh.SetVertices(newVerts);

        if (oldUVs != null && oldUVs.Length > 0)
        {
            mesh.SetUVs(0, newUVs);
        }
    }

    /// <summary>
    /// Applies Perlin-noise-based offset along the vertex normal.
    /// Because we use world-space position as input to Perlin,
    /// vertices that share the same position get the same offset,
    /// so no cracks appear between faces.
    /// </summary>
    private static void ApplyNormalJitter(
        Mesh mesh,
        float amplitude,
        float frequency)
    {
        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;

        if (verts == null || verts.Length == 0)
            return;

        float freq = Mathf.Max(0.0001f, frequency);

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            Vector3 n = normals[i];

            // Sample 2D Perlin using X/Z (you can also add Y if you want 3D feel)
            float noise = Mathf.PerlinNoise(v.x * freq, v.z * freq);
            float offset = (noise - 0.5f) * 2f * amplitude;

            verts[i] = v + n * offset;
        }

        mesh.SetVertices(verts);
    }
}
