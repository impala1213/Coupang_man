using UnityEngine;

public static class WraithVengeance
{
    public static Transform CurrentTarget { get; private set; }
    public static int CurrentTargetKills { get; private set; }

    /// <summary>
    /// Call when a player kills a creature.
    /// </summary>
    public static void RegisterKiller(Transform player, int killCount)
    {
        if (player == null) return;
        CurrentTarget = player;
        CurrentTargetKills = Mathf.Max(0, killCount);
    }

    /// <summary>
    /// Optional: clear target after the wraith finishes its execution.
    /// </summary>
    public static void ClearIfTarget(Transform player)
    {
        if (player == null) return;
        if (CurrentTarget == player)
        {
            CurrentTarget = null;
            CurrentTargetKills = 0;
        }
    }
}
