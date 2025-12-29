using UnityEngine;

/// <summary>
/// A weighted list of feature definitions for a given planet (MapProfile).
/// The generator selects and places features BEFORE base terrain height is finalized.
/// </summary>
[CreateAssetMenu(menuName = "Map/Features/Feature Directory")]
public class FeatureDirectory : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public FeatureDefinition feature;

        [Tooltip("Weight used when randomly picking this feature type (higher = more likely).")]
        public int weight = 1;

        [Tooltip("Minimum number of instances of this feature to place (always guaranteed if placement succeeds).")]
        public int minCount = 0;

        [Tooltip("Maximum number of instances of this feature to place.")]
        public int maxCount = 1;
    }

    [Tooltip("Available feature entries for this directory.")]
    public Entry[] entries;

    [Header("Selection Budget")]
    [Tooltip("If true, the planner will enforce a TOTAL instance count and pick features by weight until the budget is filled.\nIf false, each entry independently rolls a count between minCount..maxCount.")]
    public bool useTotalBudget = true;

    [Tooltip("Minimum total number of feature instances (across all entries).")]
    public int minTotalInstances = 0;

    [Tooltip("Maximum total number of feature instances (across all entries).")]
    public int maxTotalInstances = 6;

    [Header("Placement")]
    [Tooltip("Number of random candidate samples per feature instance.")]
    public int placementTriesPerInstance = 200;

    [Tooltip("When usingTotalBudget, max attempts to fill the budget before giving up (prevents infinite loops if placement is impossible).")]
    public int budgetFillMaxAttempts = 200;

    [Tooltip("If true, attempt to avoid placing features too close to the world boundary.")]
    public bool keepMarginFromBoundary = true;

    [Min(0f)]
    [Tooltip("Additional boundary margin in world units.")]
    public float boundaryMargin = 2f;
}
