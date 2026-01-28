using UnityEngine;

/// <summary>
/// Attach this to an authored cave entrance object in a prebuilt map.
///
/// At runtime, CaveSceneSpawner will generate the modular interior (using CaveDungeonGenerator)
/// and wire the CaveEntranceTeleport on this object to the interior entry point.
/// </summary>
[DisallowMultipleComponent]
public class CaveEntranceLink : MonoBehaviour
{
    [Header("Definition")]
    public CaveFeatureDefinition caveDefinition;

    [Header("Interior Placement")]
    [Tooltip("Optional explicit interior origin. If null, CaveSceneSpawner uses caveDefinition settings.")]
    public Transform interiorOrigin;

    [Tooltip("Optional override parent for the generated interior. If null, CaveSceneSpawner uses its configured cavesRoot.")]
    public Transform cavesRootOverride;

    [Header("Seed")]
    [Tooltip("If >= 0, overrides the generated seed for this entrance.")]
    public int seedOverride = -1;

    [Header("Runtime")]
    public CaveInstance generatedInstance;

    public bool HasGenerated => generatedInstance != null;
}
