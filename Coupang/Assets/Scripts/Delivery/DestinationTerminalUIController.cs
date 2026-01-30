using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Modal UI controller for Destination terminals (planet drop zones).
/// Similar policy to MonitorTerminalUIController:
/// - sets InteractionLock.ModalUIOpen while open
/// - unlocks cursor
/// - allows clicking "Complete Delivery"
/// </summary>
public class DestinationTerminalUIController : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot; // disable by default
    public DestinationTerminalPanelUI panelUI;
    public Button closeButton;
    public Button completeButton;

    [Header("Optional")]
    public Behaviour[] disableWhileOpen; // camera look etc

    private DestinationTerminalInteractable _boundTerminal;

    private bool _cursorWasVisible;
    private CursorLockMode _cursorWasLockMode;

    private void Awake()
    {
        if (!panelUI && panelRoot)
            panelUI = panelRoot.GetComponentInChildren<DestinationTerminalPanelUI>(true);

        if (!panelRoot)
            panelRoot = gameObject;

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        if (completeButton != null)
        {
            completeButton.onClick.RemoveListener(OnCompleteClicked);
            completeButton.onClick.AddListener(OnCompleteClicked);
        }
    }

    public void Toggle(DestinationTerminalInteractable terminal)
    {
        if (IsOpen)
            Close();
        else
            Open(terminal);
    }

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    public void Open(DestinationTerminalInteractable terminal)
    {
        if (terminal == null) return;

        _boundTerminal = terminal;

        if (panelRoot != null)
            panelRoot.SetActive(true);

        InteractionLock.SetModalUIOpen(true);

        _cursorWasVisible = Cursor.visible;
        _cursorWasLockMode = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (disableWhileOpen != null)
        {
            for (int i = 0; i < disableWhileOpen.Length; i++)
                if (disableWhileOpen[i] != null)
                    disableWhileOpen[i].enabled = false;
        }

        if (panelUI != null)
            panelUI.Bind(terminal);

        // Ensure EventSystem selection for gamepad/mouse
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        InteractionLock.SetModalUIOpen(false);

        Cursor.visible = _cursorWasVisible;
        Cursor.lockState = _cursorWasLockMode;

        if (disableWhileOpen != null)
        {
            for (int i = 0; i < disableWhileOpen.Length; i++)
                if (disableWhileOpen[i] != null)
                    disableWhileOpen[i].enabled = true;
        }

        _boundTerminal = null;
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            Close();

        if (IsOpen && panelUI != null && _boundTerminal != null)
            panelUI.Refresh();
    }

    private void OnCompleteClicked()
    {
        if (_boundTerminal == null) return;
        _boundTerminal.TryCompleteDelivery();
    }
}
