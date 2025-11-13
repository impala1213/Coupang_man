using UnityEngine;

public class MapRunner : MonoBehaviour
{
    public Transform terrainParent;
    public TerrainModule defaultTerrainModule;
    public bool overrideProfile;
    public MapProfile profileOverride;

    public TerrainModule LastModuleUsed { get; private set; }
    public MapProfile LastProfileUsed { get; private set; }

    public void Run(MapProfile profile, int seed)
    {
        if (terrainParent == null)
            terrainParent = transform;

        MapProfile effectiveProfile = profile;

        if (overrideProfile && profileOverride != null)
        {
            effectiveProfile = profileOverride;
        }

        if (effectiveProfile == null)
        {
            Debug.LogError("MapRunner: effective profile is null.");
            return;
        }

        TerrainModule module = effectiveProfile.terrainModule != null
            ? effectiveProfile.terrainModule
            : defaultTerrainModule;

        if (module == null)
        {
            Debug.LogError("MapRunner: no TerrainModule assigned.");
            return;
        }

        LastModuleUsed = module;
        LastProfileUsed = effectiveProfile;

        Rng rng = new Rng(seed);
        module.GenerateTerrain(effectiveProfile, rng, terrainParent);
    }
}
