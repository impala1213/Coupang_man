using UnityEngine;

public class MapRunner : MonoBehaviour
{
    [Header("Terrain Setup")]
    [Tooltip("Parent transform under which terrain chunks / meshes will be generated.")]
    public Transform terrainParent;

    [Tooltip("Default terrain module used when the MapProfile has no terrainModule assigned.")]
    public TerrainModule defaultTerrainModule;

    [Header("Profile Override")]
    [Tooltip("If true, always use profileOverride instead of the provided profile in Run().")]
    public bool overrideProfile;

    [Tooltip("Profile used when overrideProfile is true.")]
    public MapProfile profileOverride;

    [Header("Runtime State (read-only)")]
    [Tooltip("Terrain module that was actually used in the last Run().")]
    public TerrainModule LastModuleUsed { get; private set; }

    [Tooltip("Map profile that was actually used in the last Run().")]
    public MapProfile LastProfileUsed { get; private set; }

    [Tooltip("Seed value that was used to generate the last terrain.")]
    public int LastSeedUsed { get; private set; }

    /// <summary>
    /// Generates terrain using the given profile and seed.
    /// The seed value is stored and logged for debugging / reproduction.
    /// </summary>
    /// <param name="profile">Profile passed in by caller (may be overridden by profileOverride).</param>
    /// <param name="seed">Seed used to initialize the terrain RNG.</param>
    public void Run(MapProfile profile, int seed)
    {
        if (terrainParent == null)
            terrainParent = transform;

        // Decide which profile to actually use.
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

        // Choose terrain module: profile override > default.
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
        LastSeedUsed = seed;

        // Log seed each time we generate terrain so it is easy to reproduce a planet.
        Debug.Log($"MapRunner.Run: Generating terrain with seed {seed} (Profile='{effectiveProfile.name}', Module='{module.name}')");

        Rng rng = new Rng(seed);
        module.GenerateTerrain(effectiveProfile, rng, terrainParent);
    }
}
