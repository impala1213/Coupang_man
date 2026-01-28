using UnityEngine;

/// <summary>
/// FeatureDefinition specialization for modular cave dungeons.
/// 
/// This feature is planned like any other feature (footprint/flatten), but its content is generated
/// as a separate interior dungeon and accessed via an interactable teleport entrance.
/// </summary>
[CreateAssetMenu(menuName = "Map/Caves/Cave Definition", fileName = "CaveDefinition")]
public class CaveFeatureDefinition : ScriptableObject
{
    [System.Serializable]
    public struct WeightedPrefab
    {
        public GameObject prefab;
        [Min(0f)] public float weight;
    }

    public enum InteriorPlacementMode
    {
        /// <summary>
        /// Interior origin is placed at (terrainParent.position + interiorBaseOffset + instanceIndex*spacing).
        /// Use this when you want a fixed "underground area" regardless of feature position.
        /// </summary>
        ManualOffset = 0,

        /// <summary>
        /// Interior origin is placed below the surface at the feature placement position.
        /// (surfaceY - interiorDepthBelowSurface) plus interiorPlanarOffset and instance spacing.
        /// </summary>
        BelowSurface = 1,
    }

    [Header("Entrance")]
    [Tooltip("Prefab to spawn on the surface. Must have a collider for interaction. If it does not contain CaveEntranceTeleport, one will be added at runtime.")]
    public GameObject entrancePrefab;

    [Tooltip("Prefab to spawn inside the cave near the entry as the return portal. If null, entrancePrefab will be reused.")]
    public GameObject exitPrefab;

    [Header("Start Piece")]
    [Tooltip("If true and exitPrefab is assigned, the dungeon generation will start from an instance of exitPrefab instead of a random room. The exit prefab should include CaveConnector children and optionally an EntryPoint child.")]
    public bool useExitPrefabAsStartPiece = true;

    [Tooltip("Offset applied when spawning the outside entrance (terrainParent space).")]
    public Vector3 entranceOffset = Vector3.zero;

    [Header("Interior Placement")]
    [Tooltip("How to place the interior dungeon in world space.")]
    public InteriorPlacementMode interiorPlacementMode = InteriorPlacementMode.BelowSurface;

    [Tooltip("Base planar offset applied to the computed interior origin (world space). Useful to move interiors away from the planet center line.")]
    public Vector3 interiorPlanarOffset = Vector3.zero;

    [Tooltip("Only used when interiorPlacementMode = BelowSurface. Interior Y will be (surfaceY - interiorDepthBelowSurface).")]
    public float interiorDepthBelowSurface = 220f;

    [Tooltip("Only used when interiorPlacementMode = ManualOffset. Base offset from terrainParent.position where cave interiors are spawned (world space).")]
    public Vector3 interiorBaseOffset = new Vector3(0f, -250f, 0f);

    [Tooltip("Spacing between multiple cave interiors to avoid overlap (world units). Applied along world +X.")]
    public float interiorInstanceSpacing = 220f;

    [Header("Modular Generation - Topology")]
    [Min(1)]
    [Tooltip("Maximum number of room pieces placed (including the start room, and terminal rooms).")]
    public int maxRooms = 12;

    [Range(0f, 1f)]
    [Tooltip("Probability that an available ROOM connector starts a corridor chain. Otherwise it is capped.")]
    public float roomToCorridorProbability = 0.65f;

    [Range(0f, 1f)]
    [Tooltip("Probability that an available CORRIDOR connector continues with another corridor segment (while under maxCorridorChain).")]
    public float corridorContinueProbability = 0.35f;

    [Range(0f, 1f)]
    [Tooltip("Probability that an available CORRIDOR connector ends by spawning a normal room (if not continuing a corridor). Remaining probability becomes an end (terminal/cap).")]
    public float corridorToRoomProbability = 0.75f;

    [Min(0)]
    [Tooltip("Maximum number of consecutive corridor segments between rooms. 0 means corridors never chain (room -> corridor -> room/end).")]
    public int maxCorridorChain = 2;

    [Header("Terminal Rooms (End Rooms)")]
    [Tooltip("If true, when a branch would end (cap), try to place a terminal room instead.")]
    public bool useTerminalRooms = true;

    [Range(0f, 1f)]
    [Tooltip("When a branch would end (cap), chance to try placing a terminal room first. If placement fails, it falls back to cap.")]
    public float corridorToTerminalRoomProbability = 1f;

    [Tooltip("Terminal room prefabs used at the end of cave branches. Recommended: exactly 1 connector (in).")]
    public WeightedPrefab[] terminalRooms;

    [Tooltip("If true, any remaining unused connectors will be capped at the end (unless allowOpenEnd is true on the connector).")]
    public bool capUnusedConnectors = true;

    [Header("Generation - Collision / Loops")]
    [Tooltip("If true, the generator rejects placements that would overlap previously placed pieces.")]
    public bool preventOverlaps = true;

    [Min(0f)]
    [Tooltip("Extra padding (world units) applied to each piece bounds when checking overlap. Higher = more conservative.")]
    public float overlapPadding = 0.25f;

    [Min(0f)]
    [Tooltip("A small shrink applied to bounds before overlap testing to allow pieces to touch at connection faces.")]
    public float touchEpsilon = 0.03f;

    [Min(0f)]
    [Tooltip("Reject placements where an unused connector ends up too close to an existing connector (helps prevent accidental \"meeting\"). 0 disables.")]
    public float minConnectorSeparation = 0.5f;

    [Min(1)]
    [Tooltip("How many different prefab attempts to try per connector before giving up and ending.")]
    public int placementRetriesPerConnector = 6;

    [Header("Prefabs")]
    [Tooltip("Room prefabs. Should contain CaveConnector children.")]
    public WeightedPrefab[] rooms;

    [Tooltip("Corridor prefabs. Can contain 2+ CaveConnector children (junctions supported).")]
    public WeightedPrefab[] corridors;

    [Tooltip("Cap (wall) prefabs. Should contain 1 CaveConnector child.")]
    public WeightedPrefab[] caps;
}
