using UnityEngine;

public class ParentDebugLogger : MonoBehaviour
{
    private void Awake()
    {
        Debug.Log($"[ParentDebug] {name} Awake, parent = {transform.parent}", this);
    }

    private void OnTransformParentChanged()
    {
        Debug.Log($"[ParentDebug] {name} parent changed to {transform.parent}", this);
    }
}
