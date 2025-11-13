using UnityEngine;

public static class InteractionLock
{
    public static bool LeverHasFocus { get; private set; }

    public static void SetLeverFocus(bool hasFocus)
    {
        LeverHasFocus = hasFocus;
    }
}
