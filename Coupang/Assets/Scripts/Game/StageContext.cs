using UnityEngine;
using System;

/// <summary>
/// Optional per-gameplay-scene configuration for authored (prebuilt) maps.
/// Put this in each gameplay(planet) scene so GameSession can:
/// - Find the stage container placed in the map (no container root moving between scenes)
/// - Pick a DropZone goal from candidate markers (random)
/// - Spawn per-planet items/monsters on ground outside the container
/// - Override delivery prefabs / shield settings
/// </summary>
[DisallowMultipleComponent]
public class StageContext : MonoBehaviour
{
    [Header("Stage Container (Placed In Scene)")]
    [Tooltip("Reference to the container that exists in this authored map scene.")]
    public StageContainer stageContainer;

    [Tooltip("Legacy: optional explicit player spawn anchor. (Not used when lever-time relative spawn is enabled.)")]
    public Transform playerSpawnAnchor;

    [Header("Drop Zone Candidates (Goal)")]
    [Tooltip("If > 0, overrides GameSession destinationSplitCount for this planet (how many separate destinations must be visited).")]
    public int destinationCountOverride = 0;

    [Tooltip("Place multiple candidate transforms in the scene. GameSession will pick one randomly and spawn the DropZone there. The chosen DropZone becomes the radar goal.")]
    public Transform[] dropZoneCandidates;

    [Tooltip("Legacy single-anchor. Used only if no candidates are provided.")]
    public Transform dropZoneAnchor;

    [Header("Delivery Prefabs (Optional Overrides)")]
    public DeliveryDropZone dropZonePrefab;
    public GameObject shieldPrefab;

    [Header("Shield (Optional Overrides)")]
    [Tooltip("If > 0, overrides the shield radius used when spawning the shield.")]
    public float shieldRadiusOverride = 0f;

    [Header("Drop Zone Placement (Fallback Random)")]
    [Tooltip("Minimum horizontal distance from the stage container (used only when no anchors/candidates exist).")]
    public float dropZoneMinDistanceFromLanding = 40f;

    [Tooltip("Maximum horizontal distance from the stage container (used only when no anchors/candidates exist).")]
    public float dropZoneMaxDistanceFromLanding = 90f;

    [Tooltip("How many random placements to try before falling back (used only when no anchors/candidates exist).")]
    public int dropZonePlacementAttempts = 24;

    [Header("Drop Zone Raycast (Fallback Random)")]
    public float dropZoneRaycastAboveOffset = 120f;
    public float dropZoneRaycastDistance = 400f;

    [Tooltip("If zero, GameSession will use its own ground mask.")]
    public LayerMask dropZoneGroundMask;

    [Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;

        [Min(0)] public int minCount = 0;
        [Min(0)] public int maxCount = 0;

        [Tooltip("Align prefab up to the ground normal.")]
        public bool alignToGroundNormal = true;

        [Tooltip("Randomize yaw around up axis.")]
        public bool randomYaw = true;
    }

    [Header("Planet Spawns (Optional)")]
    [Tooltip("Spawned on landing (after player/cargo transfer).")]
    public SpawnEntry[] itemSpawns;

    [Tooltip("Spawned on landing (after player/cargo transfer).")]
    public SpawnEntry[] monsterSpawns;

    [Tooltip("Optional parent for spawned objects (items/monsters).")]
    public Transform spawnParent;

    [Header("Spawn Area (Around Container)")]
    [Tooltip("Minimum horizontal distance from the stage container.")]
    public float spawnMinDistanceFromContainer = 35f;

    [Tooltip("Maximum horizontal distance from the stage container.")]
    public float spawnMaxDistanceFromContainer = 140f;

    [Tooltip("Minimum distance from the chosen DropZone (0 disables).")]
    public float spawnMinDistanceFromDropZone = 15f;

    [Tooltip("How many random placements to try per spawned object.")]
    public int spawnPlacementAttempts = 24;

    [Header("Monster Spawn (Around Destinations)")]
    [Tooltip("If true, monsterSpawns will be placed around each destination DropZone instead of around the container.")]
    public bool spawnMonstersAroundDestinations = true;

    [Tooltip("Minimum horizontal distance from a destination DropZone when spawning monsters.")]
    public float monsterSpawnMinDistanceFromDestination = 20f;

    [Tooltip("Maximum horizontal distance from a destination DropZone when spawning monsters.")]
    public float monsterSpawnMaxDistanceFromDestination = 80f;

    [Tooltip("Minimum separation between spawned monsters (to avoid heavy overlaps).")]
    public float monsterSpawnMinSeparation = 8f;

    [Tooltip("Optional: Minimum distance from OTHER destinations (0 disables).")]
    public float monsterSpawnMinDistanceFromOtherDestinations = 0f;

    [Tooltip("How many random placements to try per spawned monster (0 uses spawnPlacementAttempts).")]
    public int monsterSpawnPlacementAttempts = 0;


    [Header("Spawn Raycast")]
    public float spawnRaycastAboveOffset = 150f;
    public float spawnRaycastDistance = 600f;

    [Tooltip("If zero, GameSession will use ~0.")]
    public LayerMask spawnGroundMask;

    private void Awake()
    {
        if (!stageContainer)
            stageContainer = GetComponentInChildren<StageContainer>(true);
    }
}
