using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    [Header("Scenes")]
    [SerializeField] private string shipSceneName = "Ship";
    [SerializeField] private string gameplaySceneName = "Gameplay";

    [Header("References")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform containerRoot;
    [SerializeField] private Animator containerDoorAnimator;
    [SerializeField] private Transform shipContainerAnchor;
    [SerializeField] private Transform shipEnvironmentRoot; // ship visuals root

    [Header("Planet")]
    [SerializeField] private PlanetDirectory currentPlanetDirectory;
    [SerializeField] private MapProfile defaultProfile;
    [SerializeField] private int defaultSeed = 12345;

    [Header("Cinematic")]
    [SerializeField] private float minCinematicSeconds = 5f;
    [SerializeField] private bool waitUntilWorldReady = true;

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
    private LandingHelper landingHelper;
    private Vector3 playerLocalOffsetFromContainer;
    private Vector3 initialContainerPositionOnShip;
    private Quaternion initialContainerRotationOnShip;

    private static readonly int DoorOpenHash = Animator.StringToHash("Open");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (playerRoot == null || containerRoot == null)
        {
            Debug.LogError("GameSession: playerRoot or containerRoot is not assigned.");
        }
        else
        {
            if (containerRoot.parent != null)
            {
                Debug.LogWarning(
                    $"GameSession: containerRoot had parent '{containerRoot.parent.name}'. " +
                    "Detaching it so only the container moves between scenes."
                );
                containerRoot.SetParent(null, true);
            }

            playerLocalOffsetFromContainer = playerRoot.position - containerRoot.position;
            initialContainerPositionOnShip = containerRoot.position;
            initialContainerRotationOnShip = containerRoot.rotation;
        }

        if (containerRoot != null)
        {
            Debug.Log($"[GameSession] containerRoot = {containerRoot.name}, parent = {(containerRoot.parent ? containerRoot.parent.name : "null")}, scene = {containerRoot.gameObject.scene.name}");
        }
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

        // ensure ship environment is visible on start
        SetShipEnvironmentVisible(true);
    }

    public void OnContainerLeverPulled()
    {
        Debug.Log("GameSession: Lever pulled");
        if (state == SessionState.LandingInProgress || state == SessionState.ReturningToShip)
        {
            return;
        }

        if (state == SessionState.OnShip)
        {
            StartCoroutine(StartLandingRoutine());
        }
        else if (state == SessionState.OnPlanet)
        {
            StartCoroutine(ReturnToShipRoutine());
        }
    }

    public void StartLandingForPlanet(PlanetDirectory directory)
    {
        currentPlanetDirectory = directory;
        if (state == SessionState.OnShip)
        {
            StartCoroutine(StartLandingRoutine());
        }
    }

    private IEnumerator StartLandingRoutine()
    {
        if (containerRoot == null || playerRoot == null)
        {
            Debug.LogError("GameSession: containerRoot or playerRoot is not assigned.");
            yield break;
        }

        state = SessionState.LandingInProgress;
        SetDoorOpen(false);

        MapProfile profile;
        int seed;
        SelectPlanet(out profile, out seed);

        if (profile == null)
        {
            Debug.LogError("GameSession: No MapProfile selected.");
            state = SessionState.OnShip;
            yield break;
        }

        if (!SceneManager.GetSceneByName(gameplaySceneName).IsValid())
        {
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Additive);
            while (!loadOp.isDone)
            {
                yield return null;
            }
        }

        gameplayScene = SceneManager.GetSceneByName(gameplaySceneName);
        if (!gameplayScene.IsValid())
        {
            Debug.LogError($"GameSession: Gameplay scene '{gameplaySceneName}' is not valid.");
            state = SessionState.OnShip;
            yield break;
        }

        FindLandingHelper();
        if (landingHelper == null)
        {
            Debug.LogError("GameSession: LandingHelper not found in Gameplay scene.");
            state = SessionState.OnShip;
            yield break;
        }

        landingHelper.Initialize(profile, seed);

        float elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            bool worldReady = !waitUntilWorldReady || (landingHelper != null && landingHelper.IsEssentialReady);
            if (elapsed >= minCinematicSeconds && worldReady)
            {
                break;
            }
            yield return null;
        }

        MoveContainerAndPlayerToGameplay();

        // hide ship environment while on planet
        SetShipEnvironmentVisible(false);

        SetDoorOpen(true);

        SceneManager.SetActiveScene(gameplayScene);
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
        SetDoorOpen(false);

        yield return new WaitForSeconds(0.5f);

        MoveContainerAndPlayerToShip(shipScene);

        if (gameplayScene.IsValid())
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(gameplayScene);
            while (!unloadOp.isDone)
            {
                yield return null;
            }
        }

        SceneManager.SetActiveScene(shipScene);
        landingHelper = null;
        gameplayScene = default;

        // show ship environment again
        SetShipEnvironmentVisible(true);

        SetDoorOpen(true);

        state = SessionState.OnShip;
    }

    private void SelectPlanet(out MapProfile profile, out int seed)
    {
        profile = null;
        seed = defaultSeed;

        if (currentPlanetDirectory != null)
        {
            profile = currentPlanetDirectory.GetRandomProfile(out seed);
        }

        if (profile == null)
        {
            profile = defaultProfile;
            seed = defaultSeed;
        }
    }

    private void FindLandingHelper()
    {
        landingHelper = null;

        if (gameplayScene.IsValid())
        {
            GameObject[] roots = gameplayScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                LandingHelper helper = roots[i].GetComponentInChildren<LandingHelper>(true);
                if (helper != null)
                {
                    landingHelper = helper;
                    return;
                }
            }
        }

        if (landingHelper == null)
        {
            landingHelper = Object.FindFirstObjectByType<LandingHelper>(FindObjectsInactive.Include);
        }

    }

    private void MoveContainerAndPlayerToGameplay()
    {
        if (!gameplayScene.IsValid())
        {
            Debug.LogError("GameSession: gameplayScene is not valid.");
            return;
        }

        Transform anchor = landingHelper != null ? landingHelper.PlanetContainerAnchor : null;
        if (anchor == null)
        {
            Debug.LogWarning("GameSession: PlanetContainerAnchor is not assigned. Using world origin.");
        }

        Vector3 targetContainerPosition = anchor != null ? anchor.position : Vector3.zero;
        MoveContainerAndPlayer(containerRoot, playerRoot, targetContainerPosition, gameplayScene);

        var autoParent = containerRoot.GetComponentInChildren<ContainerAutoParent>(true);
        if (autoParent != null)
        {
            autoParent.ResyncSceneItems();
        }
    }

    private void MoveContainerAndPlayerToShip(Scene shipScene)
    {
        if (!shipScene.IsValid())
        {
            Debug.LogError("GameSession: shipScene is not valid.");
            return;
        }

        Vector3 targetContainerPosition;

        if (shipContainerAnchor != null)
        {
            targetContainerPosition = shipContainerAnchor.position;
        }
        else
        {
            targetContainerPosition = initialContainerPositionOnShip;
        }

        MoveContainerAndPlayer(containerRoot, playerRoot, targetContainerPosition, shipScene);

        var autoParent = containerRoot.GetComponentInChildren<ContainerAutoParent>(true);
        if (autoParent != null && autoParent.gameObject.scene == shipScene)
        {
            autoParent.ResyncSceneItems();
        }
    }

    private void MoveContainerAndPlayer(Transform container, Transform player, Vector3 targetContainerPosition, Scene targetScene)
    {
        if (container == null || player == null)
        {
            Debug.LogError("GameSession: container or player is null.");
            return;
        }

        if (container == player)
        {
            Debug.LogError("GameSession: containerRoot and playerRoot cannot be the same object.");
            return;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = false;
        if (controller != null)
        {
            wasEnabled = controller.enabled;
            controller.enabled = false;
        }

        Vector3 playerOffset = playerLocalOffsetFromContainer;
        if (playerOffset == Vector3.zero)
        {
            playerOffset = player.position - container.position;
        }

        container.position = targetContainerPosition;
        player.position = container.position + playerOffset;

        SceneManager.MoveGameObjectToScene(container.gameObject, targetScene);
        SceneManager.MoveGameObjectToScene(player.gameObject, targetScene);

        if (controller != null)
        {
            controller.enabled = wasEnabled;
        }
    }

    private void SetDoorOpen(bool isOpen)
    {
        if (containerDoorAnimator == null)
        {
            return;
        }

        containerDoorAnimator.SetBool(DoorOpenHash, isOpen);
    }

    private void SetShipEnvironmentVisible(bool visible)
    {
        if (shipEnvironmentRoot != null)
        {
            shipEnvironmentRoot.gameObject.SetActive(visible);
        }
    }
}
