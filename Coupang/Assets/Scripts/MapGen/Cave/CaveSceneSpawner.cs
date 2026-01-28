using UnityEngine;

/// <summary>
/// Prebuilt-map cave bootstrapper.
///
/// The legacy surface generation pipeline (VoxelTerrainModule + FeaturePlanner) used to spawn cave entrances
/// and interiors automatically. Since the project now uses authored maps, cave entrances are placed manually
/// in the scene (with CaveEntranceLink). This spawner generates the modular interiors and wires teleports.
///
/// Usage:
/// - Place CaveEntranceLink on each entrance object (author the entrance prefab/mesh in the map).
/// - Add CaveSceneSpawner anywhere in the gameplay scene (often on StageContext).
/// - Optionally assign cavesRoot to control where generated interiors live.
/// </summary>
[DisallowMultipleComponent]
public class CaveSceneSpawner : MonoBehaviour
{
    [Header("Discovery")]
    [Tooltip("If true, automatically finds all CaveEntranceLink components in the active scene on Start().")]
    public bool autoFindLinks = true;

    [Tooltip("Optional explicit list. If empty and autoFindLinks is true, the spawner will find links automatically.")]
    public CaveEntranceLink[] links;

    [Header("Generation")]
    [Tooltip("Parent under which generated cave interiors are created. If null, one will be created under this object.")]
    public Transform cavesRoot;

    [Tooltip("Base seed used when a link does not override it.")]
    public int baseSeed = 12345;

    [Tooltip("Extra spacing between multiple interiors (world units). Applied along world +X.")]
    public float interiorInstanceSpacing = 220f;

    private void Start()
    {
        GenerateAll();
    }

    public void GenerateAll()
    {
        if (autoFindLinks)
        {
            var found = FindObjectsByType<CaveEntranceLink>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (found != null && found.Length > 0)
            {
                // Only generate links that belong to the same scene as this spawner.
                int count = 0;
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && found[i].gameObject.scene == gameObject.scene)
                        count++;
                }

                if (count == found.Length)
                {
                    links = found;
                }
                else
                {
                    links = new CaveEntranceLink[count];
                    int w = 0;
                    for (int i = 0; i < found.Length; i++)
                    {
                        var l = found[i];
                        if (l != null && l.gameObject.scene == gameObject.scene)
                            links[w++] = l;
                    }
                }
            }
        }

        if (links == null || links.Length == 0)
            return;

        if (cavesRoot == null)
        {
            Transform existing = transform.Find("CaveInteriors");
            if (existing != null)
                cavesRoot = existing;
            else
            {
                var go = new GameObject("CaveInteriors");
                go.transform.SetParent(transform, false);
                cavesRoot = go.transform;
            }
        }

        int instanceIndex = 0;
        for (int i = 0; i < links.Length; i++)
        {
            var link = links[i];
            if (link == null) continue;
            if (link.caveDefinition == null) continue;
            if (link.HasGenerated) continue;

            GenerateOne(link, instanceIndex);
            instanceIndex++;
        }
    }

    private void GenerateOne(CaveEntranceLink link, int instanceIndex)
    {
        CaveFeatureDefinition def = link.caveDefinition;
        if (def == null) return;

        // Determine interior origin
        Vector3 interiorOrigin;

        if (link.interiorOrigin != null)
        {
            interiorOrigin = link.interiorOrigin.position;
        }
        else
        {
            float spacing = instanceIndex * Mathf.Max(0f, interiorInstanceSpacing);

            if (def.interiorPlacementMode == CaveFeatureDefinition.InteriorPlacementMode.ManualOffset)
            {
                interiorOrigin = transform.position + def.interiorBaseOffset + Vector3.right * spacing;
            }
            else
            {
                float y = link.transform.position.y - Mathf.Max(0f, def.interiorDepthBelowSurface);
                interiorOrigin = new Vector3(link.transform.position.x, y, link.transform.position.z);
                interiorOrigin += def.interiorPlanarOffset;
                interiorOrigin += Vector3.right * spacing;
            }
        }

        // RNG
        int seed = link.seedOverride >= 0 ? link.seedOverride : (baseSeed + instanceIndex * 1337);
        Rng rng = new Rng(seed);

        Transform parent = link.cavesRootOverride != null ? link.cavesRootOverride : cavesRoot;
        if (parent == null) parent = transform;

        string caveName = GetDefinitionLabel(def);
        string instanceName = $"{caveName}_Interior_{instanceIndex}";

        var genRes = CaveDungeonGenerator.Generate(def, rng, interiorOrigin, parent, instanceName);
        Transform insideEntry = genRes.insideEntryPoint != null ? genRes.insideEntryPoint : genRes.root;

        if (insideEntry == null)
        {
            Debug.LogWarning($"CaveSceneSpawner: '{instanceName}' generated but insideEntry is null.");
            return;
        }

        // Create outside return point under the entrance
        Transform outsideReturnPoint = EnsureOutsideReturnPoint(link.transform);

        // Wire outside entrance teleport -> inside entry
        CaveEntranceTeleport outsideTele = link.GetComponentInChildren<CaveEntranceTeleport>(true);
        if (outsideTele == null)
            outsideTele = link.gameObject.AddComponent<CaveEntranceTeleport>();

        outsideTele.teleportTarget = insideEntry;
        outsideTele.returnPoint = outsideReturnPoint;

        // Wire inside exit teleport -> outside return
        bool startedFromExitPiece = def.useExitPrefabAsStartPiece && def.exitPrefab != null;

        if (startedFromExitPiece)
        {
            Transform exitRoot = genRes.startPieceRoot != null ? genRes.startPieceRoot : genRes.root;
            if (exitRoot != null)
            {
                CaveEntranceTeleport insideTele = exitRoot.GetComponentInChildren<CaveEntranceTeleport>(true);
                if (insideTele == null)
                    insideTele = exitRoot.gameObject.AddComponent<CaveEntranceTeleport>();

                insideTele.teleportTarget = outsideReturnPoint;
                insideTele.returnPoint = insideEntry;
            }
        }
        else
        {
            GameObject exitPrefab = def.exitPrefab != null ? def.exitPrefab : def.entrancePrefab;
            if (exitPrefab != null)
            {
                Transform exitParent = genRes.root != null ? genRes.root : parent;
                GameObject exitGO = Instantiate(exitPrefab, insideEntry.position, insideEntry.rotation, exitParent);
                exitGO.name = $"{caveName}_Exit_{instanceIndex}";

                CaveEntranceTeleport insideTele = exitGO.GetComponentInChildren<CaveEntranceTeleport>(true);
                if (insideTele == null)
                    insideTele = exitGO.AddComponent<CaveEntranceTeleport>();

                insideTele.teleportTarget = outsideReturnPoint;
                insideTele.returnPoint = insideEntry;
            }
        }

        // Store links on instance
        if (genRes.instance != null)
        {
            genRes.instance.outsideReturnPoint = outsideReturnPoint;
            genRes.instance.insideEntryPoint = insideEntry;
            link.generatedInstance = genRes.instance;
        }
    }

    private static string GetDefinitionLabel(CaveFeatureDefinition def)
    {
        if (def == null)
            return "Cave";

        // Use the ScriptableObject asset name as a stable identifier.
        // (FeatureDefinition.id was removed when Features were refactored out.)
        string label = def.name;
        if (string.IsNullOrWhiteSpace(label))
            label = "Cave";
        return label;
    }




    private static Transform EnsureOutsideReturnPoint(Transform entrance)
    {
        Transform t = entrance.Find("OutsideReturnPoint");
        if (t != null)
            return t;

        GameObject go = new GameObject("OutsideReturnPoint");
        go.transform.SetParent(entrance, false);
        go.transform.position = entrance.position;
        go.transform.rotation = entrance.rotation;
        return go.transform;
    }
}
