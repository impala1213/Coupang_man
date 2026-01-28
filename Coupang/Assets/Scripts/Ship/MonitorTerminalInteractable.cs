using UnityEngine;

/// <summary>
/// Ship monitor/terminal interactable.
/// Uses the existing PlayerInteractableBase pipeline (ray focus + E press).
/// 
/// - Shows a control hint via InteractionLock.TerminalHasFocus.
/// - Opens/closes a modal terminal UI when used.
/// </summary>
public class MonitorTerminalInteractable : PlayerInteractableBase
{
    [Header("Terminal")]
    public MonitorTerminalUIController uiController;

    [Tooltip("If true, logs focus/use events to help debug interaction setup.")]
    public bool debugLog = false;

    private void Awake()
    {
        // Auto-wire to reduce scene setup mistakes.
        if (uiController == null)
            uiController = GetComponentInChildren<MonitorTerminalUIController>(true);
        if (uiController == null)
            uiController = GetComponentInParent<MonitorTerminalUIController>(true);
        if (uiController == null)
            uiController = FindObjectOfType<MonitorTerminalUIController>(true);
    }

    public override void OnFocusEnter(PlayerController player)
    {
        InteractionLock.SetTerminalFocus(true);
        if (debugLog)
            Debug.Log("[MonitorTerminalInteractable] FocusEnter");
    }

    public override void OnFocusExit(PlayerController player)
    {
        InteractionLock.SetTerminalFocus(false);
        if (debugLog)
            Debug.Log("[MonitorTerminalInteractable] FocusExit");
    }

    public override void OnUsePressed(PlayerController player)
    {
        if (uiController == null)
        {
            Debug.LogWarning("[MonitorTerminalInteractable] uiController is missing. Add MonitorTerminalUIController somewhere active in the scene and assign it (or keep it as a parent/child so auto-wire can find it).");
            return;
        }
        if (debugLog)
            Debug.Log("[MonitorTerminalInteractable] UsePressed -> Toggle");
        uiController.Toggle();
    }
}
