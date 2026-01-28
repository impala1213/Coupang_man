// Assets/Scripts/Inventory/InventorySystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    [Header("Config")]
    public int slotCount = 5;

    [Header("Refs")]
    public CarrierController carrier;

    [Header("Carrier Mount")]
    [Tooltip("Player pivot where the carrier object is attached when equipped.")]
    public Transform carrierMountPivot; // e.g., player/CarrierPivot

    [Header("Held Visual (in-hand)")]
    [Tooltip("If true, spawns a visual instance of the active item and snaps it to hand sockets.")]
    public bool spawnHeldVisual = true;

    [Tooltip("Socket name under Player hierarchy for one-hand items.")]
    public string rightHandSocketName = "RightHandSocket";

    [Tooltip("Socket name under Player hierarchy for two-hand items.")]
    [FormerlySerializedAs("twoHandSocketName")]
    public string twoHandSocketName = "TwoHandSocket";

    [Tooltip("Grip transform name inside item prefab for in-hand attach (used for both one-hand and two-hand).")]
    [FormerlySerializedAs("gripName")]
    public string gripName = "Grip_R";

    [Tooltip("LEGACY (no longer used for socket selection): kept for backward compatibility with older prefabs.")]
    public string carryGripName = "CarryGrip";

    [Tooltip("Disable all colliders on held visual instance.")]
    public bool disableCollidersOnHeld = true;

    [Tooltip("Disable all rigidbodies on held visual instance.")]
    public bool disableRigidbodiesOnHeld = true;

    [Tooltip("Disable WorldItem component on held visual instance.")]
    public bool disableWorldItemOnHeld = true;

    [Tooltip("Disable PickupInteractable on held visual instance if present.")]
    public bool disablePickupInteractableOnHeld = true;

    [Header("State (read-only)")]
    public int activeIndex = 0;

    public event Action OnInventoryChanged;

    [Serializable]
    public class ItemStackData
    {
        public ItemDefinition def;
        public int size;

        // Optional durability snapshot (kept for compatibility even if you later remove durability).
        public int durCurrent = -1;
        public int durMax = -1;
    }

    [Serializable]
    public class Slot
    {
        public ItemStackData stack;
    }

    public List<Slot> slots = new List<Slot>();

    // Held visual runtime
    private Transform _rightHandSocket;
    private Transform _twoHandSocket;
    private GameObject _heldInstance;

    private void Awake()
    {
        EnsureSlots();
        activeIndex = Mathf.Clamp(activeIndex, 0, slotCount - 1);

        AutoWireCarrierMountPivot();

        if (!carrier)
            carrier = UnityEngine.Object.FindFirstObjectByType<CarrierController>();

        ResolveSockets();
        RefreshHeldVisual();
    }

    private void OnDestroy()
    {
        DestroyHeldVisual();
    }

    private void EnsureSlots()
    {
        if (slots.Count == slotCount) return;

        slots.Clear();
        for (int i = 0; i < slotCount; i++)
            slots.Add(new Slot());
    }

    private void AutoWireCarrierMountPivot()
    {
        if (carrierMountPivot) return;

        var player = GetComponentInParent<PlayerController>();
        if (player != null)
        {
            var pivot = player.transform.Find("CarrierPivot");
            carrierMountPivot = pivot != null ? pivot : player.transform;
        }
        else
        {
            carrierMountPivot = transform;
        }
    }

    // ─────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────
    public bool IsEmpty(int i) =>
        i >= 0 && i < slots.Count && slots[i].stack == null;

    public ItemDefinition Get(int i) =>
        (i >= 0 && i < slots.Count && slots[i].stack != null) ? slots[i].stack.def : null;

    public ItemDefinition ActiveDef() => Get(activeIndex);

    public bool HasCarrierInInventory()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i].stack;
            if (s != null && s.def != null && s.def.isCarrier)
                return true;
        }
        return false;
    }

    public int FindContiguousSpace(int required)
    {
        for (int start = 0; start <= slotCount - required; start++)
        {
            bool ok = true;
            for (int k = 0; k < required; k++)
            {
                if (!IsEmpty(start + k))
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return start;
        }
        return -1;
    }

    // ─────────────────────────────────────────────
    // Control
    // ─────────────────────────────────────────────
    public void SetActiveIndex(int idx)
    {
        activeIndex = Mathf.Clamp(idx, 0, slotCount - 1);
        NotifyChanged();
    }

    public bool TryPickupWorldItem(WorldItem worldItem)
    {
        if (!worldItem || !worldItem.definition)
        {
            Debug.LogWarning("[InventorySystem] WorldItem/definition null.");
            return false;
        }

        var def = worldItem.definition;
        int need = Mathf.Clamp(def.slotSize, 1, slotCount);

        int where = FindContiguousSpace(need);
        if (where >= 0)
        {
            // Snapshot durability if the world item supports it
            int cur = -1, max = -1;
            if (worldItem.TryGetDurability(out var c, out var m))
            {
                cur = c;
                max = m;
            }

            var data = new ItemStackData
            {
                def = def,
                size = need,
                durCurrent = cur,
                durMax = max
            };

            for (int k = 0; k < need; k++)
                slots[where + k].stack = data;

            if (def.isCarrier)
            {
                // Carrier: keep the same world instance and attach it to player
                HandleCarrierPickup(worldItem);
            }
            else
            {
                // Normal item: let WorldItem handle cleanup (usually destroy)
                worldItem.OnPickedUp(true);
            }

            activeIndex = where;
            NotifyChanged();
            return true;
        }

        // No inventory space => try mounting onto carrier if present
        if (HasCarrierInInventory())
        {
            if (!carrier)
                carrier = UnityEngine.Object.FindFirstObjectByType<CarrierController>();

            if (!carrier)
            {
                Debug.LogWarning("[InventorySystem] Carrier reference missing.");
                return false;
            }

            if (!def.isCarrier)
                return carrier.TryMount(worldItem);
        }

        return false;
    }

    /// <summary>
    /// Drop active item to world as a new prefab (non-carrier items only).
    /// IMPORTANT: Always call WorldItem.OnDropped so the item re-enables physics/colliders properly.
    /// </summary>
    public bool DropActiveItem(Transform dropOrigin, Vector3 forward)
    {
        var head = GetActiveStack();
        if (head == null || head.def == null)
            return false;

        var def = head.def;

        // Carrier dropping is handled separately (hold G)
        if (!def.isCarrier)
        {
            Vector3 pos = ComputeDropPos(dropOrigin, forward);

            if (def.worldPrefab)
            {
                var go = UnityEngine.Object.Instantiate(def.worldPrefab, pos, Quaternion.identity);
                go.name = def.worldPrefab.name;

                var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                wi.definition = def;

                if (head.durCurrent >= 0 || head.durMax > 0)
                    wi.ApplyDurability(head.durCurrent, head.durMax, true);

                // ✅ Critical fix: restore to "world state" (colliders/rigidbody/gravity/visibility)
                wi.OnDropped(pos, Vector3.zero);
            }
        }

        ClearStackFromSlots(head);
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Only removes the carrier item from inventory.
    /// The actual world drop is done by CarrierController.DropAsBundle.
    /// </summary>
    public bool DropCarrierAsBundle(Transform dropOrigin, Vector3 forward)
    {
        var head = GetActiveStack();
        if (head == null || head.def == null || !head.def.isCarrier)
            return false;

        ClearStackFromSlots(head);
        NotifyChanged();
        return true;
    }

    // ─────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────
    private ItemStackData GetActiveStack()
    {
        if (activeIndex < 0 || activeIndex >= slots.Count) return null;
        return slots[activeIndex].stack;
    }

    private Vector3 ComputeDropPos(Transform origin, Vector3 forward)
    {
        if (origin)
            return origin.position + forward * 0.6f + Vector3.up * 0.5f;

        return transform.position + transform.forward * 0.6f + Vector3.up * 0.5f;
    }

    private void NotifyChanged()
    {
        OnInventoryChanged?.Invoke();
        RefreshHeldVisual();
    }

    /// <summary>
    /// Clear all slots referencing this stack and adjust activeIndex.
    /// </summary>
    private void ClearStackFromSlots(ItemStackData head)
    {
        if (head == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].stack == head)
                slots[i].stack = null;
        }

        // Move activeIndex left if there are consecutive empty slots
        while (activeIndex > 0 &&
               slots[activeIndex].stack == null &&
               slots[activeIndex - 1].stack == null)
        {
            activeIndex--;
        }
    }

    /// <summary>
    /// Attach the picked-up carrier world instance to the player pivot and configure it for equipped state.
    /// </summary>
    private void HandleCarrierPickup(WorldItem worldItem)
    {
        if (!worldItem) return;

        var cc = worldItem.GetComponent<CarrierController>();
        if (!cc)
        {
            Debug.LogWarning("[InventorySystem] Carrier item picked up but no CarrierController found.");
            return;
        }

        // Prevent other systems from re-parenting this carrier while equipped
        worldItem.ignoreContainerAutoParent = true;

        carrier = cc;

        // Attach under mount pivot (usually player's back)
        if (carrierMountPivot)
        {
            Transform t = cc.transform;
            t.SetParent(carrierMountPivot, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        // Configure physics while worn
        if (!worldItem.rb) worldItem.rb = worldItem.GetComponent<Rigidbody>();
        if (worldItem.rb)
        {
            worldItem.rb.linearVelocity = Vector3.zero;
            worldItem.rb.angularVelocity = Vector3.zero;
            worldItem.rb.isKinematic = true;
            worldItem.rb.useGravity = false;
        }

        // Disable colliders while equipped
        var cols = worldItem.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
            if (c) c.enabled = false;

        // Keep renderers on (so you can see the backpack)
        var rends = worldItem.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
            if (r) r.enabled = true;

        // Push reference into PlayerController if possible
        if (carrierMountPivot)
        {
            var player = carrierMountPivot.GetComponentInParent<PlayerController>();
            if (player != null)
                player.carrier = cc;
        }
    }

    // ─────────────────────────────────────────────
    // Held Visual (socket attach)
    // ─────────────────────────────────────────────
    private void ResolveSockets()
    {
        Transform playerRoot = GetComponentInParent<PlayerController>() != null
            ? GetComponentInParent<PlayerController>().transform
            : transform;

        _rightHandSocket = FindDeepChildBFS(playerRoot, rightHandSocketName);
        _twoHandSocket = FindDeepChildBFS(playerRoot, twoHandSocketName);
        if (!_twoHandSocket) _twoHandSocket = FindDeepChildBFS(playerRoot, "CarrySocket"); // legacy fallback

        if (!_rightHandSocket) _rightHandSocket = playerRoot;
        if (!_twoHandSocket) _twoHandSocket = playerRoot;
    }

    private void RefreshHeldVisual()
    {
        if (!spawnHeldVisual)
        {
            DestroyHeldVisual();
            return;
        }

        if (!_rightHandSocket || !_twoHandSocket)
            ResolveSockets();

        DestroyHeldVisual();

        var def = ActiveDef();
        if (!def) return;

        // Carrier item should not be shown in hand
        if (def.isCarrier) return;

        if (!def.worldPrefab) return;

        _heldInstance = UnityEngine.Object.Instantiate(def.worldPrefab);
        _heldInstance.name = def.worldPrefab.name + "_Held";

        // Disable physics/interaction on held instance
        if (disableCollidersOnHeld)
        {
            var cols = _heldInstance.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
                if (c) c.enabled = false;
        }

        if (disableRigidbodiesOnHeld)
        {
            var rbs = _heldInstance.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rbs)
            {
                if (!rb) continue;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (disableWorldItemOnHeld)
        {
            var wi = _heldInstance.GetComponent<WorldItem>();
            if (wi) wi.enabled = false;
        }

        if (disablePickupInteractableOnHeld)
        {
            var pi = _heldInstance.GetComponent<PickupInteractable>();
            if (pi) pi.enabled = false;
        }

        // Decide socket by ItemDefinition.carryKind (single source of truth).
        // Grip is shared; you only need ONE grip transform in the item prefab.
        Transform grip = FindDeepChildBFS(_heldInstance.transform, gripName);

        // Legacy fallbacks (older prefabs may still use these names)
        if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, "Grip_R");
        if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, carryGripName);

        bool useTwoHandSocket = def.carryKind != CarryKind.OneHand;
        Transform socket = useTwoHandSocket ? _twoHandSocket : _rightHandSocket;

        SnapRootToSocket(_heldInstance.transform, grip, socket);
    }

    private void DestroyHeldVisual()
    {
        if (_heldInstance)
        {
            UnityEngine.Object.Destroy(_heldInstance);
            _heldInstance = null;
        }
    }

    private static Transform FindDeepChildBFS(Transform root, string name)
    {
        if (!root || string.IsNullOrEmpty(name)) return null;

        var q = new Queue<Transform>();
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

    /// <summary>
    /// Snap root so that grip matches socket (position + rotation).
    /// If grip is null, root is simply parented to socket with zero local transform.
    /// </summary>
    private static void SnapRootToSocket(Transform root, Transform grip, Transform socket)
    {
        if (!root || !socket) return;

        if (!grip)
        {
            root.SetParent(socket, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            return;
        }

        // Matrix-based snap (stable)
        root.SetParent(null, true);

        Matrix4x4 socketM = socket.localToWorldMatrix;
        Matrix4x4 rootToGrip = root.worldToLocalMatrix * grip.localToWorldMatrix;
        Matrix4x4 desiredRootM = socketM * rootToGrip.inverse;

        Vector3 pos = (Vector3)desiredRootM.GetColumn(3);
        Vector3 forward = (Vector3)desiredRootM.GetColumn(2);
        Vector3 up = (Vector3)desiredRootM.GetColumn(1);

        forward.Normalize();
        up = (up - Vector3.Dot(up, forward) * forward).normalized;
        if (up.sqrMagnitude < 0.0001f) up = Vector3.up;

        Quaternion rot = Quaternion.LookRotation(forward, up);

        root.SetPositionAndRotation(pos, rot);
        root.SetParent(socket, true);
    }

// ─────────────────────────────────────────────
// Mission helpers
// ─────────────────────────────────────────────
/// <summary>
/// Removes any inventory slot whose definition matches one of the given defs.
/// Used to purge mission cargo on return.
/// </summary>
public int RemoveAllMatchingDefinitions(ICollection<ItemDefinition> defs)
{
    if (defs == null || defs.Count == 0)
        return 0;

    int removedSlots = 0;

    for (int i = 0; i < slots.Count; i++)
    {
        var st = slots[i].stack;
        if (st == null || st.def == null)
            continue;

        if (!defs.Contains(st.def))
            continue;

        slots[i].stack = null;
        removedSlots++;
    }

    activeIndex = Mathf.Clamp(activeIndex, 0, slotCount - 1);

    // Refresh held visuals & UI
    DestroyHeldVisual();
    RefreshHeldVisual();
    OnInventoryChanged?.Invoke();

    return removedSlots;
}


}
