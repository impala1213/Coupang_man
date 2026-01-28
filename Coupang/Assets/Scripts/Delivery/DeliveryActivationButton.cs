using UnityEngine;

/// <summary>
/// Hold-to-activate button near the drop zone.
/// When activated, it seals the zone (spawns shield) and pays money.
/// </summary>
public class DeliveryActivationButton : PlayerInteractableBase
{
    [SerializeField] private DeliveryDropZone dropZone;
    [SerializeField] private float requiredHoldSeconds = 1.2f;
    [SerializeField] private bool autoUseContractMinItems = true;
    [SerializeField] private DeliveryContractDefinition contract; // optional

    private bool _focused;
    private bool _pressed;
    private float _hold;
    private bool _fired;

    public override bool IsHoldInteraction => true;

    private void Awake()
    {
        if (!dropZone)
            dropZone = GetComponentInParent<DeliveryDropZone>();
    }

    public void BindContract(DeliveryContractDefinition def)
    {
        contract = def;
    }

    public override void OnFocusEnter(PlayerController player)
    {
        _focused = true;
        ResetHold();
    }

    public override void OnFocusExit(PlayerController player)
    {
        _focused = false;
        ResetHold();
    }

    public override void OnUsePressed(PlayerController player)
    {
        if (!_focused) return;
        _pressed = true;
        _hold = 0f;
        _fired = false;
    }

    public override void OnUseReleased(PlayerController player)
    {
        _pressed = false;
        _hold = 0f;
        _fired = false;
    }

    public override void TickWhileHeld(PlayerController player, float deltaTime)
    {
        if (!_focused || !_pressed || _fired) return;
        if (dropZone == null) return;
        if (dropZone.IsActivated) return;

        _hold += deltaTime;
        if (_hold >= requiredHoldSeconds)
        {
            _fired = true;
            int min = 1;
            if (autoUseContractMinItems && contract != null)
                min = Mathf.Max(0, contract.minItemsToActivate);
            dropZone.TryActivate(min);
        }
    }

    private void ResetHold()
    {
        _pressed = false;
        _hold = 0f;
        _fired = false;
    }
}
