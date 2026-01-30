using UnityEngine;

/// <summary>
/// Attach this to the DropZone prefab root (the same object that has DeliveryDropZone).
/// Registers/unregisters this DropZone as a radar goal.
/// </summary>
[DefaultExecutionOrder(10000)]
public class DropZoneGoalReporter : MonoBehaviour
{
    private void OnEnable()
    {
        RadarGoalRegistry.RegisterGoal(transform);
    }

    private void OnDisable()
    {
        RadarGoalRegistry.UnregisterGoal(transform);
    }

    // In case someone enables/disables the object and wants re-bind
    public void ResendGoal()
    {
        RadarGoalRegistry.RegisterGoal(transform);
    }
}
