using UnityEngine;

/// <summary>
/// Hold-to-activate lever.
/// Derives from PlayerInteractableBase so it can share the same focus/interaction pipeline
/// with other interactables.
/// </summary>
public class UniversalLever : PlayerInteractableBase
{
    [SerializeField] private float requiredHoldSeconds = 1.5f;
    [SerializeField] private GameSession session;

    private bool isFocused;
    private bool isPressed;
    private float holdTime;
    private bool hasFiredThisPress;

    public override bool IsHoldInteraction => true;

    private void Awake()
    {
        if (session == null)
        {
            session = GameSession.Instance;
        }
    }

    public override void OnFocusEnter(PlayerController player)
    {
        isFocused = true;
        InteractionLock.SetLeverFocus(true);
        ResetHold();
    }

    public override void OnFocusExit(PlayerController player)
    {
        isFocused = false;
        InteractionLock.SetLeverFocus(false);
        ResetHold();
    }

    public override void OnUsePressed(PlayerController player)
    {
        if (!isFocused)
            return;

        isPressed = true;
        holdTime = 0f;
        hasFiredThisPress = false;
    }

    public override void OnUseReleased(PlayerController player)
    {
        isPressed = false;
        holdTime = 0f;
        hasFiredThisPress = false;
    }

    public override void TickWhileHeld(PlayerController player, float deltaTime)
    {
        if (!isFocused)
            return;

        if (!isPressed)
            return;

        if (hasFiredThisPress)
            return;

        holdTime += deltaTime;

        if (holdTime >= requiredHoldSeconds)
        {
            hasFiredThisPress = true;
            FireLever();
        }
    }

    // Backward-compat wrappers (if some other code still calls FocusEnter/Exit)
    public void FocusEnter() => OnFocusEnter(null);
    public void FocusExit() => OnFocusExit(null);

    // Backward-compat wrappers (if some other code calls these)
    public void OnUsePressed() => OnUsePressed(null);
    public void OnUseReleased() => OnUseReleased(null);

    // Legacy Tick signature used by older PlayerController logic
    public void Tick(float deltaTime) => TickWhileHeld(null, deltaTime);

    private void FireLever()
    {
        if (session == null)
        {
            session = GameSession.Instance;
        }

        if (session != null)
        {
            session.OnContainerLeverPulled();
        }
        else
        {
            Debug.LogWarning("UniversalLever: GameSession not set.");
        }
    }

    private void ResetHold()
    {
        isPressed = false;
        holdTime = 0f;
        hasFiredThisPress = false;
    }
}
