// Assets/Scripts/Item/ItemDefinition.cs
using UnityEngine;

public enum ItemType
{
    Tool,
    Consumable,
    Cargo,
    Special
}

public enum ItemCategory
{
    None = 0,
    Food,
    Weapon,
    Electronics,
    Luxury,
    Energy,
    Medicine,
    Antique,
    Ore,
    Carrier
}

[CreateAssetMenu(menuName = "Game/Item Definition", fileName = "NewItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    // 式式 Identity/UI 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Identity")]
    public string itemId;                  // unique key (e.g., "food_crate_a")
    public string displayName;             // UI fallback name (localization can override)
    public Sprite icon;                    // UI icon (optional)

    // 式式 Classification 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Classification")]
    public ItemType itemType = ItemType.Tool;         // gameplay branching (used by inventory/use logic)
    public ItemCategory category = ItemCategory.None; // taxonomy for quests/economy
    public bool isCarrier = false;                    // true if this item is the carrier itself

    // 式式 Inventory 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Inventory")]
    [Range(1, 5)] public int slotSize = 1;           // contiguous hotbar slots required

    // 式式 Physics & Carrier Stacking 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Physics & Carrier Stacking")]
    [Tooltip("Mass used in balance/difficulty calculations (kg).")]
    public float weight = 1f;

    [Tooltip("Box proxy used WHEN LOADED ON THE CARRIER (meters). Y is stack height increment.")]
    public Vector3 stackSize = new Vector3(0.40f, 0.40f, 0.30f);

    [Tooltip("Optional prefab to visualize this item on the carrier instead of a primitive cube.")]
    public GameObject stackVisualPrefab;

    [Tooltip("Fallback color for the primitive cube when no stack prefab is provided.")]
    public Color stackColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    // 式式 Carrier Mount (optional) 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Carrier Mount (optional)")]
    [Tooltip("Local position relative to CarrierSlot pivot. (0,0,0) means slot center.")]
    public Vector3 carrierLocalPosition = Vector3.zero;

    [Tooltip("Local Euler rotation relative to CarrierSlot pivot.")]
    public Vector3 carrierLocalEuler = Vector3.zero;

    [Tooltip("Local scale relative to CarrierSlot pivot. (0,0,0) is treated as (1,1,1).")]
    public Vector3 carrierLocalScale = Vector3.one;

    // 式式 World Prefab 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("World Prefab")]
    [Tooltip("Prefab used when the item exists in the world or is dropped.")]
    public GameObject worldPrefab;

    // 式式 Breakability 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Breakability")]
    public bool breakable = false;
    public float breakImpulseThreshold = 15f;
    public GameObject brokenPrefab;

    // 式式 Economy (optional) 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    [Header("Economy (optional)")]
    public int baseValue = 0;

    // Convenience: by design every item can be loaded on the carrier
    public bool IsCargoLike => true;

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式
    // Editor-time helpers: auto-fit stackSize from stackVisualPrefab bounds
    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式

    private void OnValidate()
    {
        // If you want automatic fitting, set stackSize to (0,0,0) in the inspector.
        // When a visual prefab is assigned and stackSize is zero, it will auto-fit.
        if (stackVisualPrefab != null && stackSize == Vector3.zero)
        {
            AutoFitStackSizeFromVisualPrefabInternal();
        }
    }

    /// <summary>
    /// Context menu entry to manually auto-fit stackSize from stackVisualPrefab.
    /// Right-click the asset in the inspector and choose this.
    /// </summary>
    [ContextMenu("Auto Fit Stack Size From Visual Prefab")]
    public void AutoFitStackSizeFromVisualPrefab()
    {
        AutoFitStackSizeFromVisualPrefabInternal();
    }

    /// <summary>
    /// Instantiates the stackVisualPrefab temporarily, computes combined bounds
    /// from renderers (or colliders as fallback), and uses that size as stackSize.
    /// </summary>
    private void AutoFitStackSizeFromVisualPrefabInternal()
    {
        if (!stackVisualPrefab)
        {
            Debug.LogWarning("[ItemDefinition] No stackVisualPrefab assigned, cannot auto-fit stackSize.", this);
            return;
        }

        GameObject temp = Instantiate(stackVisualPrefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.position = Vector3.zero;
        temp.transform.rotation = Quaternion.identity;
        temp.transform.localScale = Vector3.one;

        bool hasBounds = false;
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);

        // Prefer renderers for visual size
        Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (!r) continue;

            if (!hasBounds)
            {
                combined = r.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(r.bounds);
            }
        }

        // Fallback to colliders if there were no renderers
        if (!hasBounds)
        {
            Collider[] colliders = temp.GetComponentsInChildren<Collider>();
            foreach (var c in colliders)
            {
                if (!c) continue;

                if (!hasBounds)
                {
                    combined = c.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(c.bounds);
                }
            }
        }

        if (!hasBounds)
        {
            Debug.LogWarning("[ItemDefinition] Could not find any Renderer or Collider to measure size from stackVisualPrefab.", this);
        }
        else
        {
            // Use world-space size as stackSize. This is usually fine since the
            // temporary instance is created with identity transform.
            Vector3 size = combined.size;

            // Guard against extremely small or zero sizes to avoid issues.
            const float minDimension = 0.01f;
            size.x = Mathf.Max(minDimension, size.x);
            size.y = Mathf.Max(minDimension, size.y);
            size.z = Mathf.Max(minDimension, size.z);

            stackSize = size;
        }

        // Clean up the temporary instance immediately
        DestroyImmediate(temp);
    }
}
