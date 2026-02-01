// Assets/Scripts/Inventory/InventorySystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;

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
    public string twoHandSocketName = "TwoHandSocket";

    [Tooltip("Single grip transform name inside item prefab used for BOTH one-hand & two-hand.")]
    public string gripPointName = "GripPoint";

    [Header("State (read-only)")]
    public int activeIndex = 0;

    public event Action OnInventoryChanged;

    [Serializable]
    public class ItemStackData
    {
        public ItemDefinition def;
        public int size;

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
            return false;

        if (worldItem.pickupLocked)
            return false;

        var def = worldItem.definition;

        ContractCargoMarker cargoMarker = worldItem.GetComponent<ContractCargoMarker>();
        bool shouldNotifyContractPickup = cargoMarker != null && !cargoMarker.WasPicked;

        int need = Mathf.Clamp(def.slotSize, 1, slotCount);
        int where = FindContiguousSpace(need);

        if (where >= 0)
        {
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
                HandleCarrierPickup(worldItem);
            else
                worldItem.OnPickedUp(true);

            if (shouldNotifyContractPickup)
            {
                cargoMarker.MarkPicked();
                if (GameSession.Instance != null)
                    GameSession.Instance.RegisterContractCargoPickup();
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
                return false;

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

                wi.OnDropped(pos, Vector3.zero);
            }
        }

        ClearStackFromSlots(head);
        NotifyChanged();
        return true;
    }

    public bool DropCarrierAsBundle(Transform dropOrigin, Vector3 forward)
    {
        var head = GetActiveStack();
        if (head == null || head.def == null || !head.def.isCarrier)
            return false;

        ClearStackFromSlots(head);
        NotifyChanged();
        return true;
    }

    public int SpillAllToWorld(Transform dropOrigin, Vector3 forward, Transform throwerRoot = null)
    {
        EnsureSlots();

        Vector3 f = forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f)
            f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude > 0.0001f) f.Normalize();
        else f = Vector3.forward;

        Vector3 basePos = dropOrigin
            ? dropOrigin.position
            : (transform.position + transform.forward * 0.6f + Vector3.up * 0.5f);

        float posJitter = 0.25f;
        float yawSpread = 65f;
        float minSpeed = 2.5f;
        float maxSpeed = 6.5f;
        float minUp = 1.4f;
        float maxUp = 3.1f;

        float spinMin = 2.0f;
        float spinMax = 9.0f;

        float carrierThrowSpeed = 3.0f;
        Transform thrower = throwerRoot != null ? throwerRoot : transform;

        var uniqueStacks = new HashSet<ItemStackData>();
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i].stack;
            if (s != null) uniqueStacks.Add(s);
        }

        int dropped = 0;

        foreach (var head in uniqueStacks)
        {
            if (head == null || head.def == null) continue;
            var def = head.def;

            if (def.isCarrier)
            {
                if (carrier != null)
                {
                    var carrierWI = carrier.GetComponent<WorldItem>();
                    if (carrierWI) carrierWI.ignoreContainerAutoParent = false;

                    if (carrier.HasAnyMounted())
                        carrier.SpillAllOnCarrierDrop(basePos + Vector3.up * 0.2f, f);

                    Vector3 cPos = basePos + UnityEngine.Random.insideUnitSphere * 0.08f + Vector3.up * 0.15f;
                    carrier.DropAsBundle(cPos, f);

                    var crb = carrier.GetComponent<Rigidbody>();
                    if (crb)
                        crb.linearVelocity = f * carrierThrowSpeed + Vector3.up * 1.2f;

                    carrier = null;
                    dropped++;
                }
                else
                {
                    if (def.worldPrefab)
                    {
                        Vector3 pos = basePos + UnityEngine.Random.insideUnitSphere * posJitter + Vector3.up * 0.2f;
                        var go = UnityEngine.Object.Instantiate(def.worldPrefab, pos, Quaternion.identity);
                        go.name = def.worldPrefab.name;

                        var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                        wi.definition = def;

                        if (head.durCurrent >= 0 || head.durMax > 0)
                            wi.ApplyDurability(head.durCurrent, head.durMax, true);

                        Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-yawSpread, yawSpread), Vector3.up) * f;
                        float speed = UnityEngine.Random.Range(minSpeed, maxSpeed);
                        float up = UnityEngine.Random.Range(minUp, maxUp);
                        Vector3 vel = dir.normalized * speed + Vector3.up * up;

                        wi.ArmIgnoreBreakForThrower(thrower, 0.25f);
                        wi.OnDropped(pos, vel);

                        if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
                        if (wi.rb) wi.rb.angularVelocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(spinMin, spinMax);

                        dropped++;
                    }
                }

                ClearStackFromSlots(head);
                continue;
            }

            if (def.worldPrefab)
            {
                Vector3 pos = basePos + UnityEngine.Random.insideUnitSphere * posJitter + Vector3.up * 0.2f;
                var go = UnityEngine.Object.Instantiate(def.worldPrefab, pos, Quaternion.identity);
                go.name = def.worldPrefab.name;

                var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                wi.definition = def;

                if (head.durCurrent >= 0 || head.durMax > 0)
                    wi.ApplyDurability(head.durCurrent, head.durMax, true);

                Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-yawSpread, yawSpread), Vector3.up) * f;
                float speed = UnityEngine.Random.Range(minSpeed, maxSpeed);
                float up = UnityEngine.Random.Range(minUp, maxUp);
                Vector3 vel = dir.normalized * speed + Vector3.up * up;

                wi.ArmIgnoreBreakForThrower(thrower, 0.25f);
                wi.OnDropped(pos, vel);

                if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
                if (wi.rb) wi.rb.angularVelocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(spinMin, spinMax);

                dropped++;
            }

            ClearStackFromSlots(head);
        }

        activeIndex = Mathf.Clamp(activeIndex, 0, slotCount - 1);

        DestroyHeldVisual();
        RefreshHeldVisual();
        OnInventoryChanged?.Invoke();

        return dropped;
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

    private void ClearStackFromSlots(ItemStackData head)
    {
        if (head == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].stack == head)
                slots[i].stack = null;
        }

        while (activeIndex > 0 &&
               slots[activeIndex].stack == null &&
               slots[activeIndex - 1].stack == null)
        {
            activeIndex--;
        }
    }

    private void HandleCarrierPickup(WorldItem worldItem)
    {
        if (!worldItem) return;

        var cc = worldItem.GetComponent<CarrierController>();
        if (!cc) return;

        worldItem.ignoreContainerAutoParent = true;
        carrier = cc;

        if (carrierMountPivot)
        {
            Transform t = cc.transform;
            t.SetParent(carrierMountPivot, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        if (!worldItem.rb) worldItem.rb = worldItem.GetComponent<Rigidbody>();
        if (worldItem.rb)
        {
            // avoid kinematic angular velocity warning
            bool wasKin = worldItem.rb.isKinematic;
            if (wasKin) worldItem.rb.isKinematic = false;

            worldItem.rb.linearVelocity = Vector3.zero;
            worldItem.rb.angularVelocity = Vector3.zero;

            worldItem.rb.isKinematic = true;
            worldItem.rb.useGravity = false;
        }

        var cols = worldItem.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
            if (c) c.enabled = false;

        var rends = worldItem.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
            if (r) r.enabled = true;

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

        if (def.isCarrier) return;
        if (!def.worldPrefab) return;

        _heldInstance = UnityEngine.Object.Instantiate(def.worldPrefab);
        _heldInstance.name = def.worldPrefab.name + "_Held";

        // ✅ 옵션 토글 제거: held는 무조건 "시각용"으로 만든다 (항상 disable)
        var wis = _heldInstance.GetComponentsInChildren<WorldItem>(true);
        foreach (var wi in wis)
        {
            if (!wi) continue;
            wi.ignoreContainerAutoParent = true;
            wi.enabled = false;
        }

        var cols = _heldInstance.GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
            if (c) c.enabled = false;

        var rbs = _heldInstance.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            if (!rb) continue;

            bool wasKin = rb.isKinematic;
            if (wasKin) rb.isKinematic = false;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.isKinematic = true;
            rb.useGravity = false;
        }

        var pis = _heldInstance.GetComponentsInChildren<PickupInteractable>(true);
        foreach (var pi in pis)
            if (pi) pi.enabled = false;

        bool useTwoHandSocket = def.carryKind != CarryKind.OneHand;
        Transform socket = useTwoHandSocket ? _twoHandSocket : _rightHandSocket;

        Transform grip = FindDeepChildBFS(_heldInstance.transform, gripPointName);
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
    /// Snap root so that grip matches socket (position + rotation), then parent under socket.
    /// If grip is null => root directly to socket origin.
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

        // Snap in world, then parent
        root.SetParent(null, true);

        Quaternion deltaRot = socket.rotation * Quaternion.Inverse(grip.rotation);
        Quaternion newRot = deltaRot * root.rotation;

        Vector3 v = grip.position - root.position;
        Vector3 newPos = socket.position - (deltaRot * v);

        root.SetPositionAndRotation(newPos, newRot);
        root.SetParent(socket, true);
    }

    // ─────────────────────────────────────────────
    // Mission helpers
    // ─────────────────────────────────────────────
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

        DestroyHeldVisual();
        RefreshHeldVisual();
        OnInventoryChanged?.Invoke();

        return removedSlots;
    }
}
