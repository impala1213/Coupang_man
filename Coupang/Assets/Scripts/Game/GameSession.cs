using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    [Header("Scenes")]
    [SerializeField] private string shipSceneName = "Ship";
    [SerializeField] private string gameplaySceneName = "Gameplay"; // legacy fallback if PlanetCatalog is not used

    [Header("Planet Catalog (Multi-Scene)")]
    [Tooltip("Optional. If assigned, a planet entry will be randomly selected and its gameplaySceneName will be loaded.")]
    [SerializeField] private PlanetCatalog planetCatalog;

    [Tooltip("If true, the player must pick a planet on the ship monitor before launching.")]
    [SerializeField] private bool requirePlanetSelection = true;

    [Header("Delivery Contract (Optional)")]
    [Tooltip("Fallback contract directory when planet entry has none.")]
    [SerializeField] private DeliveryContractDirectory defaultContractDirectory;

    [Header("Delivery Drop Zone (Optional)")]
    [Tooltip("How many separate destination drop zones to spawn for the active contract. Prototype uses 3.")]
    [SerializeField] private int destinationSplitCount = 3;

    [Tooltip("Fallback drop zone prefab if the StageContext in the gameplay scene does not provide one.")]
    [SerializeField] private DeliveryDropZone defaultDropZonePrefab;

    [Tooltip("Fallback shield prefab if the StageContext in the gameplay scene does not provide one.")]
    [SerializeField] private GameObject defaultShieldPrefab;

    [Tooltip("Fallback radius for the shield (if not overridden by the drop zone prefab or StageContext).")]
    [SerializeField] private float defaultShieldRadius = 10f;

    [Header("Launch Rules")]
    [Tooltip("If true, the player must pick up at least one generated contract cargo item before launch.")]
    [SerializeField] private bool requireContractPickupToLaunch = true;


[Header("Transfer (Teleport)")]
[Tooltip("Small upward offset applied to cargo after scene transfer to prevent spawning slightly below the container floor.")]
[SerializeField] private float cargoTeleportYOffset = 0.05f;

    [Header("Drop Zone Placement (Fallback Random)")]
    [SerializeField] private float dropZoneMinDistanceFromLanding = 40f;
    [SerializeField] private float dropZoneMaxDistanceFromLanding = 90f;
    [SerializeField] private int dropZonePlacementAttempts = 24;
    [SerializeField] private float dropZoneRaycastAboveOffset = 120f;
    [SerializeField] private float dropZoneRaycastDistance = 400f;
    [SerializeField] private LayerMask dropZoneGroundMask = ~0;

    [Header("References (Ship)")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("Ship container placed in the Ship scene (not moved between scenes).")]
    [SerializeField] private StageContainer shipStageContainer;

    [Tooltip("Optional: ship environment root to hide while on planet.")]
    [SerializeField] private Transform shipEnvironmentRoot;

    [Tooltip("If true, disables the ship container object while on planet.")]
    [SerializeField] private bool hideShipContainerOnPlanet = true;

    [Tooltip("Optional: bootstrapper to refresh planet candidates on return.")]
    [SerializeField] private PlanetSelectionBootstrapper selectionBootstrapper;

    [Tooltip("Optional: ship UI root to hide while on planet.")]
    [SerializeField] private Transform shipUiRoot;

    [Tooltip("If true, hides ship UI while on planet.")]
    [SerializeField] private bool hideShipUiOnPlanet = false;

    [Header("Cinematic")]
    [SerializeField] private float minCinematicSeconds = 0f;

    private enum SessionState
    {
        OnShip,
        LandingInProgress,
        OnPlanet,
        ReturningToShip
    }

    private SessionState state = SessionState.OnShip;

    private Scene shipScene;
    private Scene gameplayScene;

    private PlanetCatalog.PlanetEntry activePlanet;
    private DeliveryContractDefinition activeContract;
    private DeliveryDropZone activeDropZone;
    private readonly List<DeliveryDropZone> activeDropZones = new List<DeliveryDropZone>();
    private readonly List<DestinationTerminalInteractable> activeDestinations = new List<DestinationTerminalInteractable>();

    // Cached stage context/container in the loaded gameplay scene.
    private StageContext activeStageContext;
    private StageContainer activeStageContainer;

    private Transform chosenDropZoneCandidate;

    private static readonly int DoorOpenHash = Animator.StringToHash("Open");

    private struct CargoRelPose
    {
        public Transform t;
        // Position/rotation relative to the container frame at snapshot time (scale-independent)
        public Vector3 relPos;
        public Quaternion relRot;
        // Preserve world scale across scenes (independent of parent scale)
        public Vector3 worldScale;
    }

    private struct TransferSnapshot
    {
        public bool valid;
        public int fromContainerId;
        public Vector3 playerRelPos;
        public Quaternion playerRelRot;
        public List<CargoRelPose> cargo;
    }

    private TransferSnapshot pendingSnapshot;
    private bool hasPickedContractCargo;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        shipScene = SceneManager.GetSceneByName(shipSceneName);

        if (playerRoot == null)
            Debug.LogError("GameSession: playerRoot is not assigned.");

        ResolveShipStageContainer();
    }

    private void Start()
    {
        shipScene = SceneManager.GetSceneByName(shipSceneName);

        if (!shipScene.IsValid())
        {
            Debug.LogWarning(
                $"GameSession: Ship scene '{shipSceneName}' is not loaded. " +
                "Make sure Ship is the first scene in build and is loaded at startup."
            );
        }

        SetShipEnvironmentVisible(true);
    }

    public void OnContainerLeverPulled()
    {
        if (state == SessionState.LandingInProgress || state == SessionState.ReturningToShip)
            return;

        if (state == SessionState.OnShip)
        {
            if (requirePlanetSelection && !PlanetSelectionState.HasSelection)
            {
                Debug.LogWarning("[GameSession] No planet selected. Open the monitor UI and confirm a destination first.");
                return;
            }

            ResolveShipStageContainer();

            // Resolve selected contract for launch checks.
            DeliveryContractDefinition launchContract = null;
            if (requirePlanetSelection && PlanetSelectionState.HasSelection)
            {
                int sel = PlanetSelectionState.GetSelectedIndex();
                var offer = PlanetSelectionState.GetCandidate(sel);
                launchContract = offer.contract;
            }

            if (requireContractPickupToLaunch && launchContract != null && !hasPickedContractCargo)
            {
                Debug.LogWarning("[GameSession] Pick up at least one contract cargo item before launch.");
                return;
            }

            if (shipStageContainer != null && shipScene.IsValid())
                pendingSnapshot = CaptureSnapshot(shipStageContainer, shipScene);

            StartCoroutine(StartLandingRoutine());
            return;
        }

        if (state == SessionState.OnPlanet)
        {
            // Capture snapshot at the exact lever time (player pose + cargo positions).
            if (activeStageContainer == null && gameplayScene.IsValid())
            {
                ResolveActiveStageContextAndContainer(gameplayScene);
            }

            if (activeStageContainer != null && gameplayScene.IsValid())
            {
                pendingSnapshot = CaptureSnapshot(activeStageContainer, gameplayScene);
            }

                        // Multi-destination payout summary (recorded per terminal completion).
            if (DestinationAssignmentState.HasAssignment)
            {
                Debug.Log(DestinationAssignmentState.BuildTripSummaryText());
            }

StartCoroutine(ReturnToShipRoutine());
        }
    }

    private IEnumerator StartLandingRoutine()
    {
        if (playerRoot == null)
        {
            Debug.LogError("GameSession: playerRoot is not assigned.");
            yield break;
        }

        ResolveShipStageContainer();
        if (shipStageContainer == null)
        {
            Debug.LogError("GameSession: shipStageContainer is not assigned and could not be resolved.");
            yield break;
        }

        state = SessionState.LandingInProgress;

        // Pick planet entry (scene + optional contract dir)
        // Prefer a pre-selected planet from the ship monitor.
        PlanetSelectionState.Offer selectedOffer;
        if (PlanetSelectionState.TryGetSelection(out selectedOffer) && selectedOffer.planet != null)
        {
            activePlanet = selectedOffer.planet;
            activeContract = selectedOffer.contract;
            PlanetSelectionState.RememberSelection(selectedOffer);
        }
        else
        {
            activePlanet = (planetCatalog != null) ? planetCatalog.GetRandomPlanet() : null;
            activeContract = null;
        }

        string targetGameplaySceneName =
            (activePlanet != null && !string.IsNullOrEmpty(activePlanet.gameplaySceneName))
                ? activePlanet.gameplaySceneName
                : gameplaySceneName;

        if (string.IsNullOrEmpty(targetGameplaySceneName))
        {
            Debug.LogError("GameSession: gameplay scene name is empty.");
            state = SessionState.OnShip;
            yield break;
        }

        // Pick contract (optional)
        if (activeContract == null)
            SelectContract();

        hasPickedContractCargo = false;

        // Load gameplay scene
        if (!SceneManager.GetSceneByName(targetGameplaySceneName).IsValid())
        {
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(targetGameplaySceneName, LoadSceneMode.Additive);
            while (!loadOp.isDone)
                yield return null;
        }

        gameplayScene = SceneManager.GetSceneByName(targetGameplaySceneName);
        if (!gameplayScene.IsValid())
        {
            Debug.LogError($"GameSession: Gameplay scene '{targetGameplaySceneName}' is not valid.");
            state = SessionState.OnShip;
            yield break;
        }

        // Find StageContext + StageContainer inside the gameplay scene.
        ResolveActiveStageContextAndContainer(gameplayScene);

        if (activeStageContainer == null)
        {
            Debug.LogError("GameSession: No StageContainer found in gameplay scene. Place a StageContainer on the map container.");
            state = SessionState.OnShip;
            yield break;
        }

        // (Optional) cinematic wait
        if (minCinematicSeconds > 0f)
            yield return new WaitForSeconds(minCinematicSeconds);
        else
            yield return null;

        // Transfer ONLY player + cargo (container object itself stays authored in each scene).
        // Uses lever-time snapshot if available.
        TransferPlayerAndCargo(
            shipStageContainer,
            activeStageContainer,
            shipScene,
            gameplayScene,
            openToContainer: true
        );

        // Make gameplay scene active early for spawns
        SceneManager.SetActiveScene(gameplayScene);

        // Spawn drop zone / goal (optional)
        SpawnDeliveryDropZone();

        // Per-planet spawns (items/monsters)
        SpawnStageSpawns();

        // Hide ship environment while on planet
        SetShipEnvironmentVisible(false);

        state = SessionState.OnPlanet;
    }

    private IEnumerator ReturnToShipRoutine()
    {
        shipScene = SceneManager.GetSceneByName(shipSceneName);
        if (!shipScene.IsValid())
        {
            Debug.LogError($"GameSession: Ship scene '{shipSceneName}' is not valid or not loaded.");
            yield break;
        }

        state = SessionState.ReturningToShip;

        yield return new WaitForSeconds(0.1f);

        ResolveShipStageContainer();

        if (activeStageContainer == null && gameplayScene.IsValid())
        {
            ResolveActiveStageContextAndContainer(gameplayScene);
        }

        if (shipStageContainer == null)
        {
            Debug.LogError("GameSession: shipStageContainer is missing on return.");
        }
        else if (activeStageContainer != null)
        {
            // Transfer ONLY player + cargo (container object stays).
            // Uses lever-time snapshot if available.
            TransferPlayerAndCargo(
                activeStageContainer,
                shipStageContainer,
                gameplayScene,
                shipScene,
                openToContainer: true
            );

            ApplyMissionCargoCleanupOnReturn();
        }

        // Move HUD/UI back to ship BEFORE unloading gameplay scene
        ForceReturnAllUiToShip();

        // Unload gameplay scene (everything left behind is destroyed, per design)
        if (gameplayScene.IsValid())
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(gameplayScene);
            while (!unloadOp.isDone)
                yield return null;
        }

        SceneManager.SetActiveScene(shipScene);
        gameplayScene = default;

        activeStageContext = null;
        activeStageContainer = null;
        activeDropZone = null;
        activeContract = null;
        activePlanet = null;
        chosenDropZoneCandidate = null;

        SetShipEnvironmentVisible(true);
        ForceShipUiVisible();

        state = SessionState.OnShip;

        RefreshShipPlanetCandidates();
    }

    private void SelectContract()
    {
        activeContract = null;

        DeliveryContractDirectory dir = null;
        if (activePlanet != null && activePlanet.contractDirectory != null)
            dir = activePlanet.contractDirectory;
        else
            dir = defaultContractDirectory;

        if (dir != null)
            activeContract = dir.GetRandomContract();
    }

    public void RegisterContractCargoPickup()
    {
        hasPickedContractCargo = true;
    }

    public void ResetContractCargoPickup()
    {
        hasPickedContractCargo = false;
    }

    private TransferSnapshot CaptureSnapshot(StageContainer fromContainer, Scene fromScene)
    {
        TransferSnapshot snap = new TransferSnapshot();
        snap.valid = false;

        if (fromContainer == null)
            return snap;

        fromContainer.ResolveDefaults();

        // Ensure container auto-parent has up-to-date parenting before we snapshot.
        ResyncContainerAutoParent(fromContainer);

        // Player pose relative to container root at lever time
        Transform fromFrame = fromContainer.containerRoot != null ? fromContainer.containerRoot : fromContainer.transform;
        if (playerRoot != null && fromFrame != null)
        {
            snap.playerRelPos = InverseTransformPointNoScale(fromFrame, playerRoot.position);
            snap.playerRelRot = Quaternion.Inverse(fromFrame.rotation) * playerRoot.rotation;
        }
        else
        {
            snap.playerRelPos = Vector3.zero;
            snap.playerRelRot = Quaternion.identity;
        }

                // Cargo poses at lever time (ONLY WorldItems).
                snap.cargo = new List<CargoRelPose>(64);
        
                var worldItemsInCargo = GatherCargoWorldItems(fromContainer, fromScene);
                if (worldItemsInCargo != null && worldItemsInCargo.Count > 0)
                {
                    var added = new HashSet<Transform>();
                    for (int i = 0; i < worldItemsInCargo.Count; i++)
                    {
                        var wi = worldItemsInCargo[i];
                        if (wi == null) continue;
        
                        Transform t = wi.transform;
                        if (!added.Add(t)) continue;
        
                        AddCargoPose(fromFrame, t, snap.cargo);
                    }
                }

snap.valid = true;
        snap.fromContainerId = fromContainer.GetInstanceID();
        return snap;
    }

    private void TransferPlayerAndCargo(
        StageContainer fromContainer,
        StageContainer toContainer,
        Scene fromScene,
        Scene toScene,
        bool openToContainer)
    {
        if (fromContainer == null || toContainer == null)
        {
            Debug.LogError("GameSession.TransferPlayerAndCargo: container is null.");
            return;
        }

        fromContainer.ResolveDefaults();
        toContainer.ResolveDefaults();

        // Consume pending snapshot if it matches this transfer direction.
        TransferSnapshot snap;
        if (pendingSnapshot.valid && pendingSnapshot.fromContainerId == fromContainer.GetInstanceID())
        {
            snap = pendingSnapshot;
        }
        else
        {
            snap = CaptureSnapshot(fromContainer, fromScene);
        }
        pendingSnapshot.valid = false;

        if (snap.cargo == null || snap.cargo.Count == 0)
        {
            snap = CaptureSnapshot(fromContainer, fromScene);
        }

        Transform toFrame = toContainer.containerRoot != null ? toContainer.containerRoot : toContainer.transform;

        // Disable CharacterController while teleporting
        CharacterController controller = (playerRoot != null) ? playerRoot.GetComponent<CharacterController>() : null;
        bool controllerWasEnabled = false;
        if (controller != null)
        {
            controllerWasEnabled = controller.enabled;
            controller.enabled = false;
        }

        // 1) Move cargo objects into target scene and re-parent under toContainer.cargoRoot using the saved local pose
        HashSet<Transform> movedCargo = null;
        if (snap.cargo != null)
        {
            for (int i = 0; i < snap.cargo.Count; i++)
            {
                Transform t = snap.cargo[i].t;
                if (t == null) continue;

                if (movedCargo == null)
                    movedCargo = new HashSet<Transform>();
                movedCargo.Add(t);

                t.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(t.gameObject, toScene);

                // Place using container frame (scale-independent), then parent under cargoRoot keeping world pose.
                if (toFrame != null)
                {
                    Vector3 worldPos = TransformPointNoScale(toFrame, snap.cargo[i].relPos);
                    // Nudge upward a bit to avoid spawning slightly below the container floor due to pivot/bounds differences.
                    if (cargoTeleportYOffset != 0f) worldPos += Vector3.up * cargoTeleportYOffset;
                    Quaternion worldRot = toFrame.rotation * snap.cargo[i].relRot;
                    t.SetPositionAndRotation(worldPos, worldRot);
                }

                if (toContainer.cargoRoot != null)
                {
                    t.SetParent(toContainer.cargoRoot, true);

                    // Preserve world scale across different parent scaling.
                    Vector3 parentScale = toContainer.cargoRoot.lossyScale;
                    Vector3 ws = snap.cargo[i].worldScale;

                    float sx = Mathf.Abs(parentScale.x) > 1e-6f ? ws.x / parentScale.x : 1f;
                    float sy = Mathf.Abs(parentScale.y) > 1e-6f ? ws.y / parentScale.y : 1f;
                    float sz = Mathf.Abs(parentScale.z) > 1e-6f ? ws.z / parentScale.z : 1f;

                    t.localScale = new Vector3(sx, sy, sz);

                    // Stabilize physics after teleport (prevents immediate "drop" from carried velocity)
                    var rb = t.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.Sleep();
                    }

                }
                else
                {
                    t.SetParent(null, true);
                }
            }
        }

        // 2) Move player into target scene and place preserving relative pose to container
        if (playerRoot != null)
        {
            playerRoot.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(playerRoot.gameObject, toScene);

            // ALWAYS preserve lever-time relative pose to container (per project rules)
            if (toFrame != null)
            {
                playerRoot.position = TransformPointNoScale(toFrame, snap.playerRelPos);
                playerRoot.rotation = toFrame.rotation * snap.playerRelRot;
            }
        }

        if (controller != null)
        {
            controller.enabled = controllerWasEnabled;
        }

        // Resync auto-parenting zones on both ends (optional)
        ResyncContainerAutoParent(fromContainer);
        ResyncContainerAutoParent(toContainer);

        // Optional door animation
        SetDoorOpen(fromContainer.doorAnimator, false);
        SetDoorOpen(toContainer.doorAnimator, openToContainer);
    }

    private void SpawnDeliveryDropZone()
    {
        if (!gameplayScene.IsValid()) return;

        // Prefab selection priority: StageContext -> GameSession fallback
        DeliveryDropZone prefab = null;
        if (activeStageContext != null && activeStageContext.dropZonePrefab != null)
            prefab = activeStageContext.dropZonePrefab;
        else
            prefab = defaultDropZonePrefab;

        if (prefab == null)
        {
            Debug.LogWarning("[GameSession] No DeliveryDropZone prefab assigned (StageContext or GameSession). Skipping.");
            return;
        }

        // Multi-destination prototype: split the contract into N destinations (default 3).
        int destCount = Mathf.Max(1, destinationSplitCount);
        if (activeStageContext != null && activeStageContext.destinationCountOverride > 0)
            destCount = Mathf.Max(1, activeStageContext.destinationCountOverride);

        activeDropZones.Clear();
        activeDestinations.Clear();
        activeDropZone = null;

        if (destCount > 1)
        {
            SpawnMultiDestinationDropZones(destCount, prefab);
            return;
        }

        // 1) Candidate-based selection (random)
        chosenDropZoneCandidate = PickRandomDropZoneCandidate();
        if (chosenDropZoneCandidate != null)
        {
            // If the candidate already has a DropZone component, reuse it.
            var existing = chosenDropZoneCandidate.GetComponentInChildren<DeliveryDropZone>(true);
            if (existing != null && existing.gameObject.scene == gameplayScene)
            {
                ConfigureDropZone(existing, bindRadarGoal: true);
                return;
            }

            CreateDropZoneAt(chosenDropZoneCandidate.position, chosenDropZoneCandidate.rotation, prefab, bindRadarGoal: true);
            return;
        }

        // 2) Legacy single-anchor
        if (activeStageContext != null && activeStageContext.dropZoneAnchor != null)
        {
            CreateDropZoneAt(activeStageContext.dropZoneAnchor.position, activeStageContext.dropZoneAnchor.rotation, prefab, bindRadarGoal: true);
            return;
        }

        // 3) Fallback random placement around the stage container.
        Transform containerFrame = (activeStageContainer != null && activeStageContainer.containerRoot != null)
            ? activeStageContainer.containerRoot
            : null;

        if (containerFrame == null)
        {
            Debug.LogWarning("[GameSession] Cannot spawn DeliveryDropZone because stage container is missing.");
            return;
        }

        // StageContext overrides (if present) -> GameSession fallback
        float minDist = (activeStageContext != null && activeStageContext.dropZoneMinDistanceFromLanding > 0f)
            ? activeStageContext.dropZoneMinDistanceFromLanding
            : dropZoneMinDistanceFromLanding;

        float maxDist = (activeStageContext != null && activeStageContext.dropZoneMaxDistanceFromLanding > 0f)
            ? activeStageContext.dropZoneMaxDistanceFromLanding
            : dropZoneMaxDistanceFromLanding;

        int attempts = (activeStageContext != null && activeStageContext.dropZonePlacementAttempts > 0)
            ? activeStageContext.dropZonePlacementAttempts
            : dropZonePlacementAttempts;

        float above = (activeStageContext != null && activeStageContext.dropZoneRaycastAboveOffset > 0f)
            ? activeStageContext.dropZoneRaycastAboveOffset
            : dropZoneRaycastAboveOffset;

        float dist = (activeStageContext != null && activeStageContext.dropZoneRaycastDistance > 0f)
            ? activeStageContext.dropZoneRaycastDistance
            : dropZoneRaycastDistance;

        LayerMask groundMask = (activeStageContext != null && activeStageContext.dropZoneGroundMask.value != 0)
            ? activeStageContext.dropZoneGroundMask
            : dropZoneGroundMask;

        Vector3 spawnPos;
        Quaternion spawnRot;
        if (!TryFindDropZonePose(
                containerFrame.position,
                minDist,
                maxDist,
                attempts,
                above,
                dist,
                groundMask,
                out spawnPos,
                out spawnRot))
        {
            spawnPos = containerFrame.position + containerFrame.forward * minDist;
            spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(containerFrame.forward, Vector3.up), Vector3.up);
        }

        CreateDropZoneAt(spawnPos, spawnRot, prefab, bindRadarGoal: true);
    }

    /// <summary>
    /// Prototype: Spawn multiple destination drop zones and split the active contract into per-destination
    /// category quotas. Each destination has a DestinationTerminalInteractable under its DropZone that
    /// will pay up to its quota (most expensive items first) and then spawn a shield.
    /// </summary>
    private void SpawnMultiDestinationDropZones(int destCount, DeliveryDropZone prefab)
    {
        destCount = Mathf.Max(1, destCount);
        if (destCount <= 1 || prefab == null) return;

        // Use the pre-split assignment prepared on the ship monitor.
        // Fallback: if not prepared (or mismatched count), prepare now (will randomize).
        if (!DestinationAssignmentState.Matches(activeContract, destCount))
        {
            DestinationAssignmentState.Prepare(activeContract, destCount);
        }

        // Candidate anchors (if authored)
        List<Transform> candidates = null;
        if (activeStageContext != null && activeStageContext.dropZoneCandidates != null && activeStageContext.dropZoneCandidates.Length > 0)
        {
            for (int i = 0; i < activeStageContext.dropZoneCandidates.Length; i++)
            {
                var t = activeStageContext.dropZoneCandidates[i];
                if (t == null) continue;
                if (t.gameObject.scene != gameplayScene) continue;

                if (candidates == null) candidates = new List<Transform>(activeStageContext.dropZoneCandidates.Length);
                candidates.Add(t);
            }

            // Shuffle so we get random unique candidates without replacement.
            if (candidates != null && candidates.Count > 1)
            {
                for (int i = candidates.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
            }
        }

        Transform containerFrame = (activeStageContainer != null && activeStageContainer.containerRoot != null)
            ? activeStageContainer.containerRoot
            : (activeStageContainer != null ? activeStageContainer.transform : null);

        if (containerFrame == null)
        {
            Debug.LogWarning("[GameSession] Multi-destination spawn: activeStageContainer missing; using playerRoot as center.");
            containerFrame = (playerRoot != null) ? playerRoot.transform : null;
        }

        // StageContext overrides (if present) -> GameSession fallback (same as single DZ)
        float minDist = (activeStageContext != null && activeStageContext.dropZoneMinDistanceFromLanding > 0f)
            ? activeStageContext.dropZoneMinDistanceFromLanding
            : dropZoneMinDistanceFromLanding;

        float maxDist = (activeStageContext != null && activeStageContext.dropZoneMaxDistanceFromLanding > 0f)
            ? activeStageContext.dropZoneMaxDistanceFromLanding
            : dropZoneMaxDistanceFromLanding;

        int attempts = (activeStageContext != null && activeStageContext.dropZonePlacementAttempts > 0)
            ? activeStageContext.dropZonePlacementAttempts
            : dropZonePlacementAttempts;

        float above = (activeStageContext != null && activeStageContext.dropZoneRaycastAboveOffset > 0f)
            ? activeStageContext.dropZoneRaycastAboveOffset
            : dropZoneRaycastAboveOffset;

        float dist = (activeStageContext != null && activeStageContext.dropZoneRaycastDistance > 0f)
            ? activeStageContext.dropZoneRaycastDistance
            : dropZoneRaycastDistance;

        LayerMask groundMask = (activeStageContext != null && activeStageContext.dropZoneGroundMask.value != 0)
            ? activeStageContext.dropZoneGroundMask
            : dropZoneGroundMask;

        // Spawn N drop zones
        for (int i = 0; i < destCount; i++)
        {
            // 1) Decide pose
            Vector3 spawnPos;
            Quaternion spawnRot;

            bool gotPose = false;

            // Candidate anchor per destination (if available)
            if (candidates != null && i < candidates.Count && candidates[i] != null)
            {
                var t = candidates[i];
                spawnPos = t.position;
                spawnRot = t.rotation;

                // Snap to ground under the candidate
                Vector3 origin = spawnPos + Vector3.up * above;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore))
                {
                    spawnPos = hit.point;
                    // keep candidate rotation (designer intent)
                }

                gotPose = true;
            }
            else
            {
                gotPose = TryFindDropZonePose(
                    containerFrame.position,
                    minDist,
                    maxDist,
                    attempts,
                    above,
                    dist,
                    groundMask,
                    out spawnPos,
                    out spawnRot);

                if (!gotPose)
                {
                    spawnPos = containerFrame.position + containerFrame.forward * minDist;
                    spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(containerFrame.forward, Vector3.up), Vector3.up);
                    gotPose = true;
                }
            }

            // 2) Instantiate
            var contractPart = DestinationAssignmentState.GetPartialContract(i);
            if (contractPart == null)
                contractPart = activeContract;

            var dz = Instantiate(prefab, spawnPos, spawnRot);
            dz.name = $"{prefab.name}_Dest{i + 1}";
            SceneManager.MoveGameObjectToScene(dz.gameObject, gameplayScene);

            // Configure using the per-destination partial contract
            ConfigureDropZone(dz, bindRadarGoal: (i == 0), contractOverride: contractPart);

            // Configure the terminal UI assignment
            var term = dz.GetComponentInChildren<DestinationTerminalInteractable>(true);
            if (term != null)
            {
                term.dropZone = dz;
                term.ConfigureAssignment(contractPart, i);
                if (!activeDestinations.Contains(term))
                    activeDestinations.Add(term);
            }
            else
            {
                Debug.LogWarning($"[GameSession] Multi-destination DropZone '{dz.name}' has no DestinationTerminalInteractable.");
            }

            if (activeDropZone == null)
                activeDropZone = dz;

            if (!activeDropZones.Contains(dz))
                activeDropZones.Add(dz);
        }

        // Let the registry pick the first goal target if needed.
        DestinationGoalRegistry.NotifyDestinationsSpawned();
    }



    private Transform PickRandomDropZoneCandidate()
    {
        if (activeStageContext == null) return null;
        if (activeStageContext.dropZoneCandidates == null || activeStageContext.dropZoneCandidates.Length == 0)
            return null;

        // Filter only non-null candidates in this scene
        List<Transform> valid = null;
        for (int i = 0; i < activeStageContext.dropZoneCandidates.Length; i++)
        {
            var t = activeStageContext.dropZoneCandidates[i];
            if (t == null) continue;
            if (t.gameObject.scene != gameplayScene) continue;

            if (valid == null) valid = new List<Transform>(activeStageContext.dropZoneCandidates.Length);
            valid.Add(t);
        }

        if (valid == null || valid.Count == 0)
            return null;

        return valid[Random.Range(0, valid.Count)];
    }

    private DeliveryDropZone CreateDropZoneAt(Vector3 pos, Quaternion rot, DeliveryDropZone prefab, bool bindRadarGoal)
    {
        var dz = Instantiate(prefab, pos, rot);
        SceneManager.MoveGameObjectToScene(dz.gameObject, gameplayScene);
        ConfigureDropZone(dz, bindRadarGoal);
        return dz;
    }

    private void ConfigureDropZone(DeliveryDropZone dz, bool bindRadarGoal, DeliveryContractDefinition contractOverride = null)
    {
        if (dz == null) return;

        // Shield prefab
        GameObject shieldPrefab = null;
        if (activeStageContext != null && activeStageContext.shieldPrefab != null)
            shieldPrefab = activeStageContext.shieldPrefab;
        else
            shieldPrefab = defaultShieldPrefab;

        // Wallet
        PlayerWallet wallet = null;
        if (playerRoot != null)
            wallet = playerRoot.GetComponentInChildren<PlayerWallet>(true);

        // Shield radius
        float radius = (activeStageContext != null && activeStageContext.shieldRadiusOverride > 0f)
            ? activeStageContext.shieldRadiusOverride
            : defaultShieldRadius;

        // Configure DropZone with the active contract (mission payout = sum(baseValue)).
        var contractToUse = contractOverride != null ? contractOverride : activeContract;
        dz.Configure(contractToUse, wallet, shieldPrefab, radius);

        // Bind contract to activation buttons (optional)
        var buttons = dz.GetComponentsInChildren<DeliveryActivationButton>(true);
        if (buttons != null)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].BindContract(contractToUse);
            }
        }

        if (activeDropZone == null)
            activeDropZone = dz;

        if (!activeDropZones.Contains(dz))
            activeDropZones.Add(dz);

        // Bind radar goal
        if (bindRadarGoal)
        {
            var radar = (playerRoot != null) ? playerRoot.GetComponentInChildren<PlayerRadarUI>(true) : null;
        if (radar != null)
        {
            radar.goalTarget = dz.transform;
            radar.ForceRefresh();
        }
        }
    }

    private void SpawnStageSpawns()
    {
        if (!gameplayScene.IsValid()) return;
        if (activeStageContext == null) return;
        if (activeStageContainer == null) return;

        Transform containerFrame = (activeStageContainer.containerRoot != null) ? activeStageContainer.containerRoot : activeStageContainer.transform;
        Vector3 center = containerFrame.position;
        Vector3 forward = containerFrame.forward;

        float minR = Mathf.Max(0f, activeStageContext.spawnMinDistanceFromContainer);
        float maxR = Mathf.Max(minR + 1f, activeStageContext.spawnMaxDistanceFromContainer);
        float minFromDZ = Mathf.Max(0f, activeStageContext.spawnMinDistanceFromDropZone);
        int attempts = Mathf.Max(1, activeStageContext.spawnPlacementAttempts);
        float above = Mathf.Max(1f, activeStageContext.spawnRaycastAboveOffset);
        float dist = Mathf.Max(1f, activeStageContext.spawnRaycastDistance);
        LayerMask groundMask = (activeStageContext.spawnGroundMask.value != 0) ? activeStageContext.spawnGroundMask : (LayerMask)(-1);

        Vector3 dzPos = (activeDropZone != null) ? activeDropZone.transform.position : Vector3.positiveInfinity;

        SpawnEntries(activeStageContext.itemSpawns, activeStageContext.spawnParent, center, forward, dzPos, minFromDZ, minR, maxR, attempts, above, dist, groundMask);

        if (activeStageContext.spawnMonstersAroundDestinations && activeDropZones != null && activeDropZones.Count > 0)
        {
            float mMinR = Mathf.Max(0f, activeStageContext.monsterSpawnMinDistanceFromDestination);
            float mMaxR = Mathf.Max(mMinR + 1f, activeStageContext.monsterSpawnMaxDistanceFromDestination);
            float minSep = Mathf.Max(0f, activeStageContext.monsterSpawnMinSeparation);
            float minFromOther = Mathf.Max(0f, activeStageContext.monsterSpawnMinDistanceFromOtherDestinations);
            int mAttempts = (activeStageContext.monsterSpawnPlacementAttempts > 0) ? activeStageContext.monsterSpawnPlacementAttempts : attempts;

            SpawnEntriesAroundDestinations(activeStageContext.monsterSpawns, activeStageContext.spawnParent, activeDropZones, forward, mMinR, mMaxR, minSep, minFromOther, mAttempts, above, dist, groundMask);
        }
        else
        {
            SpawnEntries(activeStageContext.monsterSpawns, activeStageContext.spawnParent, center, forward, dzPos, minFromDZ, minR, maxR, attempts, above, dist, groundMask);
        }
    }

    private void SpawnEntries(
        StageContext.SpawnEntry[] entries,
        Transform parent,
        Vector3 center,
        Vector3 fallbackForward,
        Vector3 dropZonePos,
        float minDistFromDropZone,
        float minR,
        float maxR,
        int attempts,
        float raycastAbove,
        float raycastDist,
        LayerMask groundMask)
    {
        if (entries == null || entries.Length == 0)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || e.prefab == null) continue;

            int minC = Mathf.Max(0, e.minCount);
            int maxC = Mathf.Max(minC, e.maxCount);
            int count = (maxC == minC) ? minC : Random.Range(minC, maxC + 1);

            for (int k = 0; k < count; k++)
            {
                if (!TryFindSpawnPose(center, dropZonePos, minDistFromDropZone, minR, maxR, attempts, raycastAbove, raycastDist, groundMask, out Vector3 pos, out Vector3 normal))
                {
                    // Fallback near the edge
                    pos = center + fallbackForward * minR;
                    normal = Vector3.up;
                }

                Quaternion rot = BuildSpawnRotation(e, normal, fallbackForward);

                GameObject go = Instantiate(e.prefab, pos, rot);
                SceneManager.MoveGameObjectToScene(go, gameplayScene);

                if (parent != null)
                    go.transform.SetParent(parent, true);
            }
        }
    }


    private void SpawnEntriesAroundDestinations(
        StageContext.SpawnEntry[] entries,
        Transform parent,
        List<DeliveryDropZone> destinations,
        Vector3 fallbackForward,
        float minR,
        float maxR,
        float minSeparation,
        float minDistFromOtherDestinations,
        int attempts,
        float raycastAbove,
        float raycastDist,
        LayerMask groundMask)
    {
        if (entries == null || entries.Length == 0)
            return;

        // Collect destination centers in this gameplay scene.
        List<Vector3> destCenters = null;
        if (destinations != null)
        {
            for (int i = 0; i < destinations.Count; i++)
            {
                var dz = destinations[i];
                if (dz == null) continue;
                if (dz.gameObject.scene != gameplayScene) continue;
                if (destCenters == null) destCenters = new List<Vector3>(destinations.Count);
                destCenters.Add(dz.transform.position);
            }
        }

        if (destCenters == null || destCenters.Count == 0)
            return;

        float minSepSq = minSeparation * minSeparation;
        int[] spawnedPerDest = new int[destCenters.Count];
        List<Vector3> occupied = (minSeparation > 0f) ? new List<Vector3>(64) : null;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || e.prefab == null) continue;

            int minC = Mathf.Max(0, e.minCount);
            int maxC = Mathf.Max(minC, e.maxCount);
            int count = (maxC == minC) ? minC : Random.Range(minC, maxC + 1);

            for (int k = 0; k < count; k++)
            {
                int destIndex = PickLeastUsedDestination(spawnedPerDest);

                if (!TryFindSpawnPoseAroundDestination(destCenters, destIndex, minR, maxR, minSepSq, occupied, minDistFromOtherDestinations,
                    attempts, raycastAbove, raycastDist, groundMask, out Vector3 pos, out Vector3 normal))
                {
                    // Fallback: place near the destination edge.
                    Vector3 c = destCenters[destIndex];
                    pos = c + fallbackForward * minR;
                    normal = Vector3.up;
                }

                if (occupied != null)
                    occupied.Add(pos);

                spawnedPerDest[destIndex]++;

                Quaternion rot = BuildSpawnRotation(e, normal, fallbackForward);

                GameObject go = Instantiate(e.prefab, pos, rot);
                SceneManager.MoveGameObjectToScene(go, gameplayScene);

                if (parent != null)
                    go.transform.SetParent(parent, true);
            }
        }
    }

    private static int PickLeastUsedDestination(int[] counts)
    {
        if (counts == null || counts.Length == 0) return 0;

        int best = 0;
        int bestCount = counts[0];
        int ties = 1;

        for (int i = 1; i < counts.Length; i++)
        {
            int c = counts[i];
            if (c < bestCount)
            {
                best = i;
                bestCount = c;
                ties = 1;
            }
            else if (c == bestCount)
            {
                ties++;
                if (Random.Range(0, ties) == 0)
                    best = i;
            }
        }

        return best;
    }

    private bool TryFindSpawnPoseAroundDestination(
        List<Vector3> destinationCenters,
        int destinationIndex,
        float minDist,
        float maxDist,
        float minSeparationSq,
        List<Vector3> occupiedPositions,
        float minDistFromOtherDestinations,
        int attempts,
        float raycastAbove,
        float raycastDist,
        LayerMask groundMask,
        out Vector3 pos,
        out Vector3 normal)
    {
        pos = default;
        normal = Vector3.up;

        if (destinationCenters == null || destinationCenters.Count == 0)
            return false;

        destinationIndex = Mathf.Clamp(destinationIndex, 0, destinationCenters.Count - 1);
        Vector3 centerPos = destinationCenters[destinationIndex];

        float otherMinSq = (minDistFromOtherDestinations > 0f) ? (minDistFromOtherDestinations * minDistFromOtherDestinations) : 0f;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 dir2 = RandomUnitCircleDirection();
            float d = Random.Range(minDist, maxDist);
            Vector3 candidateXZ = centerPos + new Vector3(dir2.x, 0f, dir2.y) * d;

            // Optional: avoid spawning too close to other destinations
            if (otherMinSq > 0f && destinationCenters.Count > 1)
            {
                bool tooClose = false;
                for (int j = 0; j < destinationCenters.Count; j++)
                {
                    if (j == destinationIndex) continue;
                    if ((new Vector2(candidateXZ.x, candidateXZ.z) - new Vector2(destinationCenters[j].x, destinationCenters[j].z)).sqrMagnitude < otherMinSq)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;
            }

            // Optional: avoid overlapping other spawned monsters
            if (occupiedPositions != null && minSeparationSq > 0f)
            {
                bool overlap = false;
                for (int j = 0; j < occupiedPositions.Count; j++)
                {
                    if ((new Vector2(candidateXZ.x, candidateXZ.z) - new Vector2(occupiedPositions[j].x, occupiedPositions[j].z)).sqrMagnitude < minSeparationSq)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (overlap) continue;
            }

            Vector3 origin = candidateXZ + Vector3.up * raycastAbove;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDist, groundMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
                normal = hit.normal;
                return true;
            }
        }

        return false;
    }


    private static Vector2 RandomUnitCircleDirection()
    {
        Vector2 dir = Random.insideUnitCircle;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.up;
        return dir.normalized;
    }

    private Quaternion BuildSpawnRotation(StageContext.SpawnEntry e, Vector3 groundNormal, Vector3 fallbackForward)
    {
        Vector3 up = e.alignToGroundNormal ? groundNormal : Vector3.up;

        Vector3 fwd;
        if (e.randomYaw)
        {
            Vector2 d = RandomUnitCircleDirection();
            fwd = new Vector3(d.x, 0f, d.y);
        }
        else
        {
            fwd = fallbackForward;
        }

        fwd = Vector3.ProjectOnPlane(fwd, up);
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.ProjectOnPlane(Vector3.forward, up);

        return Quaternion.LookRotation(fwd, up);
    }

    private bool TryFindSpawnPose(
        Vector3 centerPos,
        Vector3 dropZonePos,
        float minDistFromDropZone,
        float minDist,
        float maxDist,
        int attempts,
        float raycastAbove,
        float raycastDist,
        LayerMask groundMask,
        out Vector3 pos,
        out Vector3 normal)
    {
        pos = default;
        normal = Vector3.up;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 dir = RandomUnitCircleDirection();

            float d = Random.Range(minDist, maxDist);
            Vector3 candidateXZ = centerPos + new Vector3(dir.x, 0f, dir.y) * d;

            if (minDistFromDropZone > 0f && dropZonePos.x < 1e20f)
            {
                float sq = (candidateXZ - dropZonePos).sqrMagnitude;
                if (sq < minDistFromDropZone * minDistFromDropZone)
                    continue;
            }

            Vector3 origin = candidateXZ + Vector3.up * raycastAbove;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDist, groundMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
                normal = hit.normal;
                return true;
            }
        }

        return false;
    }

    private bool TryFindDropZonePose(
        Vector3 centerPos,
        float minDist,
        float maxDist,
        int attempts,
        float raycastAbove,
        float raycastDist,
        LayerMask groundMask,
        out Vector3 pos,
        out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;

        for (int i = 0; i < Mathf.Max(1, attempts); i++)
        {
            Vector2 dir = RandomUnitCircleDirection();

            float d = Random.Range(minDist, Mathf.Max(minDist + 1f, maxDist));
            Vector3 candidateXZ = centerPos + new Vector3(dir.x, 0f, dir.y) * d;
            Vector3 origin = candidateXZ + Vector3.up * raycastAbove;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDist, groundMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;

                Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;

                rot = Quaternion.LookRotation(forward, hit.normal);
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────
    // Mission rules
    // ─────────────────────────────────────────────

    private bool HasAllRequiredCargoInContainer(StageContainer container, Scene containerScene, DeliveryContractDefinition contract, out string missingReport)
    {
        missingReport = string.Empty;

        if (contract == null || contract.requiredItems == null || contract.requiredItems.Length == 0)
            return true;

        if (container == null)
        {
            missingReport = "[GameSession] Cannot launch: shipStageContainer is missing.";
            return false;
        }

        container.ResolveDefaults();
        ResyncContainerAutoParent(container);

        Transform cargoRoot = container.cargoRoot != null ? container.cargoRoot : container.transform;

        // Required totals
        Dictionary<ItemDefinition, int> required = new Dictionary<ItemDefinition, int>();
        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var r = contract.requiredItems[i];
            if (r == null || r.item == null) continue;

            int qty = Mathf.Max(1, r.requiredQty);
            if (required.ContainsKey(r.item))
                required[r.item] += qty;
            else
                required.Add(r.item, qty);
        }

        if (required.Count == 0)
            return true;

        // Found totals inside container
        Dictionary<ItemDefinition, int> found = new Dictionary<ItemDefinition, int>();
        var items = cargoRoot.GetComponentsInChildren<WorldItem>(true);
        for (int i = 0; i < items.Length; i++)
        {
            var wi = items[i];
            if (wi == null || wi.definition == null) continue;
            if (wi.gameObject.scene != containerScene) continue;

            // Count top-level cargo only (avoid counting nested parts)
            if (HasWorldItemAncestor(wi.transform, cargoRoot))
                continue;

            int stack = Mathf.Max(1, wi.stackCount);

            if (found.ContainsKey(wi.definition))
                found[wi.definition] += stack;
            else
                found.Add(wi.definition, stack);
        }

        bool ok = true;
        StringBuilder sb = new StringBuilder();
        foreach (var kv in required)
        {
            var def = kv.Key;
            int need = Mathf.Max(0, kv.Value);
            int got = (def != null && found.TryGetValue(def, out int v)) ? Mathf.Max(0, v) : 0;

            if (got < need)
            {
                ok = false;
                sb.AppendLine($"- {GetDefLabel(def)}: {got}/{need}");
            }
        }

        if (!ok)
        {
            missingReport =
                "[GameSession] Cannot launch: mission cargo is not fully inside the container.\n" +
                "Put ALL mission cargo under ShipStageContainer.cargoRoot before pulling the lever.\n" +
                "Missing:\n" + sb.ToString();
        }

        return ok;
    }

    private static string GetDefLabel(ItemDefinition def)
    {
        if (def == null) return "(null)";
        if (!string.IsNullOrEmpty(def.displayName)) return def.displayName;
        return def.name;
    }

    private HashSet<ItemDefinition> BuildRequiredDefSet(DeliveryContractDefinition contract)
    {
        if (contract == null || contract.requiredItems == null || contract.requiredItems.Length == 0)
            return null;

        var set = new HashSet<ItemDefinition>();
        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var r = contract.requiredItems[i];
            if (r == null || r.item == null) continue;
            set.Add(r.item);
        }

        return set.Count > 0 ? set : null;
    }

    /// <summary>
    /// Called when returning to Ship:
    /// - Charges disposal fee for mission cargo that is still INSIDE shipStageContainer.cargoRoot
    /// - Deletes mission cargo from ship container, player inventory, and any WorldItems attached to the player (hand/carrier)
    /// </summary>
    private void ApplyMissionCargoCleanupOnReturn()
    {
        if (playerRoot == null) return;
        if (shipStageContainer == null) return;
        if (activeContract == null) return;

        var requiredSet = BuildRequiredDefSet(activeContract);
        if (requiredSet == null || requiredSet.Count == 0)
            return;

        // Wallet (optional)
        var wallet = playerRoot.GetComponentInChildren<PlayerWallet>(true);

        // Ensure cargo parenting is up-to-date (important for correct disposal counting)
        shipStageContainer.ResolveDefaults();
        ResyncContainerAutoParent(shipStageContainer);

        // 1) Disposal fee + delete cargo left in ship container
        int disposalFee = 0;
        List<WorldItem> destroyFromContainer = new List<WorldItem>();

        Transform cargoRoot = shipStageContainer.cargoRoot != null ? shipStageContainer.cargoRoot : shipStageContainer.transform;
        var cargoItems = cargoRoot.GetComponentsInChildren<WorldItem>(true);
        for (int i = 0; i < cargoItems.Length; i++)
        {
            var wi = cargoItems[i];
            if (wi == null || wi.definition == null) continue;

            if (HasWorldItemAncestor(wi.transform, cargoRoot))
                continue;

            if (!requiredSet.Contains(wi.definition))
                continue;

            int stack = Mathf.Max(1, wi.stackCount);
            int baseValue = Mathf.Max(0, wi.definition.baseValue);
            int perUnitFee = Mathf.RoundToInt(baseValue * 0.30f);

            disposalFee += perUnitFee * stack;
            destroyFromContainer.Add(wi);
        }

        if (wallet != null && disposalFee > 0)
            wallet.AddMoney(-disposalFee);

        for (int i = 0; i < destroyFromContainer.Count; i++)
        {
            if (destroyFromContainer[i] != null)
                Destroy(destroyFromContainer[i].gameObject);
        }

        // 2) Purge mission cargo from inventory (no fee)
        var inv = playerRoot.GetComponentInChildren<InventorySystem>(true);
        PurgeInventoryByDefinitions(inv, requiredSet);

        // 3) Purge any mission WorldItems attached to the player (hand/carrier) (no fee)
        //    (These are not under shipStageContainer.cargoRoot, so no disposal fee applies.)
        var attached = playerRoot.GetComponentsInChildren<WorldItem>(true);
        for (int i = 0; i < attached.Length; i++)
        {
            var wi = attached[i];
            if (wi == null || wi.definition == null) continue;
            if (!requiredSet.Contains(wi.definition)) continue;

            // Avoid double-delete if something is still parented under the ship container root
            if (cargoRoot != null && wi.transform.IsChildOf(cargoRoot))
                continue;

            Destroy(wi.gameObject);
        }
    }

    private static void PurgeInventoryByDefinitions(InventorySystem inv, HashSet<ItemDefinition> defs)
    {
        if (inv == null || defs == null || defs.Count == 0) return;
        if (inv.slots == null || inv.slots.Count == 0) return;

        bool changed = false;

        for (int i = 0; i < inv.slots.Count; i++)
        {
            var slot = inv.slots[i];
            if (slot == null || slot.stack == null || slot.stack.def == null) continue;

            if (defs.Contains(slot.stack.def))
            {
                slot.stack = null;
                changed = true;
            }
        }

        if (changed)
        {
            // Triggers RefreshHeldVisual via internal NotifyChanged().
            inv.SetActiveIndex(inv.activeIndex);
        }
    }

    private static bool HasWorldItemAncestor(Transform t, Transform stopAt)
    {
        if (t == null) return false;
        Transform p = t.parent;
        while (p != null && p != stopAt)
        {
            if (p.GetComponent<WorldItem>() != null)
                return true;
            p = p.parent;
        }
        return false;
    }

    private List<WorldItem> GatherCargoWorldItems(StageContainer container, Scene scene)
    {
        if (container == null || !scene.IsValid())
            return null;

        container.ResolveDefaults();

        var results = new List<WorldItem>(64);
        var seen = new HashSet<WorldItem>();

        // Primary path: items under cargoRoot (authored container content).
        if (container.cargoRoot != null)
        {
            var worldItems = container.cargoRoot.GetComponentsInChildren<WorldItem>(true);
            for (int i = 0; i < worldItems.Length; i++)
            {
                var wi = worldItems[i];
                if (wi == null) continue;
                if (wi.gameObject.scene != scene) continue;
                // Exclude anything currently owned by the player (held visuals, carried items, carrier-mounted cargo).
                if (playerRoot != null && wi.transform.IsChildOf(playerRoot)) continue;
                if (wi.isOnCarrier || wi.carrierOwner != null) continue;
                // Fallback: child of a CarrierController (but not the carrier object itself)
                var ownerCarrier = wi.GetComponentInParent<CarrierController>();
                if (ownerCarrier != null && wi.GetComponent<CarrierController>() == null) continue;
                if (HasWorldItemAncestor(wi.transform, container.cargoRoot))
                    continue;

                if (seen.Add(wi))
                    results.Add(wi);
            }
        }

        // Fallback: include any world items inside the ContainerAutoParent volume (even if not parented yet).
        var autoParent = container.GetComponentInChildren<ContainerAutoParent>(true);
        var zoneCollider = (autoParent != null) ? autoParent.GetComponent<Collider>() : null;
        if (autoParent != null && zoneCollider != null)
        {
            Bounds bounds = zoneCollider.bounds;
            var allWorldItems = FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allWorldItems.Length; i++)
            {
                var wi = allWorldItems[i];
                if (wi == null) continue;
                if (wi.gameObject.scene != scene) continue;
                if (((1 << wi.gameObject.layer) & autoParent.worldItemLayers) == 0) continue;
                if (!bounds.Contains(wi.transform.position)) continue;
                if (autoParent != null && wi.ignoreContainerAutoParent)
                    continue;

                // Exclude anything currently owned by the player (held visuals, carried items, carrier-mounted cargo).
                if (playerRoot != null && wi.transform.IsChildOf(playerRoot))
                    continue;
                if (wi.isOnCarrier || wi.carrierOwner != null)
                    continue;
                var ownerCarrier = wi.GetComponentInParent<CarrierController>();
                if (ownerCarrier != null && wi.GetComponent<CarrierController>() == null)
                    continue;

                if (seen.Add(wi))
                    results.Add(wi);
            }
        }

        return results;
    }

    
    // ─────────────────────────────────────────────
    // Transform helpers (ignore scale)
    // ─────────────────────────────────────────────
    private static Vector3 InverseTransformPointNoScale(Transform frame, Vector3 worldPoint)
    {
        // Equivalent to frame.InverseTransformPoint, but ignores frame scale.
        return Quaternion.Inverse(frame.rotation) * (worldPoint - frame.position);
    }

    private static Vector3 TransformPointNoScale(Transform frame, Vector3 localPoint)
    {
        // Equivalent to frame.TransformPoint, but ignores frame scale.
        return frame.position + frame.rotation * localPoint;
    }

private static List<Transform> GatherCargoRootChildren(Transform cargoRoot, Scene scene)
    {
        var results = new List<Transform>();
        if (cargoRoot == null || !scene.IsValid())
            return results;

        for (int i = 0; i < cargoRoot.childCount; i++)
        {
            Transform child = cargoRoot.GetChild(i);
            if (child == null) continue;
            if (child.gameObject.scene != scene) continue;
            results.Add(child);
        }

        return results;
    }

    private static void AddCargoPose(Transform frame, Transform target, List<CargoRelPose> cargo)
    {
        CargoRelPose p = new CargoRelPose();
        p.t = target;
        // Store pose relative to the container frame, ignoring frame scale (prevents "weird offsets" when
        // ship/planet container prefabs have different scaling).
        p.relPos = InverseTransformPointNoScale(frame, target.position);
        p.relRot = Quaternion.Inverse(frame.rotation) * target.rotation;
        p.worldScale = target.lossyScale;
        cargo.Add(p);
    }

    private void ResolveShipStageContainer()
    {
        if (shipStageContainer != null)
        {
            shipStageContainer.ResolveDefaults();
            return;
        }

        // Try to find one in the ship scene
        var all = FindObjectsByType<StageContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.scene.name == shipSceneName)
            {
                shipStageContainer = all[i];
                shipStageContainer.ResolveDefaults();
                return;
            }
        }
    }

    private StageContext FindStageContextInScene(Scene scene)
    {
        if (!scene.IsValid())
            return null;

        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            var ctx = roots[i].GetComponentInChildren<StageContext>(true);
            if (ctx != null)
                return ctx;
        }

        // Fallback
        var any = FindFirstObjectByType<StageContext>(FindObjectsInactive.Include);
        if (any != null && any.gameObject.scene == scene)
            return any;

        return null;
    }

    private void ResolveActiveStageContextAndContainer(Scene scene)
    {
        activeStageContext = FindStageContextInScene(scene);
        activeStageContainer = ResolveStageContainerFromContext(activeStageContext, scene);
    }

    private StageContainer ResolveStageContainerFromContext(StageContext ctx, Scene scene)
    {
        if (ctx != null && ctx.stageContainer != null)
        {
            ctx.stageContainer.ResolveDefaults();
            return ctx.stageContainer;
        }

        // Try to find exactly one StageContainer in this scene
        var all = FindObjectsByType<StageContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        StageContainer found = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (all[i].gameObject.scene != scene) continue;

            if (found != null)
            {
                Debug.LogWarning("GameSession: Multiple StageContainers found in gameplay scene. Assign StageContext.stageContainer explicitly.");
                return found;
            }
            found = all[i];
        }

        if (found != null)
            found.ResolveDefaults();

        return found;
    }

    private void ResyncContainerAutoParent(StageContainer container)
    {
        if (container == null) return;

        var autoParent = container.GetComponentInChildren<ContainerAutoParent>(true);
        if (autoParent != null)
            autoParent.ResyncSceneItems();
    }

    
    private void ForceReturnAllUiToShip()
    {
        // Ensure any HUD/UI roots that were moved into the gameplay scene are moved back
        // BEFORE the gameplay scene is unloaded (otherwise Unity destroys them).
        var couriers = FindObjectsByType<UISceneCourier>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < couriers.Length; i++)
        {
            if (couriers[i] == null) continue;
            couriers[i].ForceMoveToShip();
        }
    }

private void SetDoorOpen(Animator animator, bool isOpen)
    {
        if (animator == null) return;
        animator.SetBool(DoorOpenHash, isOpen);
    }

    private void SetShipEnvironmentVisible(bool visible)
    {
        if (shipEnvironmentRoot != null)
            shipEnvironmentRoot.gameObject.SetActive(visible);

        if (hideShipContainerOnPlanet && shipStageContainer != null)
            shipStageContainer.gameObject.SetActive(visible);

        if (hideShipUiOnPlanet)
        {
            if (shipUiRoot != null)
                shipUiRoot.gameObject.SetActive(visible);
            else
                SetShipUiBySceneCanvases(visible);
        }
    }

    private void SetShipUiBySceneCanvases(bool visible)
    {
        if (!shipScene.IsValid()) return;
        var roots = shipScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            var canvases = roots[i].GetComponentsInChildren<Canvas>(true);
            for (int k = 0; k < canvases.Length; k++)
            {
                if (canvases[k] != null)
                    canvases[k].gameObject.SetActive(visible);
            }
        }
    }

    private void ForceShipUiVisible()
    {
        if (shipUiRoot != null)
            shipUiRoot.gameObject.SetActive(true);
        else
            SetShipUiBySceneCanvases(true);
    }

    private void RefreshShipPlanetCandidates()
    {
        PlanetSelectionState.Clear();

        if (selectionBootstrapper == null)
        {
            selectionBootstrapper = FindFirstObjectByType<PlanetSelectionBootstrapper>();
        }

        if (selectionBootstrapper != null)
            selectionBootstrapper.EnsureCandidates();
    }
}
