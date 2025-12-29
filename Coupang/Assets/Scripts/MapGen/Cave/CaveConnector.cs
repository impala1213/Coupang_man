using UnityEngine;

/// <summary>
/// Connection point used by the modular cave generator.
///
/// Authoring rules (prefab side):
/// - Place this on child transforms where pieces should connect.
/// - The transform's +Z axis (Transform.forward, blue arrow) should point OUTWARD from the piece.
/// - Two connectors connect when their connectorTag matches.
/// </summary>
public class CaveConnector : MonoBehaviour
{
    [Tooltip("Tag used to match connectors. Only connectors with the same tag will be connected.")]
    public string connectorTag = "Default";

    [Tooltip("If false, this connector will never be used for generation.")]
    public bool enabledForGeneration = true;

    [Tooltip("Optional: if true, generator may leave this connector open (no cap) even at the end.")]
    public bool allowOpenEnd = false;

    [HideInInspector] public bool isUsed;

    /// <summary>Outward direction of this connector (+Z).</summary>
    public Vector3 Out => transform.forward;

    public void MarkUsed()
    {
        isUsed = true;
    }
}
