// Assets/Scripts/Game/ContainerAutoParent.cs
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class ContainerAutoParent : MonoBehaviour
{
    [Header("Container")]
    [Tooltip("Root transform used for container-local items (in gameplay scene). " +
             "Only items under this root or under outsideParent will be managed.")]
    public Transform containerRoot;   // e.g., GameSession.containerRoot

    [Tooltip("Root transform used for items in ship environment (e.g., ShipEnvironmentRoot). " +
             "Only items under this root or under containerRoot will be managed.")]
    public Transform outsideParent;   // ShipEnvironmentRoot or similar

    [Header("Filter")]
    [Tooltip("Which layers are treated as world items for parenting.")]
    public LayerMask worldItemLayers = ~0;

    private Collider zoneCollider;

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null && !zoneCollider.isTrigger)
        {
            zoneCollider.isTrigger = true;
        }

        // Default containerRoot = parent of this zone if not set in inspector
        if (containerRoot == null && transform.parent != null)
        {
            containerRoot = transform.parent;
        }
    }

    void Start()
    {
        ResyncSceneItems();
    }

    /// <summary>
    /// Re-scan all WorldItems in this scene and parent them to containerRoot
    /// if inside the trigger volume, otherwise to outsideParent / scene root.
    /// 
    /// IMPORTANT:
    /// - Only items already under containerRoot or outsideParent are managed.
    ///   Anything under other roots (e.g., WorldRoot on a planet) is ignored.
    /// - Cargos mounted on a Carrier (isOnCarrier / under CarrierController)
    ///   are always ignored and keep the Carrier as parent.
    /// - Carriers equipped on player (ignoreContainerAutoParent && isCarrier): ignored.
    /// </summary>
    public void ResyncSceneItems()
    {
        if (zoneCollider == null) return;

        Scene zoneScene = gameObject.scene;
        Bounds bounds = zoneCollider.bounds;

        WorldItem[] allWorldItems = UnityEngine.Object.FindObjectsByType<WorldItem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var wi in allWorldItems)
        {
            if (wi == null) continue;
            if (wi.gameObject.scene != zoneScene) continue;
            if (((1 << wi.gameObject.layer) & worldItemLayers) == 0) continue;

            if (ShouldIgnore(wi))
                continue;

            // NEW: Only manage items that are already under containerRoot or outsideParent.
            if (!IsManagedScope(wi.transform))
                continue;

            Transform t = wi.transform;
            Vector3 pos = t.position;

            if (bounds.Contains(pos))
            {
                ParentToContainer(t);
            }
            else
            {
                ParentToOutside(t, zoneScene);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & worldItemLayers) == 0)
            return;

        WorldItem wi = other.GetComponentInParent<WorldItem>();
        if (wi == null) return;
        if (ShouldIgnore(wi))
            return;

        // NEW: Only manage items that belong to this container/ship hierarchy.
        if (!IsManagedScope(wi.transform))
            return;

        ParentToContainer(wi.transform);
    }

    void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & worldItemLayers) == 0)
            return;

        WorldItem wi = other.GetComponentInParent<WorldItem>();
        if (wi == null) return;
        if (ShouldIgnore(wi))
            return;

        // NEW: Only manage items that belong to this container/ship hierarchy.
        if (!IsManagedScope(wi.transform))
            return;

        if (wi.transform.parent == containerRoot)
        {
            ParentToOutside(wi.transform, gameObject.scene);
        }
    }

    /// <summary>
    /// Returns true if this item is considered "managed" by this auto-parent zone:
    /// - Either it is under containerRoot, or
    /// - It is under outsideParent.
    /// Any items under other roots (e.g., WorldRoot on a planet) will be ignored.
    /// </summary>
    private bool IsManagedScope(Transform itemTransform)
    {
        if (containerRoot != null && itemTransform.IsChildOf(containerRoot))
            return true;

        if (outsideParent != null && itemTransform.IsChildOf(outsideParent))
            return true;

        return false;
    }

    /// <summary>
    /// Decide whether ContainerAutoParent should NOT touch this WorldItem.
    /// - Mounted cargos on a carrier: always true.
    /// - Carriers equipped on player (ignoreContainerAutoParent && isCarrier): true.
    /// - (You can later extend this to ignore any item with ignoreContainerAutoParent == true if desired.)
    /// </summary>
    private bool ShouldIgnore(WorldItem wi)
    {
        // 1) Mounted on a carrier: always keep carrier as parent.
        if (IsMountedOnCarrier(wi))
            return true;

        // 2) Equipped carrier on player: do not re-parent.
        bool isEquippedCarrier =
            wi.definition != null &&
            wi.definition.isCarrier &&
            wi.ignoreContainerAutoParent;

        if (isEquippedCarrier)
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if this WorldItem is currently mounted on a carrier.
    /// i.e., it should always use the carrier as its parent, not container/ship/player.
    /// </summary>
    private bool IsMountedOnCarrier(WorldItem wi)
    {
        // Explicit mounted state.
        if (wi.isOnCarrier) return true;
        if (wi.carrierOwner != null) return true;

        // Fallback: child of a CarrierController, but not the carrier itself.
        var ownerCarrier = wi.GetComponentInParent<CarrierController>();
        var selfCarrier = wi.GetComponent<CarrierController>();

        if (ownerCarrier != null && selfCarrier == null)
            return true;

        return false;
    }

    private void ParentToContainer(Transform itemTransform)
    {
        if (containerRoot == null) return;
        itemTransform.SetParent(containerRoot, true);
    }

    private void ParentToOutside(Transform itemTransform, Scene currentScene)
    {
        // In ship scene, use outsideParent (ShipRoot) as parent.
        if (outsideParent != null && outsideParent.gameObject.scene == currentScene)
        {
            itemTransform.SetParent(outsideParent, true);
        }
        else
        {
            // In other scenes, detach to scene root (planet items, etc.).
            itemTransform.SetParent(null, true);
        }
    }
}
