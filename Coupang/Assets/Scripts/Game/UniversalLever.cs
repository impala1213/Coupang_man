using UnityEngine;

public class UniversalLever : MonoBehaviour
{
    [SerializeField] private float requiredHoldSeconds = 1.5f;
    [SerializeField] private GameSession session;

    private bool isFocused;
    private bool isPressed;
    private float holdTime;
    private bool hasFiredThisPress;

    private void Awake()
    {
        if (session == null)
        {
            session = GameSession.Instance;
        }
    }

    // New naming used by PlayerController
    public void OnFocusEnter()
    {
        isFocused = true;
        InteractionLock.SetLeverFocus(true);
        ResetHold();
    }

    public void OnFocusExit()
    {
        isFocused = false;
        InteractionLock.SetLeverFocus(false);
        ResetHold();
    }

    // Backward-compat wrappers (if some other code still calls FocusEnter/Exit)
    public void FocusEnter()
    {
        OnFocusEnter();
    }

    public void FocusExit()
    {
        OnFocusExit();
    }

    public void OnUsePressed()
    {
        if (!isFocused)
        {
            return;
        }

        isPressed = true;
        holdTime = 0f;
        hasFiredThisPress = false;
    }

    public void OnUseReleased()
    {
        isPressed = false;
        holdTime = 0f;
        hasFiredThisPress = false;
    }

    public void Tick(float deltaTime)
    {
        if (!isFocused)
        {
            return;
        }

        if (!isPressed)
        {
            return;
        }

        if (hasFiredThisPress)
        {
            return;
        }

        holdTime += deltaTime;

        if (holdTime >= requiredHoldSeconds)
        {
            hasFiredThisPress = true;
            FireLever();
        }
    }

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
