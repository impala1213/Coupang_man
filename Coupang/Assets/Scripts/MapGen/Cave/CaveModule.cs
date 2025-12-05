using UnityEngine;

public abstract class CaveModule : ScriptableObject
{
    public abstract void GenerateCaves(MapProfile profile, Rng rng, Transform parent);

    public virtual int GetLayerCount(MapProfile profile)
    {
        return 0;
    }

    public virtual Vector3 GetEntranceHint(MapProfile profile, Transform parent)
    {
        if (parent == null)
            return Vector3.zero;

        return parent.position;
    }

    public virtual Vector3 GetLayerEntranceHint(MapProfile profile, Transform parent, int layerIndex)
    {
        return GetEntranceHint(profile, parent);
    }
}
