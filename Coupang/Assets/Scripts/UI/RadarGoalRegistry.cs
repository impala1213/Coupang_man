using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-goal binding for PlayerRadarUI without polling.
///
/// - DropZones call RegisterGoal/UnregisterGoal.
/// - A "primary" goal can be set (e.g., nearest incomplete destination) for highlighting.
/// - Backwards compatible: SetGoal() clears and sets a single goal.
/// </summary>
public static class RadarGoalRegistry
{
    private static PlayerRadarUI s_radar;

    private static readonly List<Transform> s_goals = new List<Transform>(16);
    private static readonly HashSet<int> s_goalIds = new HashSet<int>();

    private static Transform s_primary;

    /// <summary>All registered goals (drop zones).</summary>
    public static IReadOnlyList<Transform> Goals => s_goals;

    /// <summary>The highlighted goal (optional).</summary>
    public static Transform PrimaryGoal => s_primary;

    /// <summary>
    /// Backwards compatible: clears any existing goals and sets a single goal.
    /// </summary>
    public static void SetGoal(Transform goal)
    {
        ClearGoals();
        if (goal == null) return;
        RegisterGoal(goal);
        SetPrimary(goal);
    }

    public static void RegisterGoal(Transform goal)
    {
        if (goal == null) return;
        if (!goal.gameObject.scene.IsValid()) return;

        int id = goal.GetInstanceID();
        if (!s_goalIds.Add(id))
            return;

        s_goals.Add(goal);

        if (s_primary == null)
            s_primary = goal;

        TryRefreshUI();
    }

    public static void UnregisterGoal(Transform goal)
    {
        if (goal == null) return;

        int id = goal.GetInstanceID();
        if (!s_goalIds.Remove(id))
            return;

        s_goals.Remove(goal);

        if (s_primary == goal)
            s_primary = (s_goals.Count > 0) ? s_goals[0] : null;

        TryRefreshUI();
    }

    /// <summary>
    /// Sets the primary goal for highlighting. Does NOT remove other goals.
    /// If the goal is not registered yet, it will be registered.
    /// </summary>
    public static void SetPrimary(Transform goal)
    {
        if (goal != null)
            RegisterGoal(goal); // no-op if already registered

        s_primary = goal;
        TryRefreshUI();
    }

    public static void ClearGoals()
    {
        s_goals.Clear();
        s_goalIds.Clear();
        s_primary = null;
        TryRefreshUI();
    }

    /// <summary>
    /// Remove null goals (scene unload etc.). Safe to call any time.
    /// </summary>
    public static void CleanupNulls()
    {
        bool changed = false;

        for (int i = s_goals.Count - 1; i >= 0; i--)
        {
            if (s_goals[i] != null) continue;
            s_goals.RemoveAt(i);
            changed = true;
        }

        if (!changed) return;

        s_goalIds.Clear();
        for (int i = 0; i < s_goals.Count; i++)
            if (s_goals[i] != null)
                s_goalIds.Add(s_goals[i].GetInstanceID());

        if (s_primary == null && s_goals.Count > 0)
            s_primary = s_goals[0];

        TryRefreshUI();
    }

    private static void TryRefreshUI()
    {
        if (s_radar == null)
            s_radar = FindRadarUI();

        if (s_radar == null) return;

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
