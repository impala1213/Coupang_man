using UnityEngine;
// If you put ItemDefinition in a namespace, add: using Game.Items;

[DisallowMultipleComponent]
public class ItemData : MonoBehaviour
{
    [Header("Definition (static data)")]
    public ItemDefinition definition;   // All static fields (id/name/icon/weight/stackSize/category/price...) live here.

    [Header("Runtime State (instance-only)")]
    public int durability = 100;        // current durability (per instance)
    public int stackCount = 1;          // current stack count (per instance)
    [SerializeField] private string instanceId; // unique per-instance id (for save/network)

    public string InstanceId => instanceId;

    void Awake()
    {
        if (string.IsNullOrEmpty(instanceId))
            instanceId = System.Guid.NewGuid().ToString("N");
    }
}
