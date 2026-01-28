using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Controls opening/closing a modal monitor UI panel.
/// 
/// Requirements:
/// - monitorPanelRoot should be a screen-space UI panel (disabled by default).
/// - Scene must have an EventSystem (Unity UI).
///
/// Behavior:
/// - When opened:
///   - InteractionLock.ModalUIOpen = true (blocks gameplay input in PlayerController)
///   - Unlocks and shows cursor
///   - Disables any extra input behaviours you provide (camera look, etc.)
/// - When closed:
///   - Restores cursor state
///   - Re-enables extra behaviours
///   - InteractionLock.ModalUIOpen = false
///
/// Input policy:
/// - While open, only mouse clicks (UI) and Escape are intended to work.
/// - Keyboard/gamepad input is blocked at gameplay-script level via InteractionLock.ModalUIOpen.
/// </summary>
public class MonitorTerminalUIController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Root panel GameObject to show/hide (e.g., MonitorPanel).")]
    public GameObject monitorPanelRoot;

    [Tooltip("Optional: set a default selectable (e.g., option A button) when opening.")]
    public GameObject defaultSelected;

    [Tooltip("Optional: UI close button. If assigned, it will call Close().")]
    public Button closeButton;

    [Header("Close Key")]
    public KeyCode closeKey = KeyCode.Escape;

    [Header("Extra Behaviours To Disable While Open (optional)")]
    [Tooltip("Disable these behaviours while UI is open (e.g., camera look scripts).")]
    public Behaviour[] disableWhileOpen;

    private bool _isOpen;

    private CursorLockMode _prevLockMode;
    private bool _prevCursorVisible;

    public bool HasPanel => monitorPanelRoot != null;

    private void Awake()
    {
        if (monitorPanelRoot != null)
            monitorPanelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseFromButton);
            closeButton.onClick.AddListener(CloseFromButton);
        }
    }

    private void OnDisable()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseFromButton);
    }

    private void Update()
    {
        if (!_isOpen) return;

        // Only allow ESC to close (mouse clicks handled by UI)
        if (Input.GetKeyDown(closeKey))
            Close();
    }

    public void Toggle()
    {
        if (_isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (_isOpen) return;

        if (monitorPanelRoot == null)
        {
            Debug.LogWarning("[MonitorTerminalUIController] monitorPanelRoot is not assigned. UI will not open.");
            return;
        }

        _isOpen = true;

        InteractionLock.SetModalUIOpen(true);
        monitorPanelRoot.SetActive(true);

        // Cursor for UI
        _prevLockMode = Cursor.lockState;
        _prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Disable extra behaviours
        if (disableWhileOpen != null)
        {
            for (int i = 0; i < disableWhileOpen.Length; i++)
            {
                if (disableWhileOpen[i] != null)
                    disableWhileOpen[i].enabled = false;
            }
        }

        // UI selection
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            if (defaultSelected != null)
                EventSystem.current.SetSelectedGameObject(defaultSelected);
        }
    }

    public void CloseFromButton()
    {
        Close();
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        InteractionLock.SetModalUIOpen(false);

        if (monitorPanelRoot != null)
            monitorPanelRoot.SetActive(false);

        // Restore cursor
        Cursor.lockState = _prevLockMode;
        Cursor.visible = _prevCursorVisible;

        // Re-enable extra behaviours
        if (disableWhileOpen != null)
        {
            for (int i = 0; i < disableWhileOpen.Length; i++)
            {
                if (disableWhileOpen[i] != null)
                    disableWhileOpen[i].enabled = true;
            }
        }
    }
}
