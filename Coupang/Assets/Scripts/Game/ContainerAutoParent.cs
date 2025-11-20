// Assets/Scripts/Game/ContainerAutoParent.cs
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class ContainerAutoParent : MonoBehaviour
{
    [Header("Container")]
    public Transform containerRoot;   // GameSession.containerRoot
    public Transform outsideParent;   // ShipEnvironmentRoot or similar

    [Header("Filter")]
    public LayerMask worldItemLayers = ~0;

    private Collider zoneCollider;

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null && !zoneCollider.isTrigger)
        {
            zoneCollider.isTrigger = true;
        }

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
    /// - Cargos mounted on a Carrier (isOnCarrier / under CarrierController)
    ///   are always ignored and keep the Carrier as parent.
    /// - Equipped carriers (on the player) are ignored.
    /// - All other WorldItems are parented as Container / Ship / Scene root.
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

        if (wi.transform.parent == containerRoot)
        {
            ParentToOutside(wi.transform, gameObject.scene);
        }
    }

    /// <summary>
    /// Decide whether ContainerAutoParent should NOT touch this WorldItem.
    /// - Mounted cargos on a carrier: always true.
    /// - Carriers equipped on player (ignoreContainerAutoParent && isCarrier): true.
    /// </summary>
    private bool ShouldIgnore(WorldItem wi)
    {
        // 1) Mounted on a carrier: always keep Carrier as parent.
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
        // Explicit mounted state
        if (wi.isOnCarrier) return true;
        if (wi.carrierOwner != null) return true;

        // Fallback: child of a CarrierController, but not the carrier itself
        var ownerCarrier = wi.GetComponentInParent<CarrierController>();
        var selfCarrier = wi.GetComponent<CarrierController>();

        if (ownerCarrier != null && selfCarrier == null)
            return true;

        return false;
    }

    void ParentToContainer(Transform itemTransform)
    {
        if (containerRoot == null) return;
        itemTransform.SetParent(containerRoot, true);
    }

    void ParentToOutside(Transform itemTransform, Scene currentScene)
    {
        // In Ship scene, use outsideParent (ShipRoot) as parent
        if (outsideParent != null && outsideParent.gameObject.scene == currentScene)
        {
            itemTransform.SetParent(outsideParent, true);
        }
        else
        {
            // In other scenes, detach to scene root (planet items, etc.)
            itemTransform.SetParent(null, true);
        }
    }
}
