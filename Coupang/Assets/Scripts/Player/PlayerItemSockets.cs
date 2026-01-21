using UnityEngine;

[DisallowMultipleComponent]
public class PlayerItemSockets : MonoBehaviour
{
    [Header("Sockets")]
    public Transform rightHandSocket;   // One-hand items
    public Transform carrySocket;       // Two-hand cargo
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
        if (!carrySocket) carrySocket = FindDeepChild(transform, "CarrySocket");
        if (!dropOrigin) dropOrigin = FindDeepChild(transform, "DropOrigin");

        if (!rightHandSocket) rightHandSocket = transform;
        if (!carrySocket) carrySocket = transform;
        if (!dropOrigin) dropOrigin = transform;
    }

    public Transform GetSocketFor(ItemDefinition def)
    {
        if (def == null) return rightHandSocket;

        bool isTwoHandCargo = (def.itemType == ItemType.Cargo) || (def.slotSize >= 2);
        return isTwoHandCargo ? carrySocket : rightHandSocket;
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
