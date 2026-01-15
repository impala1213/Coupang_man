using UnityEngine;

[DisallowMultipleComponent]
public class WraithVengeanceMark : MonoBehaviour
{
    [Header("Creature Kills")]
    [Min(0)] public int creatureKills = 0;

    public void RegisterCreatureKill()
    {
        creatureKills = Mathf.Max(0, creatureKills + 1);
    }

    public void SetKills(int value)
    {
        creatureKills = Mathf.Max(0, value);
    }
}
