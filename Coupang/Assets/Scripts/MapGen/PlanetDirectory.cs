using UnityEngine;

[CreateAssetMenu(menuName = "Map/Planet Directory")]
public class PlanetDirectory : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public MapProfile profile;
        public int weight = 1;
    }

    public Entry[] entries;
    public int defaultSeed = 12345;

    public MapProfile GetRandomProfile(out int seed)
    {
        seed = defaultSeed;

        if (entries == null || entries.Length == 0)
            return null;

        int totalWeight = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry e = entries[i];
            if (e == null || e.profile == null || e.weight <= 0) continue;
            totalWeight += e.weight;
        }

        if (totalWeight <= 0)
            return null;

        int roll = Random.Range(int.MinValue, int.MaxValue);
        seed = roll;

        int pick = Mathf.Abs(roll) % totalWeight;
        int acc = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry e = entries[i];
            if (e == null || e.profile == null || e.weight <= 0) continue;

            acc += e.weight;
            if (pick < acc)
            {
                return e.profile;
            }
        }

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].profile != null)
                return entries[i].profile;
        }

        return null;
    }
}
