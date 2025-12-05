using UnityEngine;

public abstract class TerrainModule : ScriptableObject
{
    public abstract void GenerateTerrain(MapProfile profile, Rng rng, Transform parent);

    public virtual Vector3 GetLandingHint(MapProfile profile, Transform parent)
    {
        return parent.position;
    }

    public virtual bool TryGetCaveEntranceHint(MapProfile profile, Transform parent, out Vector3 pos)
    {
        pos = Vector3.zero;
        return false;
    }
}
