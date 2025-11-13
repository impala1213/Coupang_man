using UnityEngine;

public abstract class TerrainModule : ScriptableObject
{
    public abstract void GenerateTerrain(MapProfile profile, Rng rng, Transform parent);

    public virtual Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        return parent.position;
    }
}
