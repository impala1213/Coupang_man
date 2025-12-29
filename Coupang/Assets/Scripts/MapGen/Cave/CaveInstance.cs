using UnityEngine;

/// <summary>
/// Runtime container holding references for a generated cave instance.
/// </summary>
public class CaveInstance : MonoBehaviour
{
    [Header("Runtime Links")]
    public Transform outsideReturnPoint;
    public Transform insideEntryPoint;
}
