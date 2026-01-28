using UnityEngine;

/// <summary>
/// One-shot goal binding for PlayerRadarUI without polling.
/// 
/// DropZone (or stage logic) calls RadarGoalRegistry.SetGoal once.
/// This registry finds/caches PlayerRadarUI (even if inactive) and assigns goalTarget.
/// 
/// NOTE:
/// - No DontDestroyOnLoad here. UI should be kept alive either by staying in Ship scene,
///   or by using UISceneCourier to move the HUD into the stage scene.
/// </summary>
public static class RadarGoalRegistry
{
    private static PlayerRadarUI s_radar;

    public static void SetGoal(Transform goal)
    {
        if (goal == null) return;

        if (s_radar == null)
            s_radar = FindRadarUI();

        if (s_radar == null)
            return;

        if (s_radar.goalTarget != goal)
            s_radar.goalTarget = goal;

        if (s_radar.isActiveAndEnabled)
            s_radar.ForceRefresh();
    }

    private static PlayerRadarUI FindRadarUI()
    {
        // Find even if inactive. Avoid prefab assets by requiring a valid scene.
        var all = Resources.FindObjectsOfTypeAll<PlayerRadarUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            if (r == null) continue;
            if (!r.gameObject.scene.IsValid()) continue;
            return r;
        }
        return null;
    }
}
