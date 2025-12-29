using UnityEngine;

/// <summary>
/// Interactable cave entrance/exit that teleports the player.
/// Attach to entrancePrefab and exitPrefab (or it will be added at runtime).
/// </summary>
public class CaveEntranceTeleport : PlayerInteractableBase
{
    [Header("Teleport")]
    public Transform teleportTarget;
    [Tooltip("Optional return point (not required, but useful for UI/debug).")]
    public Transform returnPoint;
    [Min(0f)] public float cooldownSeconds = 0.25f;

    private float _cooldown;

    private void Update()
    {
        if (_cooldown > 0f)
            _cooldown -= Time.deltaTime;
    }

    public override void OnFocusEnter(PlayerController player)
    {
        InteractionLock.SetLeverFocus(true);
    }

    public override void OnFocusExit(PlayerController player)
    {
        InteractionLock.SetLeverFocus(false);
    }

    public override void OnUsePressed(PlayerController player)
    {
        if (_cooldown > 0f)
            return;

        if (teleportTarget == null)
        {
            Debug.LogWarning("CaveEntranceTeleport: teleportTarget is null.");
            return;
        }

        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }

        if (player == null)
        {
            Debug.LogWarning("CaveEntranceTeleport: PlayerController not found.");
            return;
        }

        _cooldown = cooldownSeconds;
        player.TeleportTo(teleportTarget.position, teleportTarget.rotation);
    }
}
