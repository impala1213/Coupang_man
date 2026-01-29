using UnityEngine;

[DisallowMultipleComponent]
public class ContractCargoMarker : MonoBehaviour
{
    [SerializeField] private bool wasPicked;

    public bool WasPicked => wasPicked;

    public void MarkPicked()
    {
        wasPicked = true;
    }

    public void ResetPicked()
    {
        wasPicked = false;
    }
}
