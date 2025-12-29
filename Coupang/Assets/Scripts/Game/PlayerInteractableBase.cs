using UnityEngine;

/// <summary>
/// Base class for any object the player can focus and interact with using the action key (E).
///
/// PlayerController will:
/// - Raycast to find a PlayerInteractableBase
/// - Call OnFocusEnter/OnFocusExit when the focused interactable changes
/// - Call OnUsePressed/OnUseReleased on E down/up
/// - If IsHoldInteraction is true, call TickWhileHeld every frame while E is held
/// </summary>
public abstract class PlayerInteractableBase : MonoBehaviour
{
    [Header("Interactable")]
    [Tooltip("Optional short prompt text (for future UI).")]
    public string prompt = "Interact";

    /// <summary>
    /// If true, PlayerController treats this as a hold interaction and will call TickWhileHeld.
    /// </summary>
    public virtual bool IsHoldInteraction => false;

    public virtual void OnFocusEnter(PlayerController player) { }
    public virtual void OnFocusExit(PlayerController player) { }

    public virtual void OnUsePressed(PlayerController player) { }
    public virtual void OnUseReleased(PlayerController player) { }

    public virtual void TickWhileHeld(PlayerController player, float deltaTime) { }
}
