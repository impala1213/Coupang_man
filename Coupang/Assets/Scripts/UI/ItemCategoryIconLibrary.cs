using UnityEngine;

/// <summary>
/// Maps ItemCategory -> Sprite icon for contract UI.
/// Create one asset and reference it from PlanetSelectionMonitorUI / ShipMonitorDestinationDisplay.
/// </summary>
[CreateAssetMenu(menuName = "UI/Item Category Icon Library", fileName = "ItemCategoryIconLibrary")]
public class ItemCategoryIconLibrary : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public ItemCategory category;
        public Sprite icon;
    }

    [Tooltip("Icons for each item category.")]
    public Entry[] entries;

    public Sprite GetIcon(ItemCategory category)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].category == category)
                return entries[i].icon;
        }
        return null;
    }
}
