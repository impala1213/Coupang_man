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
        public static void SnapToSocket(Transform itemRoot, Transform itemGrip, Transform socket)
        {
            if (itemRoot == null || itemGrip == null || socket == null) return;

            Quaternion deltaRot = socket.rotation * Quaternion.Inverse(itemGrip.rotation);
            itemRoot.rotation = deltaRot * itemRoot.rotation;

            Vector3 deltaPos = socket.position - itemGrip.position;
            itemRoot.position += deltaPos;
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
            return slot == GripSlot.CarryGrip ? (carryGrip != null ? carryGrip : gripR) : gripR;
        }

        public CarryType ResolveCarryType(WorldItem wi)
        {
            if (modeOverride == CarryModeOverride.OneHand) return CarryType.OneHand;
            if (modeOverride == CarryModeOverride.TwoHand) return CarryType.TwoHandCargo;
            if (modeOverride == CarryModeOverride.TwoPerson) return CarryType.TwoPersonCargo;

            if (wi != null && wi.GetComponent<ItemSystemTwoPersonCargo>() != null) return CarryType.TwoPersonCargo;

            if (wi != null && wi.definition != null)
            {
                if (wi.definition.itemType == ItemType.Cargo) return CarryType.TwoHandCargo;
                if (wi.definition.slotSize >= 2) return CarryType.TwoHandCargo;
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
            if (wi.definition.itemType == ItemType.Cargo) return CarryType.TwoHandCargo;
            if (wi.definition.slotSize >= 2) return CarryType.TwoHandCargo;
            return CarryType.OneHand;
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

        private void AttachToSocket(WorldItem wi, Transform socket, GripSlot gripSlot)
        {
            if (wi == null || socket == null) return;

            var rig = wi.GetComponent<ItemSystem>();
            Transform grip = (rig != null) ? rig.GetGrip(gripSlot) : null;

            if (grip == null)
            {
                wi.transform.SetParent(socket, true);
                wi.transform.localPosition = Vector3.zero;
                wi.transform.localRotation = Quaternion.identity;
                return;
            }

            wi.transform.SetParent(null, true);
            ItemSystemSnapUtil.SnapToSocket(wi.transform, grip, socket);
            wi.transform.SetParent(socket, true);
        }

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
