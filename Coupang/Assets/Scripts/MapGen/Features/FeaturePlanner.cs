using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plans feature selection + placement using a precomputed raw surface height map (noise-only).
/// The returned placements can be used to apply flatten constraints during base terrain generation.
/// </summary>
public static class FeaturePlanner
{
    /// <summary>
    /// Selects feature instances based on directory entries and places them on the raw height map.
    /// </summary>
    /// <param name="profile">Map profile (used for landing avoidance and base height).</param>
    /// <param name="directory">Feature directory (may be null).</param>
    /// <param name="rng">Deterministic RNG.</param>
    /// <param name="rawHeightMap">Noise-only height map [x,z].</param>
    /// <param name="voxelSize">Voxel size in world units.</param>
    /// <param name="halfWidth">Half of total world width (X) in world units.</param>
    /// <param name="halfLength">Half of total world length (Z) in world units.</param>
    public static List<FeaturePlacement> Plan(
        MapProfile profile,
        FeatureDirectory directory,
        Rng rng,
        float[,] rawHeightMap,
        float voxelSize,
        float halfWidth,
        float halfLength)
    {
        var placements = new List<FeaturePlacement>(16);

        if (profile == null || directory == null || directory.entries == null || directory.entries.Length == 0 || rng == null || rawHeightMap == null)
            return placements;

        // Build a weighted pool of valid entries.
        List<FeatureDirectory.Entry> pool = new List<FeatureDirectory.Entry>(directory.entries.Length);
        int totalWeight = 0;

        for (int i = 0; i < directory.entries.Length; i++)
        {
            FeatureDirectory.Entry e = directory.entries[i];
            if (e == null || e.feature == null)
                continue;

            int w = Mathf.Max(0, e.weight);
            if (w <= 0)
                continue;

            pool.Add(e);
            totalWeight += w;
        }

        if (pool.Count == 0 || totalWeight <= 0)
            return placements;

        // If budget selection is disabled, keep legacy behavior: each entry rolls a count independently.
        if (!directory.useTotalBudget)
        {
            for (int p = 0; p < pool.Count; p++)
            {
                FeatureDirectory.Entry e = pool[p];
                int minC = Mathf.Max(0, e.minCount);
                int maxC = Mathf.Max(minC, e.maxCount);

                int count = minC;
                if (maxC > minC)
                    count += rng.NextInt(0, (maxC - minC) + 1);

                for (int i = 0; i < count; i++)
                {
                    FeatureDefinition feature = e.feature;
                    if (feature == null)
                        continue;

                    FeaturePlacement placement;
                    if (TryPlaceOne(profile, directory, feature, rng.Split(1000 + p * 31 + i), rawHeightMap, voxelSize, halfWidth, halfLength, placements, out placement))
                    {
                        placements.Add(placement);
                    }
                }
            }

            return placements;
        }

        // Budget selection:
        // 1) Try to place all minCount (mandatory instances)
        // 2) Pick remaining instances by weight until total budget is filled (or attempts run out)
        int[] placedCounts = new int[pool.Count];
        int sumMin = 0;
        int sumMax = 0;

        for (int i = 0; i < pool.Count; i++)
        {
            int minC = Mathf.Max(0, pool[i].minCount);
            int maxC = Mathf.Max(minC, pool[i].maxCount);
            sumMin += minC;
            sumMax += maxC;
        }

        int minTotal = Mathf.Max(0, directory.minTotalInstances);
        int maxTotal = Mathf.Max(minTotal, directory.maxTotalInstances);

        // Clamp budget to what's actually possible.
        int targetTotal = rng.NextInt(minTotal, maxTotal + 1);
        targetTotal = Mathf.Clamp(targetTotal, sumMin, Mathf.Max(sumMin, sumMax));

        // Mandatory placement (minCount)
        for (int p = 0; p < pool.Count; p++)
        {
            FeatureDirectory.Entry e = pool[p];
            int minC = Mathf.Max(0, e.minCount);
            for (int i = 0; i < minC; i++)
            {
                FeatureDefinition feature = e.feature;
                if (feature == null)
                    continue;

                FeaturePlacement placement;
                if (TryPlaceOne(profile, directory, feature, rng.Split(2000 + p * 97 + i), rawHeightMap, voxelSize, halfWidth, halfLength, placements, out placement))
                {
                    placements.Add(placement);
                    placedCounts[p]++;
                }
            }
        }

        int remaining = Mathf.Max(0, targetTotal - placements.Count);
        int attempts = 0;
        int maxAttempts = Mathf.Max(1, directory.budgetFillMaxAttempts);

        while (remaining > 0 && attempts < maxAttempts)
        {
            attempts++;

            // Build eligible pool (not at maxCount)
            int eligibleTotalW = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                FeatureDirectory.Entry e = pool[i];
                int maxC = Mathf.Max(Mathf.Max(0, e.minCount), e.maxCount);
                if (placedCounts[i] >= maxC)
                    continue;

                eligibleTotalW += Mathf.Max(0, e.weight);
            }

            if (eligibleTotalW <= 0)
                break;

            int pick = rng.NextInt(0, eligibleTotalW);
            int accum = 0;
            int chosenIndex = -1;

            for (int i = 0; i < pool.Count; i++)
            {
                FeatureDirectory.Entry e = pool[i];
                int maxC = Mathf.Max(Mathf.Max(0, e.minCount), e.maxCount);
                if (placedCounts[i] >= maxC)
                    continue;

                int w = Mathf.Max(0, e.weight);
                accum += w;
                if (pick < accum)
                {
                    chosenIndex = i;
                    break;
                }
            }

            if (chosenIndex < 0)
                break;

            FeatureDefinition chosen = pool[chosenIndex].feature;
            if (chosen == null)
                continue;

            FeaturePlacement placement;
            if (TryPlaceOne(profile, directory, chosen, rng.Split(5000 + attempts * 13), rawHeightMap, voxelSize, halfWidth, halfLength, placements, out placement))
            {
                placements.Add(placement);
                placedCounts[chosenIndex]++;
                remaining--;
            }
        }

        return placements;
    }


    private static bool TryPlaceOne(
        MapProfile profile,
        FeatureDirectory directory,
        FeatureDefinition feature,
        Rng rng,
        float[,] rawHeightMap,
        float voxelSize,
        float halfWidth,
        float halfLength,
        List<FeaturePlacement> existing,
        out FeaturePlacement placement)
    {
        placement = default;

        int sizeX = rawHeightMap.GetLength(0);
        int sizeZ = rawHeightMap.GetLength(1);

        // Compute boundary margin so the footprint stays within the world.
        float hx = feature.GetFootprintHalfWidth();
        float hz = feature.GetFootprintHalfLength();
        float margin = Mathf.Max(hx, hz);

        if (directory.keepMarginFromBoundary)
            margin += Mathf.Max(0f, directory.boundaryMargin);

        float minX = -halfWidth + margin;
        float maxX = halfWidth - margin;
        float minZ = -halfLength + margin;
        float maxZ = halfLength - margin;

        if (maxX <= minX || maxZ <= minZ)
            return false;

        float reservedR = feature.GetReservedRadius();
        int tries = Mathf.Max(1, directory.placementTriesPerInstance);

        float bestScore = float.MaxValue;
        Vector3 bestPos = Vector3.zero;
        bool found = false;
        float bestTargetY = 0f;

        // Avoid landing zone (center).
        float avoidR = 0f;
        if (feature.avoidLandingZone)
        {
            float landingR = Mathf.Max(0f, profile.landingRadius);
            float landingFalloff = Mathf.Max(0f, profile.landingFalloff);
            avoidR = landingR + landingFalloff + Mathf.Max(0f, feature.extraAvoidLandingRadius);
        }

        for (int t = 0; t < tries; t++)
        {
            float lx = rng.NextFloat(minX, maxX);
            float lz = rng.NextFloat(minZ, maxZ);

            float r = Mathf.Sqrt(lx * lx + lz * lz);
            if (r < feature.minRadiusFromCenter || r > feature.maxRadiusFromCenter)
                continue;

            if (avoidR > 0f && r <= avoidR)
                continue;

            // Overlap check (2D circle).
            bool overlap = false;
            for (int i = 0; i < existing.Count; i++)
            {
                FeaturePlacement other = existing[i];
                if (other.feature == null)
                    continue;

                Vector2 a = new Vector2(lx, lz);
                Vector2 b = new Vector2(other.localPosition.x, other.localPosition.z);
                float minDist = reservedR + Mathf.Max(0f, other.reservedRadius);
                if ((a - b).sqrMagnitude < minDist * minDist)
                {
                    overlap = true;
                    break;
                }
            }

            if (overlap)
                continue;

            // Sample center height.
            float centerY = SampleHeight(rawHeightMap, voxelSize, halfWidth, halfLength, lx, lz);

            // Determine flatten target height based on mode.
            float targetY = ResolveTargetY(profile, feature, centerY);

            // Estimate slope within footprint using corner samples.
            float maxSlope = EstimateMaxSlopeDegrees(rawHeightMap, voxelSize, halfWidth, halfLength, lx, lz, hx, hz, centerY);
            if (maxSlope > feature.maxSlopeDegrees)
                continue;

            // Score: prefer flatter terrain + smaller delta between target and center (less terrain deformation).
            float score = maxSlope * 10f + Mathf.Abs(targetY - centerY);

            if (score < bestScore)
            {
                bestScore = score;
                bestPos = new Vector3(lx, targetY, lz);
                bestTargetY = targetY;
                found = true;
            }
        }

        if (!found)
            return false;

        float yaw = rng.NextFloat(0f, 360f);
        placement = new FeaturePlacement(feature, bestPos, yaw, bestTargetY, reservedR);
        return true;
    }

    private static float ResolveTargetY(MapProfile profile, FeatureDefinition feature, float sampledCenterY)
    {
        float y;
        switch (feature.flattenTargetMode)
        {
            case FeatureDefinition.FlattenTargetMode.BaseHeight:
                y = profile.baseHeight;
                break;

            case FeatureDefinition.FlattenTargetMode.FixedWorldY:
                y = feature.flattenFixedWorldY;
                break;

            case FeatureDefinition.FlattenTargetMode.SampledHeight:
            default:
                y = sampledCenterY;
                break;
        }

        y += feature.flattenHeightOffset;
        return y;
    }

    private static float EstimateMaxSlopeDegrees(
        float[,] heightMap,
        float voxelSize,
        float halfWidth,
        float halfLength,
        float centerX,
        float centerZ,
        float halfFootprintX,
        float halfFootprintZ,
        float centerY)
    {
        // Sample at 4 corners and compute max slope angle relative to center.
        float[] sx = new float[] { -halfFootprintX, halfFootprintX, -halfFootprintX, halfFootprintX };
        float[] sz = new float[] { -halfFootprintZ, -halfFootprintZ, halfFootprintZ, halfFootprintZ };

        float maxDeg = 0f;

        for (int i = 0; i < 4; i++)
        {
            float px = centerX + sx[i];
            float pz = centerZ + sz[i];
            float y = SampleHeight(heightMap, voxelSize, halfWidth, halfLength, px, pz);

            float dx = sx[i];
            float dz = sz[i];
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist <= 0.0001f)
                continue;

            float dy = Mathf.Abs(y - centerY);
            float deg = Mathf.Atan2(dy, dist) * Mathf.Rad2Deg;
            if (deg > maxDeg)
                maxDeg = deg;
        }

        return maxDeg;
    }

    /// <summary>
    /// Samples the height map using bilinear interpolation in index space.
    /// localX/localZ are in terrainParent local space (same as VoxelTerrainModule localX/localZ).
    /// </summary>
    private static float SampleHeight(
        float[,] heightMap,
        float voxelSize,
        float halfWidth,
        float halfLength,
        float localX,
        float localZ)
    {
        int sizeX = heightMap.GetLength(0);
        int sizeZ = heightMap.GetLength(1);

        float fx = (localX + halfWidth) / voxelSize;
        float fz = (localZ + halfLength) / voxelSize;

        // Clamp to valid range
        fx = Mathf.Clamp(fx, 0f, sizeX - 1.001f);
        fz = Mathf.Clamp(fz, 0f, sizeZ - 1.001f);

        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, sizeX - 1);
        int z1 = Mathf.Min(z0 + 1, sizeZ - 1);

        float tx = fx - x0;
        float tz = fz - z0;

        float a = heightMap[x0, z0];
        float b = heightMap[x1, z0];
        float c = heightMap[x0, z1];
        float d = heightMap[x1, z1];

        float ab = Mathf.Lerp(a, b, tx);
        float cd = Mathf.Lerp(c, d, tx);
        return Mathf.Lerp(ab, cd, tz);
    }
}
