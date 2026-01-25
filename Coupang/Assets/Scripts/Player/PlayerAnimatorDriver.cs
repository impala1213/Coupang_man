// Assets/Scripts/Player/PlayerAnimatorDriver.cs
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAnimatorDriver : MonoBehaviour
{
    [Header("Refs")]
    public PlayerController player;
    public InventorySystem inventory;
    public Animator animator;

    [Header("Animator Parameters")]
    public string speedParam = "Speed";
    public string holdPoseParam = "HoldPose";
    public string fallDownTrigger = "FallDown";

    [Header("Ability (optional)")] 
    [Tooltip("Keeps default Idle/Walk flow unless enabled.")] 
    public bool enableAbilityInput = false;
    public KeyCode abilityKey = KeyCode.Mouse1;
    public string abilityTrigger = "Ability";

    [Header("Animator State Names (optional fallback)")]
    [Tooltip("If you don't add an Animation Event on the stand clip, this fallback can unlock input when this state finishes.")]
    public string standStateName = "PlayerStand";
    public int baseLayerIndex = 0;

    [Header("Upper Body Layer (optional)")]
    [Tooltip("If >= 0, driver can force the upper-body layer weight to 0 during knockdown/stand.")]
    public int upperBodyLayerIndex = 1;
    public bool controlUpperBodyLayerWeight = false;

    private bool _waitingForStandFinish;

    private void Awake()
    {
        if (!player) player = GetComponentInParent<PlayerController>() ?? GetComponent<PlayerController>();
        if (!inventory && player) inventory = player.inventory;
        if (!animator) animator = GetComponentInChildren<Animator>(true);

        if (player != null)
            player.OnKnockdownStarted += OnKnockdownStarted;
    }

    private void OnDestroy()
    {
        if (player != null)
            player.OnKnockdownStarted -= OnKnockdownStarted;
    }

    private void Update()
    {
        if (!player || !animator) return;

        // Speed (Idle/Walk)
        animator.SetFloat(speedParam, player.PlanarSpeed);

        // Hold pose (Upper body overlay)
        int pose = ComputeHoldPose();
        bool knockLocked = player.IsKnockdownLocked;

        if (knockLocked)
        {
            pose = 0;
            _waitingForStandFinish = true;
        }

        animator.SetInteger(holdPoseParam, pose);

        // Ability (optional): only triggers when enabled, and control is not locked.
        if (enableAbilityInput && !player.IsControlLocked && Input.GetKeyDown(abilityKey))
            animator.SetTrigger(abilityTrigger);

        if (controlUpperBodyLayerWeight && upperBodyLayerIndex >= 0 && upperBodyLayerIndex < animator.layerCount)
        {
            float w = (pose == 0) ? 0f : 1f;
            if (knockLocked) w = 0f;
            animator.SetLayerWeight(upperBodyLayerIndex, w);
        }

        // Fallback unlock: if we're waiting and stand state finished, unlock.
        if (_waitingForStandFinish && !animator.IsInTransition(baseLayerIndex))
        {
            var st = animator.GetCurrentAnimatorStateInfo(baseLayerIndex);
            if (st.IsName(standStateName) && st.normalizedTime >= 0.98f)
            {
                AnimEvent_StandFinished();
            }
        }
    }

    private int ComputeHoldPose()
    {
        if (!inventory) return 0;

        var def = inventory.ActiveDef();
        if (!def) return 0;

        switch (def.carryKind)
        {
            case CarryKind.OneHand: return 1;
            case CarryKind.TwoHand: return 2;
            default: return 0;
        }
    }

    private void OnKnockdownStarted()
    {
        if (!animator) return;

        animator.ResetTrigger(fallDownTrigger);
        animator.SetTrigger(fallDownTrigger);
        _waitingForStandFinish = true;
    }

    /// <summary>
    /// Animation Event hook (recommended):
    /// Add an Animation Event at the END of PlayerStand clip calling this method.
    /// This is the cleanest way to ensure input unlock exactly at the right time.
    /// </summary>
    public void AnimEvent_StandFinished()
    {
        if (!_waitingForStandFinish) return;
        _waitingForStandFinish = false;

        if (player != null)
            player.ClearKnockdownLock();
    }
}
