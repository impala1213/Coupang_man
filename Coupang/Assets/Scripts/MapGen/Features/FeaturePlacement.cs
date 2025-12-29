using UnityEngine;

/// <summary>
/// Runtime result of feature selection + placement.
/// Stored in terrainParent space (same space used by VoxelTerrainModule height calculations).
/// </summary>
[System.Serializable]
public struct FeaturePlacement
{
    public FeatureDefinition feature;

    /// <summary>Center position in terrainParent local space (Y = flatten target surface height).</summary>
    public Vector3 localPosition;

    /// <summary>Yaw rotation (around Y axis) in degrees for future use (prefab orientation).</summary>
    public float yawDegrees;

    /// <summary>Flatten target surface world Y (redundant but convenient).</summary>
    public float targetSurfaceWorldY;

    /// <summary>Reserved radius used to avoid overlapping placements.</summary>
    public float reservedRadius;

    public FeaturePlacement(FeatureDefinition feature, Vector3 localPos, float yawDeg, float targetY, float reservedRadius)
    {
        this.feature = feature;
        this.localPosition = localPos;
        this.yawDegrees = yawDeg;
        this.targetSurfaceWorldY = targetY;
        this.reservedRadius = reservedRadius;
    }
}
