using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Carves a natural-looking ramp corridor between two heightmap-based meshes
/// (surface + cave layers). It modifies existing meshes so the corridor looks
/// integrated into the terrain. The ramp can have a soft "terraced rock" feel,
/// but avoids hard vertical spikes.
/// </summary>
public static class SteppedCorridorCarver
{
    private struct PathSample
    {
        public Vector3 position;
        public float t;
    }

    public static void CarveCorridorBetweenMeshes(
        MeshFilter upperMeshFilter,
        MeshFilter lowerMeshFilter,
        Vector3 startWorld,
        Vector3 endWorld,
        float corridorRadius,
        float blendRadius,
        float stepHeight,
        float verticalNoiseAmplitude,
        float maxSlopeDegrees,
        Rng rng)
    {
        if (upperMeshFilter == null || lowerMeshFilter == null)
            return;

        float radius = Mathf.Max(0.1f, corridorRadius);
        float blend = Mathf.Max(0f, blendRadius);
        // stepHeight는 계단 느낌을 줄 정도로만 사용 (너무 크면 다시 스파이크 생김)
        float terraceSize = Mathf.Max(0.2f, stepHeight);

        List<PathSample> samples = GeneratePathSamples(
            startWorld,
            endWorld,
            radius,
            maxSlopeDegrees,
            rng
        );

        CarveMeshAlongPath(upperMeshFilter, samples, radius, blend, terraceSize, verticalNoiseAmplitude);
        CarveMeshAlongPath(lowerMeshFilter, samples, radius, blend, terraceSize, verticalNoiseAmplitude);
    }

    /// <summary>
    /// Generates a slightly curved path between start and end with lateral noise,
    /// and tries to keep the effective slope below maxSlopeDegrees.
    /// </summary>
    private static List<PathSample> GeneratePathSamples(
        Vector3 start,
        Vector3 end,
        float corridorRadius,
        float maxSlopeDegrees,
        Rng rng)
    {
        int segments = 56;
        var samples = new List<PathSample>(segments + 1);

        Vector3 startXZ = new Vector3(start.x, 0f, start.z);
        Vector3 endXZ = new Vector3(end.x, 0f, end.z);
        Vector3 baseDirXZ = endXZ - startXZ;

        float baseDistXZ = baseDirXZ.magnitude;
        if (baseDistXZ < 0.0001f)
        {
            baseDirXZ = new Vector3(1f, 0f, 0f);
            baseDistXZ = 1f;
        }
        Vector3 baseDirXZNorm = baseDirXZ / baseDistXZ;

        float dy = end.y - start.y;
        float maxSlopeRad = Mathf.Max(1f, maxSlopeDegrees) * Mathf.Deg2Rad;

        // Horizontal distance needed to keep slope below maxSlope
        float requiredHoriz = Mathf.Abs(dy) / Mathf.Tan(maxSlopeRad);
        float extraHoriz = Mathf.Max(0f, requiredHoriz - baseDistXZ);

        // If distance is not enough, bend sideways to increase path length
        Vector3 sideDirXZ = new Vector3(-baseDirXZNorm.z, 0f, baseDirXZNorm.x);
        float sideAmp = extraHoriz * 0.5f + corridorRadius * 0.3f;

        float lateralSeed = rng.NextFloat(0f, 1000f);
        float verticalSeed = rng.NextFloat(0f, 1000f);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;

            Vector3 baseXZ = Vector3.Lerp(startXZ, endXZ, t);

            // Soft S-shaped curve
            float sin = Mathf.Sin(t * Mathf.PI); // 0 -> 1 -> 0
            float lateralMain = sin * sideAmp;

            // Small lateral noise
            float lateralNoise = Mathf.PerlinNoise(t * 2.3f + lateralSeed, lateralSeed * 0.37f);
            lateralNoise = (lateralNoise - 0.5f) * 0.5f * sideAmp;

            Vector3 offsetXZ = sideDirXZ * (lateralMain + lateralNoise);
            Vector3 posXZ = baseXZ + offsetXZ;

            // Y: simple linear ramp + small noise
            float y = Mathf.Lerp(start.y, end.y, t);
            float vNoise = Mathf.PerlinNoise(t * 1.7f + verticalSeed, verticalSeed * 0.51f);
            vNoise = (vNoise - 0.5f) * 2f;
            y += vNoise * (corridorRadius * 0.1f);

            samples.Add(new PathSample
            {
                position = new Vector3(posXZ.x, y, posXZ.z),
                t = t
            });
        }

        return samples;
    }

    /// <summary>
    /// Modifies a mesh so that vertices near the path are moved to form
    /// a walkable ramp with smooth falloff into the surrounding terrain.
    /// Cave meshes are detected by name and only their floors are carved.
    /// </summary>
    private static void CarveMeshAlongPath(
        MeshFilter meshFilter,
        List<PathSample> samples,
        float corridorRadius,
        float blendRadius,
        float terraceSize,
        float verticalNoiseAmplitude)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null || samples == null || samples.Count == 0)
            return;

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] verts = mesh.vertices;
        Transform tr = meshFilter.transform;

        float maxRadius = corridorRadius + blendRadius;
        float maxRadiusSqr = maxRadius * maxRadius;

        // Detect cave layers by name (HeightmapCaveMesh_L*)
        bool isCaveLayer = meshFilter.gameObject.name.StartsWith("HeightmapCaveMesh");

        // For cave layers we only want to touch the floor (bottom half)
        float minY = 0f;
        float maxY = 0f;
        float midY = 0f;

        if (isCaveLayer)
        {
            bool first = true;
            for (int i = 0; i < verts.Length; i++)
            {
                float wy = tr.TransformPoint(verts[i]).y;
                if (first)
                {
                    minY = maxY = wy;
                    first = false;
                }
                else
                {
                    if (wy < minY) minY = wy;
                    if (wy > maxY) maxY = wy;
                }
            }

            midY = (minY + maxY) * 0.5f;
        }

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldPos = tr.TransformPoint(verts[i]);

            // For cave layers, skip ceiling vertices (we only carve the floor)
            if (isCaveLayer && worldPos.y > midY)
                continue;

            Vector2 vXZ = new Vector2(worldPos.x, worldPos.z);

            float bestDistSqr = float.MaxValue;
            int bestIndex = -1;

            // Find closest path sample in XZ plane
            for (int s = 0; s < samples.Count; s++)
            {
                Vector3 sp = samples[s].position;
                Vector2 sXZ = new Vector2(sp.x, sp.z);
                float dSqr = (vXZ - sXZ).sqrMagnitude;
                if (dSqr < bestDistSqr)
                {
                    bestDistSqr = dSqr;
                    bestIndex = s;
                }
            }

            if (bestIndex < 0 || bestDistSqr > maxRadiusSqr)
                continue;

            float dist = Mathf.Sqrt(bestDistSqr);
            float radialT = Mathf.InverseLerp(maxRadius, corridorRadius, dist);
            if (radialT <= 0f)
                continue;

            PathSample sample = samples[bestIndex];

            // Base ramp height
            float rampY = sample.position.y;

            // Soft terracing: small snap to terrace grid
            float terraced = Mathf.Round(rampY / terraceSize) * terraceSize;
            rampY = Mathf.Lerp(rampY, terraced, 0.4f);

            // Small vertical noise
            if (verticalNoiseAmplitude > 0f)
            {
                float n = Mathf.PerlinNoise(sample.t * 5.13f, 123.456f);
                n = (n - 0.5f) * 2f;
                rampY += n * verticalNoiseAmplitude;
            }

            float targetY;

            if (!isCaveLayer)
            {
                // Surface: mainly dig downward, do not create spikes upwards
                targetY = Mathf.Min(rampY, worldPos.y);
            }
            else
            {
                // Cave floor: we can raise a bit or lower a bit to follow the ramp
                // Lerp towards rampY but keep some of the original floor
                targetY = Mathf.Lerp(worldPos.y, rampY, 0.7f);
            }

            // Smooth blending based on distance from path center
            float blendT = radialT * radialT; // softer edge

            worldPos.y = Mathf.Lerp(worldPos.y, targetY, blendT);

            verts[i] = tr.InverseTransformPoint(worldPos);
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;

        MeshCollider mc = meshFilter.GetComponent<MeshCollider>();
        if (mc != null)
        {
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;
        }
    }
}
