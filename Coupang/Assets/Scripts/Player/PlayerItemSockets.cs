using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PlayerItemSockets : MonoBehaviour
{
    [Header("Sockets")]
    public Transform rightHandSocket;   // One-hand items

    [FormerlySerializedAs("carrySocket")]
    public Transform twoHandSocket;     // Two-hand items (legacy name: CarrySocket)

    public Transform dropOrigin;        // Drop/throw origin

    private void Reset()
    {
        TryAutoResolve();
    }

    private void Awake()
    {
        TryAutoResolve();
    }

    public void TryAutoResolve()
    {
        if (!rightHandSocket) rightHandSocket = FindDeepChild(transform, "RightHandSocket");

        if (!twoHandSocket)
            twoHandSocket = FindDeepChild(transform, "TwoHandSocket");
        if (!twoHandSocket)
            twoHandSocket = FindDeepChild(transform, "CarrySocket"); // legacy fallback

        if (!dropOrigin) dropOrigin = FindDeepChild(transform, "DropOrigin");

        if (!rightHandSocket) rightHandSocket = transform;
        if (!twoHandSocket) twoHandSocket = transform;
        if (!dropOrigin) dropOrigin = transform;
    }

    public Transform GetSocketFor(ItemDefinition def)
    {
        if (def == null) return rightHandSocket;

        // Socket selection is based ONLY on ItemDefinition.carryKind (not slotSize, not itemType).
        bool isOneHand = def.carryKind == CarryKind.OneHand;
        return isOneHand ? rightHandSocket : twoHandSocket;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (!root) return null;

        var q = new System.Collections.Generic.Queue<Transform>();
        q.Enqueue(root);

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (t.name == name) return t;

            for (int i = 0; i < t.childCount; i++)
                q.Enqueue(t.GetChild(i));
        }

        return null;
    }
}
