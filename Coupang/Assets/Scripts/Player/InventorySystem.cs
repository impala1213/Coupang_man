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

    [Tooltip("Grip transform name inside item prefab for one-hand attach.")]
    [FormerlySerializedAs("gripName")]
    public string gripName = "Grip_R";

    [Tooltip("Grip transform name inside item prefab for two-hand attach.")]
    public string carryGripName = "CarryGrip";

    [Tooltip("Disable all colliders on held visual instance.")]
    public bool disableCollidersOnHeld = true;

    [Tooltip("Disable all rigidbodies on held visual instance.")]
    public bool disableRigidbodiesOnHeld = true;

    [Tooltip("Disable WorldItem component on held visual instance.")]
    public bool disableWorldItemOnHeld = true;

    [Tooltip("Disable PickupInteractable on held visual instance if present.")]
    public bool disablePickupInteractableOnHeld = true;

    [Header("Debug")]
    public bool enableHeldVisualDebugLogs = true;
    public bool drawGripToSocketLine = true;
    public float debugLineDuration = 2f;

    [Header("State (read-only)")]
    public int activeIndex = 0;

    public event Action OnInventoryChanged;

    [Serializable]
    public class ItemStackData
    {
        public ItemDefinition def;
        public int size;

        // Optional durability snapshot
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

        // Items sealed by completed drop zones cannot be picked up
        if (worldItem.pickupLocked)
            return false;

        var def = worldItem.definition;
        ContractCargoMarker cargoMarker = worldItem.GetComponent<ContractCargoMarker>();
        bool shouldNotifyContractPickup = cargoMarker != null && !cargoMarker.WasPicked;
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

                // restore to world state
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

    // 
    // Knockback / Impact spill
    // 
    public int SpillAllToWorld(Transform dropOrigin, Vector3 forward, Transform throwerRoot = null)
    {
        EnsureSlots();

        // Flatten direction (yaw only)
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

        // Parameters
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

        // Drop each unique stack only once
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

            // Carrier stack
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

            // Normal item
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
            else
            {
                Debug.LogWarning($"[InventorySystem] SpillAllToWorld: '{def.displayName}' has no worldPrefab. Clearing anyway.");
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
        if (!cc)
        {
            Debug.LogWarning("[InventorySystem] Carrier item picked up but no CarrierController found.");
            return;
        }

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
        if (!_twoHandSocket) _twoHandSocket = FindDeepChildBFS(playerRoot, "CarrySocket"); // legacy fallback

        if (!_rightHandSocket) _rightHandSocket = playerRoot;
        if (!_twoHandSocket) _twoHandSocket = playerRoot;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static string GetPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static void LogSnapError(string tag, Transform grip, Transform socket)
    {
        if (grip == null || socket == null)
        {
            Debug.Log($"{tag} grip or socket null");
            return;
        }

        float dist = Vector3.Distance(grip.position, socket.position);
        float ang = Quaternion.Angle(grip.rotation, socket.rotation);
        Debug.Log($"{tag} grip->socket dist={dist:F4} ang={ang:F2}");
    }
#endif

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

        // Make sure held instance is purely visual
        var heldWorldItems = _heldInstance.GetComponentsInChildren<WorldItem>(true);
        foreach (var hwi in heldWorldItems)
        {
            if (!hwi) continue;
            hwi.ignoreContainerAutoParent = true;
        }

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

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        if (disableWorldItemOnHeld)
        {
            var wis = _heldInstance.GetComponentsInChildren<WorldItem>(true);
            foreach (var wi in wis)
                if (wi) wi.enabled = false;
        }

        if (disablePickupInteractableOnHeld)
        {
            var pis = _heldInstance.GetComponentsInChildren<PickupInteractable>(true);
            foreach (var pi in pis)
                if (pi) pi.enabled = false;
        }

        // one-hand => RightHandSocket + Grip_R
        // two-hand => TwoHandSocket + CarryGrip
        bool useTwoHandSocket = def.carryKind != CarryKind.OneHand;
        Transform socket = useTwoHandSocket ? _twoHandSocket : _rightHandSocket;

        Transform grip = null;
        if (useTwoHandSocket)
        {
            // Two-hand: CarryGrip first
            grip = FindDeepChildBFS(_heldInstance.transform, carryGripName);
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, "CarryGrip");
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, gripName);
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, "Grip_R");
        }
        else
        {
            // One-hand: Grip_R first
            grip = FindDeepChildBFS(_heldInstance.transform, gripName);
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, "Grip_R");
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, carryGripName);
            if (!grip) grip = FindDeepChildBFS(_heldInstance.transform, "CarryGrip");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (enableHeldVisualDebugLogs)
        {
            Debug.Log($"[HeldVisual] def={def.name} carryKind={def.carryKind} prefab={(def.worldPrefab ? def.worldPrefab.name : "null")}");
            Debug.Log($"[HeldVisual] socket={(socket ? GetPath(socket) : "null")}  right={(_rightHandSocket ? GetPath(_rightHandSocket) : "null")}  twoHand={(_twoHandSocket ? GetPath(_twoHandSocket) : "null")}");
            Debug.Log($"[HeldVisual] useTwoHand={useTwoHandSocket} gripName='{gripName}' carryGripName='{carryGripName}' foundGrip={(grip ? GetPath(grip) : "null")}");
            if (grip != null) LogSnapError("[HeldVisual PRE ]", grip, socket);
            else Debug.LogWarning("[HeldVisual] grip NOT FOUND => will FALLBACK to socket origin");
        }
#endif

        SnapRootToSocket(_heldInstance.transform, grip, socket);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (enableHeldVisualDebugLogs && grip != null)
        {
            LogSnapError("[HeldVisual POST]", grip, socket);
            if (drawGripToSocketLine && socket != null)
                Debug.DrawLine(grip.position, socket.position, Color.red, debugLineDuration);
        }
#endif
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
    ///
    /// 핵심:
    /// - root를 socket의 자식으로 둔 상태에서,
    /// - socket 로컬 공간에서 grip이 (0,0,0 / identity)가 되도록 root.localPosition/localRotation을 보정한다.
    /// → 결과적으로 "CarryGrip이 TwoHandSocket에 딱 맞닿는" 상태가 된다.
    /// </summary>
    private static void SnapRootToSocket(Transform root, Transform grip, Transform socket)
    {
        if (!root || !socket) return;

        // 먼저 socket 밑으로 붙이고(월드 포즈 유지)
        root.SetParent(socket, true);

        if (!grip)
        {
            // grip이 없으면 그냥 원점 부착
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[SnapRootToSocket] grip==null => FALLBACK localPosition/Rotation = zero");
#endif
            return;
        }

        // 2-pass: 회전 보정 -> 위치 보정 (한 번 더 반복해서 잔오차 제거)
        for (int pass = 0; pass < 2; pass++)
        {
            // (1) 회전: socket 기준으로 grip 회전이 identity가 되게 root를 보정
            Quaternion gripRotInSocket = Quaternion.Inverse(socket.rotation) * grip.rotation;
            Quaternion cancelRot = Quaternion.Inverse(gripRotInSocket);

            // socket 로컬에서 root를 회전(원점 기준) : localRotation과 localPosition 둘 다 회전시켜야 함
            root.localRotation = cancelRot * root.localRotation;
            root.localPosition = cancelRot * root.localPosition;

            // (2) 위치: socket 기준으로 grip 위치가 Vector3.zero가 되게 root를 보정
            Vector3 gripPosInSocket = socket.InverseTransformPoint(grip.position);
            root.localPosition -= gripPosInSocket;
        }
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
