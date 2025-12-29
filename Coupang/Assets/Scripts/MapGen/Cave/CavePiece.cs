using UnityEngine;

/// <summary>
/// Optional marker component for cave room / corridor / cap prefabs.
/// Gives the generator a single place to query connectors without repeated GetComponents calls.
/// </summary>
public class CavePiece : MonoBehaviour
{
    private CaveConnector[] _connectors;

    public CaveConnector[] GetConnectors(bool includeInactive = true)
    {
        if (_connectors == null || _connectors.Length == 0)
        {
            _connectors = GetComponentsInChildren<CaveConnector>(includeInactive);
        }
        return _connectors;
    }
}
