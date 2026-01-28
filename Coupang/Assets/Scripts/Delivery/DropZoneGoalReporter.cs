using UnityEngine;

/// <summary>
/// Attach this to the DropZone prefab root (the same object that has DeliveryDropZone).
/// When the DropZone becomes active, it registers itself as the radar goal once.
/// </summary>
[DefaultExecutionOrder(10000)]
public class DropZoneGoalReporter : MonoBehaviour
{
    private bool _sent;

    private void OnEnable()
    {
        if (_sent) return;
        _sent = true;

        RadarGoalRegistry.SetGoal(transform);
    }

    // In case someone enables/disables the object and wants re-bind
    public void ResendGoal()
    {
        _sent = false;
        OnEnable();
    }
}
