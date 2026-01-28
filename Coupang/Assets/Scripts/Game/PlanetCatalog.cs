using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-scene planet list for authored (prebuilt) maps.
/// Each entry points to a gameplay scene that is loaded additively from Ship.
/// </summary>
[CreateAssetMenu(menuName = "Game/Planet Catalog", fileName = "PlanetCatalog")]
public class PlanetCatalog : ScriptableObject
{
    [System.Serializable]
    public class PlanetEntry
    {
        [Tooltip("Optional id for debugging/UI.")]
        public string planetId;

        [Tooltip("Optional display name for UI. If empty, planetId or scene name will be used.")]
        public string displayName;

        [Tooltip("Optional planet type label for UI (e.g., Winterland, Desert, Toxic).")]
        public string planetType;

        [Tooltip("Gameplay scene name to load additively. Must be in Build Settings.")]
        public string gameplaySceneName;

        [Tooltip("Optional contract pool for this planet.")]
        public DeliveryContractDirectory contractDirectory;

        [Tooltip("Danger/risk level (0-100). Used to display warning icons on UIs/ship monitors.")]
        [Range(0, 100)]
        public int riskLevel = 0;
        public int weight = 1;

        public string GetDisplayLabel()
        {
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
            if (!string.IsNullOrWhiteSpace(planetId)) return planetId;
            if (!string.IsNullOrWhiteSpace(gameplaySceneName)) return gameplaySceneName;
            return "Planet";
        }
    }

    public PlanetEntry[] entries;

    public PlanetEntry GetRandomPlanet()
    {
        if (entries == null || entries.Length == 0)
            return null;

        int total = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (!IsValidEntry(e)) continue;
            total += Mathf.Max(0, e.weight);
        }

        if (total <= 0)
            return FirstValid();

        int pick = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (!IsValidEntry(e)) continue;

            acc += Mathf.Max(0, e.weight);
            if (pick < acc)
                return e;
        }

        return FirstValid();
    }

    /// <summary>
    /// Returns up to count distinct planets, weighted, without replacement.
    /// If fewer than count valid entries exist, the array will be shorter.
    /// </summary>
    public PlanetEntry[] GetRandomPlanetsDistinct(int count)
    {
        count = Mathf.Clamp(count, 0, 32);
        if (count <= 0) return new PlanetEntry[0];
        if (entries == null || entries.Length == 0) return new PlanetEntry[0];

        var pool = new List<PlanetEntry>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (IsValidEntry(e))
                pool.Add(e);
        }

        if (pool.Count == 0) return new PlanetEntry[0];

        var result = new List<PlanetEntry>(Mathf.Min(count, pool.Count));

        for (int k = 0; k < count; k++)
        {
            if (pool.Count == 0) break;
            var picked = PickWeighted(pool);
            if (picked == null) break;
            result.Add(picked);
            pool.Remove(picked);
        }

        return result.ToArray();
    }

    private static PlanetEntry PickWeighted(List<PlanetEntry> pool)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++)
            total += Mathf.Max(0, pool[i].weight);

        if (total <= 0)
            return pool[Random.Range(0, pool.Count)];

        int pick = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(0, pool[i].weight);
            if (pick < acc)
                return pool[i];
        }

        return pool[0];
    }

    private bool IsValidEntry(PlanetEntry e)
    {
        if (e == null) return false;
        if (e.weight <= 0) return false;
        if (string.IsNullOrEmpty(e.gameplaySceneName)) return false;
        return true;
    }

    private PlanetEntry FirstValid()
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
        {
            if (IsValidEntry(entries[i]))
                return entries[i];
        }
        return null;
    }
}
