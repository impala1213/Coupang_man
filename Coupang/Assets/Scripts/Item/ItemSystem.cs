using System.Collections.Generic;
using UnityEngine;

namespace DeliveryBot.ItemSystem
{
    public enum CarryType { OneHand, TwoHandCargo, TwoPersonCargo }
    public enum PlayerCarryState { Free, HoldingOneHand, CarryingTwoHand, TwoPerson_OneSide, TwoPerson_TwoSide, Dragging }
    public enum HandleSide { A, B, Auto }
    public enum GripSlot { GripR, CarryGrip }
    public enum CarryModeOverride { Auto, OneHand, TwoHand, TwoPerson }

    public interface IDamageReceiver { void TakeDamage(float amount); }

    public static class ItemSystemDamageUtil
    {
        public static void DealDamage(GameObject target, float amount)
        {
            if (target == null) return;

            var receiver = target.GetComponent<IDamageReceiver>();
            if (receiver != null) { receiver.TakeDamage(amount); return; }

            target.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
        }
    }

    public static class ItemSystemSnapUtil
    {
        /// <summary>
        /// Snaps an item so that <paramref name="itemGrip"/> matches <paramref name="socket"/> in world space.
        /// 
        /// Why matrix-based?
        /// - Works regardless of where the grip sits in the hierarchy (not necessarily direct child)
        /// - Less sensitive to parent scale/rotation quirks
        /// 
        /// If <paramref name="itemGrip"/> is null, the item root is simply aligned to the socket.
        /// </summary>
        public static void SnapToSocket(Transform itemRoot, Transform itemGrip, Transform socket)
        {
            if (itemRoot == null || socket == null) return;

            if (itemGrip == null)
            {
                itemRoot.SetPositionAndRotation(socket.position, socket.rotation);
                return;
            }

            // Compute the relative transform (root -> grip) in matrix form.
            // For a descendant grip, this collapses to the local matrix from root to grip.
            Matrix4x4 rootToGrip = itemRoot.worldToLocalMatrix * itemGrip.localToWorldMatrix;
            Matrix4x4 desiredRootM = socket.localToWorldMatrix * rootToGrip.inverse;

            Vector3 pos = (Vector3)desiredRootM.GetColumn(3);
            Vector3 forward = (Vector3)desiredRootM.GetColumn(2);
            Vector3 up = (Vector3)desiredRootM.GetColumn(1);

            // Orthonormalize
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            forward.Normalize();

            up = (up - Vector3.Dot(up, forward) * forward);
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            up.Normalize();

            Quaternion rot = Quaternion.LookRotation(forward, up);
            itemRoot.SetPositionAndRotation(pos, rot);
        }

        /// <summary>
        /// Snaps (by grip) and then parents the item under the socket while preserving world pose.
        /// </summary>
        public static void SnapAndParentToSocket(Transform itemRoot, Transform itemGrip, Transform socket, bool worldPositionStays = true)
        {
            if (itemRoot == null || socket == null) return;

            if (itemGrip == null)
            {
                // Simple parent + zero local pose
                itemRoot.SetParent(socket, false);
                itemRoot.localPosition = Vector3.zero;
                itemRoot.localRotation = Quaternion.identity;
                return;
            }

            // Snap in world space, then parent while preserving the snapped pose.
            itemRoot.SetParent(null, true);
            SnapToSocket(itemRoot, itemGrip, socket);
            itemRoot.SetParent(socket, worldPositionStays);
        }
    }

    public static class ItemSystemVisualUtil
    {
        public static void SetRenderersEnabled(GameObject go, bool enabled)
        {
            if (go == null) return;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
                if (rs[i] != null) rs[i].enabled = enabled;
        }
    }

    // ---------------------------------------------------------
    // Item-side grip data (attach to item prefab)
    // ---------------------------------------------------------
    [DisallowMultipleComponent]
    public class ItemSystem : MonoBehaviour
    {
        public CarryModeOverride modeOverride = CarryModeOverride.Auto;

        [Header("Grips")]
        public Transform gripR;
        public Transform carryGrip;

        [Header("Two-person handles (optional)")]
        public Transform handleA;
        public Transform handleB;

        [Header("Signals")]
        [Range(0.1f, 1.2f)] public float moveSpeedMultiplier = 1.0f;
        [Range(0f, 1f)] public float viewOcclusion = 0.0f;

        [Header("Throw (one-hand)")]
        public int throwSelfDamage = 5;

        [Header("Animation (optional)")]
        public AnimatorOverrideController oneHandOverride;
        public AnimatorOverrideController twoHandOverride;

        public Transform GetGrip(GripSlot slot)
        {
            // We no longer distinguish grip slots for one-hand vs two-hand.
            // Use gripR as the single source of truth (carryGrip is legacy fallback).
            if (gripR != null) return gripR;
            if (carryGrip != null) return carryGrip;

            // NOTE: returning transform means "no offset", which can look like snapping to socket origin
            return transform;
        }

        /// <summary>
        /// Attach this item to a player socket so that the authored grip point matches the socket.
        /// If <paramref name="gripOverride"/> is provided, it will be used instead of the authored grips.
        /// </summary>
        public void AttachToSocket(Transform socket, Transform gripOverride = null)
        {
            Transform grip = gripOverride != null ? gripOverride : (gripR != null ? gripR : carryGrip);
            ItemSystemSnapUtil.SnapAndParentToSocket(transform, grip, socket, true);
        }

        public CarryType ResolveCarryType(WorldItem wi)
        {
            if (modeOverride == CarryModeOverride.OneHand) return CarryType.OneHand;
            if (modeOverride == CarryModeOverride.TwoHand) return CarryType.TwoHandCargo;
            if (modeOverride == CarryModeOverride.TwoPerson) return CarryType.TwoPersonCargo;

            if (wi != null && wi.GetComponent<ItemSystemTwoPersonCargo>() != null)
                return CarryType.TwoPersonCargo;

            if (wi != null && wi.definition != null)
            {
                switch (wi.definition.carryKind)
                {
                    case CarryKind.OneHand: return CarryType.OneHand;
                    case CarryKind.TwoHand: return CarryType.TwoHandCargo;
                    case CarryKind.TwoPersonCargo: return CarryType.TwoPersonCargo;
                    default: return CarryType.OneHand;
                }
            }

            return CarryType.OneHand;
        }
    }

    // ---------------------------------------------------------
    // Two-person cargo (attach only to 2-person items)
    // ---------------------------------------------------------
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldItem))]
    public class ItemSystemTwoPersonCargo : MonoBehaviour
    {
        public Transform handleA;
        public Transform handleB;

        public float soloDragStartDelay = 2.0f;
        public float dragDamagePerSecond_Player = 8.0f;
        public float dragDamagePerSecond_Item = 6.0f;

        public float dragOffsetBack = 0.7f;
        public float dragOffsetDown = 0.25f;
        public float soloTiltDegrees = 18f;

        private WorldItem wi;
        private ItemSystem rig;

        private ItemSystemPlayerCarry holderA;
        private ItemSystemPlayerCarry holderB;

        private float soloMoveTimer;
        private bool isDragging;

        public bool HasAnyHolder => holderA != null || holderB != null;
        public bool IsFullyCarried => holderA != null && holderB != null;
        public bool IsDragging => isDragging;

        private void Awake()
        {
            wi = GetComponent<WorldItem>();
            rig = GetComponent<ItemSystem>();

            if (rig != null)
            {
                if (handleA == null) handleA = rig.handleA;
                if (handleB == null) handleB = rig.handleB;
            }
        }

        public bool TryGrab(ItemSystemPlayerCarry player, HandleSide side)
        {
            if (player == null) return false;
            if (handleA == null || handleB == null) return false;

            if (side == HandleSide.Auto)
                side = ChooseNearestFreeHandle(player);

            if (side == HandleSide.A)
            {
                if (holderA != null && holderA != player) return false;
                holderA = player;
            }
            else
            {
                if (holderB != null && holderB != player) return false;
                holderB = player;
            }

            EnsureHeldWorldState();
            soloMoveTimer = 0f;
            isDragging = false;
            return true;
        }

        public void Release(ItemSystemPlayerCarry player)
        {
            if (player == null) return;

            if (holderA == player) holderA = null;
            if (holderB == player) holderB = null;

            soloMoveTimer = 0f;
            isDragging = false;

            if (!HasAnyHolder && wi != null)
            {
                wi.ignoreContainerAutoParent = false;
                wi.OnDropped(transform.position, Vector3.zero);
            }
        }

        private void EnsureHeldWorldState()
        {
            if (wi == null) return;

            wi.ignoreContainerAutoParent = true;
            wi.OnPickedUp(false);

            // WorldItem.OnPickedUp hides renderers; re-enable for carry visuals.
            ItemSystemVisualUtil.SetRenderersEnabled(wi.gameObject, true);
        }

        private HandleSide ChooseNearestFreeHandle(ItemSystemPlayerCarry player)
        {
            bool aFree = holderA == null || holderA == player;
            bool bFree = holderB == null || holderB == player;

            if (aFree && !bFree) return HandleSide.A;
            if (!aFree && bFree) return HandleSide.B;

            Transform socket = player.TwoPersonHandleSocketOrFallback;
            if (socket == null) return HandleSide.A;

            float da = Vector3.SqrMagnitude(handleA.position - socket.position);
            float db = Vector3.SqrMagnitude(handleB.position - socket.position);
            return da <= db ? HandleSide.A : HandleSide.B;
        }

        private void LateUpdate()
        {
            if (!HasAnyHolder) return;

            if (IsFullyCarried)
            {
                AlignBetweenTwoHolders();
                soloMoveTimer = 0f;
                isDragging = false;
                return;
            }

            var solo = holderA != null ? holderA : holderB;
            var soloSide = holderA != null ? HandleSide.A : HandleSide.B;

            AlignToSoloHolder(solo, soloSide);

            if (solo != null && solo.IsAttemptingMove)
            {
                soloMoveTimer += Time.deltaTime;
                if (!isDragging && soloMoveTimer >= soloDragStartDelay)
                    isDragging = true;
            }
            else
            {
                soloMoveTimer = 0f;
                isDragging = false;
            }

            if (isDragging && solo != null)
                ApplyDraggingPoseAndDamage(solo);
        }

        private void AlignBetweenTwoHolders()
        {
            Transform socketA = holderA.TwoPersonHandleSocketOrFallback;
            Transform socketB = holderB.TwoPersonHandleSocketOrFallback;
            if (socketA == null || socketB == null) return;

            Vector3 vItem = handleB.position - handleA.position;
            Vector3 vTarget = socketB.position - socketA.position;

            if (vItem.sqrMagnitude < 1e-6f || vTarget.sqrMagnitude < 1e-6f) return;

            Quaternion delta = Quaternion.FromToRotation(vItem, vTarget);
            transform.rotation = delta * transform.rotation;
            transform.position += socketA.position - handleA.position;
        }

        private void AlignToSoloHolder(ItemSystemPlayerCarry solo, HandleSide side)
        {
            if (solo == null) return;

            Transform socket = solo.TwoPersonHandleSocketOrFallback;
            if (socket == null) return;

            Transform soloHandle = (side == HandleSide.A) ? handleA : handleB;
            ItemSystemSnapUtil.SnapToSocket(transform, soloHandle, socket);

            Vector3 tiltAxis = socket.right;
            transform.rotation = Quaternion.AngleAxis(soloTiltDegrees, tiltAxis) * transform.rotation;
        }

        private void ApplyDraggingPoseAndDamage(ItemSystemPlayerCarry solo)
        {
            Transform socket = solo.TwoPersonHandleSocketOrFallback;
            if (socket == null) return;

            Vector3 targetPos = socket.position - solo.transform.forward * dragOffsetBack;
            targetPos.y -= dragOffsetDown;
            transform.position = Vector3.Lerp(transform.position, targetPos, 12f * Time.deltaTime);

            // Damage player
            ItemSystemDamageUtil.DealDamage(solo.gameObject, dragDamagePerSecond_Player * Time.deltaTime);

            // Damage item (no Durability component, use WorldItem)
            if (wi != null && wi.useDurability)
            {
                int dmg = Mathf.CeilToInt(dragDamagePerSecond_Item * Time.deltaTime);
                if (dmg > 0) wi.ApplyDamage(dmg);
            }
        }
    }

    // ---------------------------------------------------------
    // Player carry controller (attach to player)
    // ---------------------------------------------------------
    [DisallowMultipleComponent]
    public class ItemSystemPlayerCarry : MonoBehaviour
    {
        public Transform rightHandSocket;
        public Transform carryRoot;
        public Transform stashRoot;
        public Transform twoPersonHandleSocket;

        public Animator animator;
        public string paramIsOneHand = "IsOneHand";
        public string paramIsTwoHand = "IsTwoHand";
        public string paramIsTwoPerson = "IsTwoPerson";
        public string paramIsDragging = "IsDragging";
        private RuntimeAnimatorController baseController;

        public bool useLegacyInput = false;
        public KeyCode dropKey = KeyCode.Q;
        public KeyCode throwKey = KeyCode.G;

        public float throwChargeTime = 0.75f;
        public float minThrowForce = 6f;
        public float maxThrowForce = 16f;
        public float throwUpBias = 0.08f;
        public float throwSpin = 8f;
        public Transform throwOrigin;
        public Camera viewCamera;

        public int maxOneHandSlots = 3;

        public PlayerCarryState State => state;
        private PlayerCarryState state = PlayerCarryState.Free;

        public float RequestedMoveSpeedMultiplier { get; private set; } = 1f;
        public float RequestedViewOcclusion { get; private set; } = 0f;

        private Vector2 moveInput;
        public bool IsAttemptingMove => moveInput.sqrMagnitude > 0.01f;

        public Transform TwoPersonHandleSocketOrFallback => twoPersonHandleSocket != null ? twoPersonHandleSocket : rightHandSocket;

        private readonly List<WorldItem> oneHandInventory = new List<WorldItem>();
        private int equippedIndex = -1;

        private WorldItem equippedOneHand;
        private WorldItem carryingTwoHand;
        private ItemSystemTwoPersonCargo grabbedTwoPerson;

        private bool isChargingThrow;
        private float throwCharge;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) baseController = animator.runtimeAnimatorController;

            if (stashRoot == null)
            {
                var stash = new GameObject("StashRoot");
                stash.transform.SetParent(transform, false);
                stashRoot = stash.transform;
            }

            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void Update()
        {
            if (!useLegacyInput) return;

            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            if (Input.GetKeyDown(dropKey)) DropOrRelease();
            if (Input.GetKeyDown(throwKey)) BeginThrowCharge();
            if (Input.GetKeyUp(throwKey)) EndThrowChargeAndThrow();

            if (isChargingThrow)
                throwCharge = Mathf.Min(throwChargeTime, throwCharge + Time.deltaTime);
        }

        public void SetMoveInput(Vector2 input) => moveInput = input;

        public bool TryPickupWorldItem(WorldItem wi)
        {
            if (wi == null) return false;
            if (wi.GetComponent<ItemSystemTwoPersonCargo>() != null) return false;

            var rig = wi.GetComponent<ItemSystem>();
            CarryType type = (rig != null) ? rig.ResolveCarryType(wi) : InferCarryTypeFromDefinition(wi);

            if (type == CarryType.OneHand) return PickupOneHand(wi);
            if (type == CarryType.TwoHandCargo) return PickupTwoHand(wi);
            return false;
        }

        public bool TryGrabTwoPerson(ItemSystemTwoPersonCargo cargo, HandleSide side = HandleSide.Auto)
        {
            if (cargo == null) return false;
            if (state == PlayerCarryState.CarryingTwoHand) return false;

            if (equippedOneHand != null)
                StashEquippedOneHand();

            if (!cargo.TryGrab(this, side)) return false;

            grabbedTwoPerson = cargo;
            UpdateStateFromTwoPerson();
            ApplySignalsFromRig(grabbedTwoPerson.GetComponent<ItemSystem>(), CarryType.TwoPersonCargo);

            return true;
        }

        public void ReleaseTwoPerson()
        {
            if (grabbedTwoPerson == null) return;

            grabbedTwoPerson.Release(this);
            grabbedTwoPerson = null;

            RestoreEquippedOneHandIfAny();
            SetState(equippedOneHand != null ? PlayerCarryState.HoldingOneHand : PlayerCarryState.Free);

            ApplySignalsFromRig(equippedOneHand != null ? equippedOneHand.GetComponent<ItemSystem>() : null,
                equippedOneHand != null ? CarryType.OneHand : CarryType.OneHand);
        }

        public void DropOrRelease()
        {
            if (grabbedTwoPerson != null) { ReleaseTwoPerson(); return; }
            if (carryingTwoHand != null) { DropTwoHand(); return; }
            if (equippedOneHand != null) { DropEquippedOneHand(); return; }
        }

        public void BeginThrowCharge()
        {
            if (equippedOneHand == null) return;
            if (state != PlayerCarryState.HoldingOneHand) return;

            isChargingThrow = true;
            throwCharge = 0f;
        }

        public void EndThrowChargeAndThrow()
        {
            if (!isChargingThrow) return;
            isChargingThrow = false;

            if (equippedOneHand == null) return;
            if (state != PlayerCarryState.HoldingOneHand) return;

            var wi = equippedOneHand;
            var rig = wi.GetComponent<ItemSystem>();

            float t = Mathf.Clamp01(throwCharge / Mathf.Max(0.0001f, throwChargeTime));
            float force = Mathf.Lerp(minThrowForce, maxThrowForce, t);

            Vector3 origin = throwOrigin != null ? throwOrigin.position :
                (rightHandSocket != null ? rightHandSocket.position : transform.position + Vector3.up);

            Vector3 forward = (viewCamera != null) ? viewCamera.transform.forward : transform.forward;
            Vector3 dir = (forward + Vector3.up * throwUpBias).normalized;

            Vector3 vel = dir * force;
            Vector3 ang = Random.onUnitSphere * throwSpin;

            oneHandInventory.Remove(wi);
            equippedOneHand = null;
            equippedIndex = -1;

            wi.transform.SetParent(null, true);
            wi.ignoreContainerAutoParent = false;
            wi.OnDropped(origin, vel);

            if (wi.rb != null)
                wi.rb.angularVelocity = ang;

            // Self damage on throw (no Durability component, use WorldItem)
            if (wi.useDurability)
            {
                int selfDmg = (rig != null) ? rig.throwSelfDamage : 5;
                wi.ApplyDamage(selfDmg);
            }

            if (oneHandInventory.Count > 0) EquipOneHandByIndex(0);
            else SetState(PlayerCarryState.Free);

            ApplySignalsFromRig(equippedOneHand != null ? equippedOneHand.GetComponent<ItemSystem>() : null,
                equippedOneHand != null ? CarryType.OneHand : CarryType.OneHand);
        }

        private CarryType InferCarryTypeFromDefinition(WorldItem wi)
        {
            if (wi == null || wi.definition == null) return CarryType.OneHand;
            switch (wi.definition.carryKind)
            {
                case CarryKind.OneHand: return CarryType.OneHand;
                case CarryKind.TwoHand: return CarryType.TwoHandCargo;
                case CarryKind.TwoPersonCargo: return CarryType.TwoPersonCargo;
                default: return CarryType.OneHand;
            }
        }

        private bool PickupOneHand(WorldItem wi)
        {
            if (oneHandInventory.Count >= maxOneHandSlots) return false;
            if (wi == null) return false;

            wi.ignoreContainerAutoParent = true;
            wi.OnPickedUp(false);
            ItemSystemVisualUtil.SetRenderersEnabled(wi.gameObject, false);

            wi.transform.SetParent(stashRoot, true);

            oneHandInventory.Add(wi);

            if (state == PlayerCarryState.Free || state == PlayerCarryState.HoldingOneHand)
                EquipOneHandByIndex(oneHandInventory.Count - 1);

            return true;
        }

        private bool PickupTwoHand(WorldItem wi)
        {
            if (carryingTwoHand != null) return false;
            if (grabbedTwoPerson != null) return false;

            if (equippedOneHand != null)
                StashEquippedOneHand();

            carryingTwoHand = wi;

            wi.ignoreContainerAutoParent = true;
            wi.OnPickedUp(false);

            AttachToSocket(wi, carryRoot, GripSlot.CarryGrip);
            ItemSystemVisualUtil.SetRenderersEnabled(wi.gameObject, true);

            SetState(PlayerCarryState.CarryingTwoHand);
            ApplySignalsFromRig(wi.GetComponent<ItemSystem>(), CarryType.TwoHandCargo);

            return true;
        }

        private void DropTwoHand()
        {
            if (carryingTwoHand == null) return;

            var wi = carryingTwoHand;
            carryingTwoHand = null;

            wi.transform.SetParent(null, true);
            wi.ignoreContainerAutoParent = false;
            wi.OnDropped(transform.position + transform.forward * 0.8f, Vector3.zero);

            RestoreEquippedOneHandIfAny();
            SetState(equippedOneHand != null ? PlayerCarryState.HoldingOneHand : PlayerCarryState.Free);

            ApplySignalsFromRig(equippedOneHand != null ? equippedOneHand.GetComponent<ItemSystem>() : null,
                equippedOneHand != null ? CarryType.OneHand : CarryType.OneHand);
        }

        private void DropEquippedOneHand()
        {
            if (equippedOneHand == null) return;

            var wi = equippedOneHand;

            oneHandInventory.Remove(wi);
            equippedOneHand = null;
            equippedIndex = -1;

            wi.transform.SetParent(null, true);
            wi.ignoreContainerAutoParent = false;
            wi.OnDropped(transform.position + transform.forward * 0.8f, Vector3.zero);

            if (oneHandInventory.Count > 0) EquipOneHandByIndex(0);
            else SetState(PlayerCarryState.Free);

            ApplySignalsFromRig(equippedOneHand != null ? equippedOneHand.GetComponent<ItemSystem>() : null,
                equippedOneHand != null ? CarryType.OneHand : CarryType.OneHand);
        }

        private void EquipOneHandByIndex(int index)
        {
            if (index < 0 || index >= oneHandInventory.Count) return;
            var wi = oneHandInventory[index];
            if (wi == null) return;

            if (equippedOneHand != null)
            {
                equippedOneHand.transform.SetParent(stashRoot, true);
                ItemSystemVisualUtil.SetRenderersEnabled(equippedOneHand.gameObject, false);
            }

            equippedOneHand = wi;
            equippedIndex = index;

            AttachToSocket(wi, rightHandSocket, GripSlot.GripR);
            ItemSystemVisualUtil.SetRenderersEnabled(wi.gameObject, true);

            SetState(PlayerCarryState.HoldingOneHand);
            ApplySignalsFromRig(wi.GetComponent<ItemSystem>(), CarryType.OneHand);
        }

        private void StashEquippedOneHand()
        {
            if (equippedOneHand == null) return;

            equippedOneHand.transform.SetParent(stashRoot, true);
            ItemSystemVisualUtil.SetRenderersEnabled(equippedOneHand.gameObject, false);

            equippedOneHand = null;
            equippedIndex = -1;

            SetState(PlayerCarryState.Free);
        }

        private void RestoreEquippedOneHandIfAny()
        {
            if (oneHandInventory.Count == 0) return;

            if (equippedIndex < 0 || equippedIndex >= oneHandInventory.Count)
                equippedIndex = 0;

            EquipOneHandByIndex(equippedIndex);
        }

        // =========================================================
        // DEBUG + ATTACH
        // =========================================================
        private void AttachToSocket(WorldItem wi, Transform socket, GripSlot gripSlot)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[AttachToSocket] START wi={(wi ? wi.name : "null")} socket={(socket ? socket.name : "null")} slot={gripSlot}");
#endif
            if (wi == null || socket == null) return;

            var rig = wi.GetComponent<ItemSystem>();
            Transform grip = (rig != null) ? rig.GetGrip(gripSlot) : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var rigsOnRoot = wi.GetComponents<ItemSystem>();
            var rigsInChildren = wi.GetComponentsInChildren<ItemSystem>(true);

            Debug.Log($"[AttachToSocket] rig={(rig ? rig.name : "null")} rootItemSystems={rigsOnRoot.Length} childItemSystems={rigsInChildren.Length}");
            for (int i = 0; i < rigsInChildren.Length; i++)
            {
                var r = rigsInChildren[i];
                Debug.Log($"[AttachToSocket]  - childRig[{i}] name={r.name} path={GetPath(r.transform)} gripR={(r.gripR ? GetPath(r.gripR) : "null")} carryGrip={(r.carryGrip ? GetPath(r.carryGrip) : "null")}");
            }

            Debug.Log($"[AttachToSocket] grip={(grip ? grip.name : "null")} gripPath={(grip ? GetPath(grip) : "null")}");
            if (rig != null)
                Debug.Log($"[AttachToSocket] rig.gripR={(rig.gripR ? GetPath(rig.gripR) : "null")} rig.carryGrip={(rig.carryGrip ? GetPath(rig.carryGrip) : "null")}");

            if (grip != null)
            {
                bool gripIsWiRoot = (grip == wi.transform);
                bool gripIsRigSelf = (rig != null && grip == rig.transform);
                Debug.Log($"[AttachToSocket] gripIsWiRoot={gripIsWiRoot} gripIsRigSelf={gripIsRigSelf} isChildOfWiRoot={grip.IsChildOf(wi.transform)}");

                float preDist = Vector3.Distance(grip.position, socket.position);
                float preAng = Quaternion.Angle(grip.rotation, socket.rotation);
                Debug.Log($"[AttachToSocket] PRE  grip->socket dist={preDist:F4} ang={preAng:F2}");
            }
#endif

            if (grip == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[AttachToSocket] grip==null => FALLBACK to socket origin (local zero)");
#endif
                wi.transform.SetParent(socket, true);
                wi.transform.localPosition = Vector3.zero;
                wi.transform.localRotation = Quaternion.identity;
                return;
            }

            wi.transform.SetParent(null, true);
            ItemSystemSnapUtil.SnapToSocket(wi.transform, grip, socket);
            wi.transform.SetParent(socket, true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            float postDist = Vector3.Distance(grip.position, socket.position);
            float postAng = Quaternion.Angle(grip.rotation, socket.rotation);
            Debug.Log($"[AttachToSocket] POST grip->socket dist={postDist:F4} ang={postAng:F2}");

            Debug.DrawLine(grip.position, socket.position, Color.red, 2f);
#endif
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
#endif

        private void LateUpdate()
        {
            if (grabbedTwoPerson != null)
            {
                UpdateStateFromTwoPerson();
                ApplySignalsFromRig(grabbedTwoPerson.GetComponent<ItemSystem>(), CarryType.TwoPersonCargo);
            }
        }

        private void UpdateStateFromTwoPerson()
        {
            if (grabbedTwoPerson == null) { SetState(PlayerCarryState.Free); return; }
            if (grabbedTwoPerson.IsDragging) SetState(PlayerCarryState.Dragging);
            else if (grabbedTwoPerson.IsFullyCarried) SetState(PlayerCarryState.TwoPerson_TwoSide);
            else SetState(PlayerCarryState.TwoPerson_OneSide);
        }

        private void SetState(PlayerCarryState s)
        {
            if (state == s) return;
            state = s;
            UpdateAnimatorBools();
        }

        private void ApplySignalsFromRig(ItemSystem rig, CarryType type)
        {
            RequestedMoveSpeedMultiplier = (rig != null) ? rig.moveSpeedMultiplier : 1f;
            RequestedViewOcclusion = (rig != null) ? rig.viewOcclusion : 0f;
            ApplyAnimatorOverride(rig, type);
        }

        private void ApplyAnimatorOverride(ItemSystem rig, CarryType type)
        {
            if (animator == null) return;

            if (baseController == null)
                baseController = animator.runtimeAnimatorController;

            if (rig == null)
            {
                if (baseController != null) animator.runtimeAnimatorController = baseController;
                return;
            }

            if (type == CarryType.OneHand && rig.oneHandOverride != null)
                animator.runtimeAnimatorController = rig.oneHandOverride;
            else if ((type == CarryType.TwoHandCargo || type == CarryType.TwoPersonCargo) && rig.twoHandOverride != null)
                animator.runtimeAnimatorController = rig.twoHandOverride;
            else if (baseController != null)
                animator.runtimeAnimatorController = baseController;
        }

        private void UpdateAnimatorBools()
        {
            if (animator == null) return;

            animator.SetBool(paramIsOneHand, state == PlayerCarryState.HoldingOneHand);
            animator.SetBool(paramIsTwoHand, state == PlayerCarryState.CarryingTwoHand);
            animator.SetBool(paramIsTwoPerson, state == PlayerCarryState.TwoPerson_OneSide || state == PlayerCarryState.TwoPerson_TwoSide);
            animator.SetBool(paramIsDragging, state == PlayerCarryState.Dragging);
        }
    }
}
