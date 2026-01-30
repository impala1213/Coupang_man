using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// For multi-destination missions:
/// - Register all Destination terminals
/// - Keep radar "primary" goal pointed at the nearest incomplete destination
///
/// Note: all drop zones are registered via DropZoneGoalReporter.
/// </summary>
public static class DestinationGoalRegistry
{
    private static readonly List<DestinationTerminalInteractable> s_terminals = new List<DestinationTerminalInteractable>(8);

    public static void Register(DestinationTerminalInteractable t)
    {
        if (t == null) return;
        if (!s_terminals.Contains(t))
            s_terminals.Add(t);

        UpdatePrimaryGoal();
    }

    public static void Unregister(DestinationTerminalInteractable t)
    {
        if (t == null) return;
        s_terminals.Remove(t);
        UpdatePrimaryGoal();
    }

    public static void NotifyDestinationCompleted()
    {
        UpdatePrimaryGoal();
    }

    /// <summary>
    /// Called when a new set of destinations was spawned.
    /// Forces a goal refresh.
    /// </summary>
    public static void NotifyDestinationsSpawned()
    {
        UpdatePrimaryGoal();
    }

    public static void UpdatePrimaryGoal()
    {
        var player = FindPlayer();
        if (player == null) return;

        DestinationTerminalInteractable best = null;
        float bestD2 = float.MaxValue;

        for (int i = 0; i < s_terminals.Count; i++)
        {
            var t = s_terminals[i];
            if (t == null) continue;
            if (t.IsCompleted) continue;

            var dz = t.dropZone;
            if (dz == null) continue;

            Vector3 p = dz.transform.position;
            float d2 = (p - player.position).sqrMagnitude;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = t;
            }
        }

        if (best != null && best.dropZone != null)
        {
            RadarGoalRegistry.SetPrimary(best.dropZone.transform);
        }
        else
        {
            // All complete (or none found)
            RadarGoalRegistry.SetPrimary(null);
        }
    }

    private static Transform FindPlayer()
    {
        // Find even if inactive (avoid prefabs)
        var all = Resources.FindObjectsOfTypeAll<PlayerController>();
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p == null) continue;
            if (!p.gameObject.scene.IsValid()) continue;
            return p.transform;
        }
        return null;
    }
}
