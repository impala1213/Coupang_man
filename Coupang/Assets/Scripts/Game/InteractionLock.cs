using UnityEngine;

public static class InteractionLock
{
    public static bool LeverHasFocus { get; private set; }
    public static bool TerminalHasFocus { get; private set; }

    /// <summary>
    /// True while a modal UI (e.g., monitor terminal) is open.
    /// Player input scripts should treat this as a global input lock.
    /// </summary>
    public static bool ModalUIOpen { get; private set; }

    public static void SetLeverFocus(bool hasFocus)
    {
        LeverHasFocus = hasFocus;
    }

    public static void SetTerminalFocus(bool hasFocus)
    {
        TerminalHasFocus = hasFocus;
    }

    public static void SetModalUIOpen(bool isOpen)
    {
        ModalUIOpen = isOpen;
    }
}
