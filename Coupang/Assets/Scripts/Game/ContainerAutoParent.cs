// Assets/Scripts/Game/ContainerAutoParent.cs
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class ContainerAutoParent : MonoBehaviour
{
    [Header("Container")]
    public Transform containerRoot;   // GameSession.containerRoot
    public Transform outsideParent;   // ShipEnvironmentRoot 같은 함선 루트

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

    // GameSession에서 Ship으로 복귀한 직후 다시 호출해줄 메서드
    public void ResyncSceneItems()
    {
        if (zoneCollider == null) return;

        Scene zoneScene = gameObject.scene;
        Bounds bounds = zoneCollider.bounds;
        WorldItem[] allWorldItems = Object.FindObjectsByType<WorldItem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var wi in allWorldItems)
        {
            if (wi == null) continue;
            if (wi.gameObject.scene != zoneScene) continue;
            if (((1 << wi.gameObject.layer) & worldItemLayers) == 0) continue;

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

        ParentToContainer(wi.transform);
    }

    void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & worldItemLayers) == 0)
            return;

        WorldItem wi = other.GetComponentInParent<WorldItem>();
        if (wi == null) return;

        if (wi.transform.parent == containerRoot)
        {
            ParentToOutside(wi.transform, gameObject.scene);
        }
    }

    void ParentToContainer(Transform itemTransform)
    {
        if (containerRoot == null) return;
        itemTransform.SetParent(containerRoot, true);
    }

    void ParentToOutside(Transform itemTransform, Scene currentScene)
    {
        // Ship 씬에서는 ShipEnvironmentRoot 같은 outsideParent를 사용
        if (outsideParent != null && outsideParent.gameObject.scene == currentScene)
        {
            itemTransform.SetParent(outsideParent, true);
        }
        else
        {
            // Gameplay 씬이나 outsideParent가 다른 씬에 있을 때:
            // 그냥 씬 루트로 떼어내면 "행성에 남는 짐"이 됨
            itemTransform.SetParent(null, true);
        }
    }
}
