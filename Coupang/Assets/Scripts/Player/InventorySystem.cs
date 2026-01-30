// Assets/Scripts/Inventory/InventorySystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

using DeliveryBot.ItemSystem;
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

    // 
    // Knockback / Impact spill
    // 
    /// <summary>
    /// Spills ALL inventory items into the world with an initial velocity.
    /// Used when the player gets knocked down so the inventory "explodes" out.
    /// Returns the number of physical items dropped/spawned.
    /// </summary>
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

        Transform thrower = throwerRoot != null ? throwerRoot : transform.root;

        // Tuning (kept internal so you can tweak later)
        const float minSpeed = 7f;
        const float maxSpeed = 14f;
        const float minUp = 1.2f;
        const float maxUp = 3.8f;
        const float yawSpread = 40f;
        const float posJitter = 0.12f;
        const float spinMin = 1.5f;
        const float spinMax = 8f;
        const float carrierThrowSpeed = 6f;

        var uniqueStacks = new HashSet<ItemStackData>();
        int dropped = 0;

        // Iterate current stacks (unique heads)
        for (int i = 0; i < slots.Count; i++)
        {
            var head = slots[i].stack;
            if (head == null || head.def == null) continue;
            if (!uniqueStacks.Add(head)) continue;

            ItemDefinition def = head.def;

            // Carrier item: drop the actual carrier object if we have it.
            if (def.isCarrier)
            {
                if (carrier != null)
                {
                    // Make sure carrier can be container-parented again once dropped
                    var carrierWI = carrier.GetComponent<WorldItem>();
                    if (carrierWI) carrierWI.ignoreContainerAutoParent = false;

                    // Spill anything mounted on the carrier first
                    if (carrier.HasAnyMounted())
                        carrier.SpillAllOnCarrierDrop(basePos + Vector3.up * 0.2f, f);

                    // Drop carrier frame itself
                    Vector3 cPos = basePos + UnityEngine.Random.insideUnitSphere * 0.08f + Vector3.up * 0.15f;
                    carrier.DropAsBundle(cPos, f);

                    // Give it a little kick so it also "flies" away
                    var crb = carrier.GetComponent<Rigidbody>();
                    if (crb)
                        crb.linearVelocity = f * carrierThrowSpeed + Vector3.up * 1.2f;

                    carrier = null;
                    dropped++;
                }
                else
                {
                    // Fallback: if carrier ref missing, just spawn prefab like a normal item.
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

                // Remove carrier stack from slots
                ClearStackFromSlots(head);
                continue;
            }

            // Normal inventory item
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

            // Clear stack regardless (knockdown loses inventory)
            ClearStackFromSlots(head);
        }

        activeIndex = Mathf.Clamp(activeIndex, 0, slotCount - 1);

        // Refresh held visual + UI once
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
        // Prefer the dedicated socket component if present (single source of truth).
        var socketRig = GetComponentInParent<PlayerItemSockets>();
        if (socketRig != null)
        {
            socketRig.TryAutoResolve();
            _rightHandSocket = socketRig.rightHandSocket;
            _twoHandSocket = socketRig.twoHandSocket;

            if (!_rightHandSocket) _rightHandSocket = socketRig.transform;
            if (!_twoHandSocket) _twoHandSocket = socketRig.transform;
            return;
        }

        // Fallback: name-based search (kept for backward compatibility).
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
        // IMPORTANT: This is a purely visual held instance.
        // Prevent container auto-parent / cargo transfer from treating it as real cargo.
        var heldWorldItems = _heldInstance.GetComponentsInChildren<WorldItem>(true);
        foreach (var hwi in heldWorldItems)
        {
            if (!hwi) continue;
            hwi.ignoreContainerAutoParent = true;
        }


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

        // Decide socket by ItemDefinition.carryKind (single source of truth).
        // 1) Prefer explicitly authored grips on the prefab (ItemSystem.gripR / ItemSystem.carryGrip)
        Transform grip = null;
        var itemSys = _heldInstance.GetComponentInChildren<DeliveryBot.ItemSystem.ItemSystem>(true);
        if (itemSys != null)
        {
            if (itemSys.gripR != null) grip = itemSys.gripR;
            else if (itemSys.carryGrip != null) grip = itemSys.carryGrip;
        }

        // 2) Name-based fallback (legacy / non-ItemSystem prefabs)
        if (!grip && !string.IsNullOrEmpty(gripName))
            grip = FindDeepChildBFS(_heldInstance.transform, gripName);

        if (!grip && !string.IsNullOrEmpty(carryGripName))
            grip = FindDeepChildBFS(_heldInstance.transform, carryGripName);

        bool useTwoHandSocket = def.carryKind != CarryKind.OneHand;
        Transform socket = useTwoHandSocket ? _twoHandSocket : _rightHandSocket;

        // Matrix-based snap + parent (shared utility).
        ItemSystemSnapUtil.SnapAndParentToSocket(_heldInstance.transform, grip, socket, true);
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

    // (Snap helper removed here: we now use ItemSystemSnapUtil to avoid duplicate implementations.)

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
