using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves a UI root (Canvas) between Ship scene and the currently loaded stage scene.
/// This solves cross-scene reference issues and prevents the HUD from being disabled
/// when ShipEnvironmentRoot is turned off.
///
/// Attach this to your HUD Canvas root (or a top-level UIRoot).
///
/// Behaviour:
/// - On any non-ship scene loaded (additive), detach this UI root from its parent and move it to that scene.
/// - When that non-ship scene is unloaded, move UI back to the ship scene and re-parent to shipUIParent (if provided).
///
/// Notes:
/// - Unity does not allow cross-scene drag references in the editor. Use runtime binding for goals.
/// - This script does NOT use DontDestroyOnLoad: it simply migrates the UI GameObject to the target scene.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class UISceneCourier : MonoBehaviour
{
    [Header("Ship Scene")]
    [Tooltip("If empty, this will auto-detect as the scene this UI is in at Awake.")]
    public string shipSceneName;

    [Tooltip("Optional: where to parent the HUD when it is in the ship scene. If null, it will remember its original parent.")]
    public Transform shipUIParent;

    [Header("Stage Scene")]
    [Tooltip("Optional: parent to use when moved into a stage scene. Usually left null.")]
    public Transform stageUIParent;

    private Transform _originalParent;
    private int _originalSiblingIndex;
    private bool _initialized;

    private void Awake()
    {
        if (_initialized) return;
        _initialized = true;

        if (string.IsNullOrWhiteSpace(shipSceneName))
            shipSceneName = gameObject.scene.name;

        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();

        if (shipUIParent == null)
            shipUIParent = _originalParent;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid()) return;
        if (scene.name == shipSceneName) return;

        // A stage scene is loaded additively → move HUD there.
        MoveToScene(scene, stageUIParent);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (!scene.IsValid()) return;
        if (scene.name == shipSceneName) return;

        // A stage scene got unloaded → return HUD to ship if ship scene exists.
        var ship = FindLoadedSceneByName(shipSceneName);
        if (ship.IsValid())
            MoveToScene(ship, shipUIParent, restoreSiblingIndex: true);
    }

    private void MoveToScene(Scene target, Transform desiredParent, bool restoreSiblingIndex = false)
    {
        // Ensure we are not parented under a ship root that may be disabled.
        transform.SetParent(null, true);

        SceneManager.MoveGameObjectToScene(gameObject, target);

        if (desiredParent != null && desiredParent.gameObject.scene == target)
        {
            transform.SetParent(desiredParent, true);
            if (restoreSiblingIndex && desiredParent == _originalParent)
                transform.SetSiblingIndex(_originalSiblingIndex);
        }

        // If we were moved out of a disabled ship root, force-enable.
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    private static Scene FindLoadedSceneByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return default;

        int count = SceneManager.sceneCount;
        for (int i = 0; i < count; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.IsValid() && s.isLoaded && s.name == name)
                return s;
        }
        return default;
    }
}
