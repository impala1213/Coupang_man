using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ContractCargoSpawner : MonoBehaviour
{
    [Header("Spawn Area")]
    [Tooltip("Spawn area bounds (room). If null, will try to find a BoxCollider on this object.")]
    public BoxCollider spawnBounds;

    [Tooltip("Spacing between spawned items (world units).")]
    public Vector2 spacing = new Vector2(0.6f, 0.6f);

    [Tooltip("Padding inside bounds to keep items away from walls.")]
    public Vector2 padding = new Vector2(0.2f, 0.2f);

    [Tooltip("Height offset from the bounds center in local space.")]
    public float heightOffset = 0.1f;

    [Header("Spawn Output")]
    [Tooltip("Optional parent for spawned items.")]
    public Transform spawnParent;

    [Tooltip("Clear previously spawned contract cargo when selection changes.")]
    public bool clearPrevious = true;

    [Header("Debug")]
    public bool debugLog;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    // Guard: prevent accidental "refill" when selection Changed is raised without a real user change
    // (e.g., GameSession calling PlanetSelectionState.RememberSelection() at launch).
    private DeliveryContractDefinition _spawnedForContract;
    private string _spawnedForContractName;
    private bool _hasSpawnedForContract;

    private void Awake()
    {
        if (!spawnBounds)
            spawnBounds = GetComponent<BoxCollider>();
    }

    private void OnEnable()
    {
        PlanetSelectionState.Changed += HandleSelectionChanged;
        HandleSelectionChanged();
    }

    private void OnDisable()
    {
        PlanetSelectionState.Changed -= HandleSelectionChanged;
    }

    private void HandleSelectionChanged()
    {
        if (!PlanetSelectionState.HasSelection)
        {
            if (clearPrevious)
                ClearSpawned();
            ResetGuard();
            return;
        }

        var offer = PlanetSelectionState.GetCandidate(PlanetSelectionState.GetSelectedIndex());
        if (offer.contract == null)
        {
            if (clearPrevious)
                ClearSpawned();
            ResetGuard();
            return;
        }

        // ✅ 핵심: 같은 계약인데 Changed가 한번 더 불리면(런치/리멤버/리프레시 등)
        // 이미 일부 아이템을 집어 들었더라도 다시 "풀셋"으로 리필하면 안 된다.
        if (IsSameContractAsSpawned(offer.contract) && _hasSpawnedForContract)
        {
            return;
        }

        SpawnContractCargo(offer.contract);

        if (GameSession.Instance != null)
            GameSession.Instance.ResetContractCargoPickup();
    }

    private bool IsSameContractAsSpawned(DeliveryContractDefinition contract)
    {
        if (contract == null) return false;
        if (_spawnedForContract == contract) return true;

        // Addressables/instancing 등으로 레퍼런스가 달라지는 경우를 대비한 보강.
        if (!string.IsNullOrEmpty(_spawnedForContractName) && contract.name == _spawnedForContractName)
            return true;

        return false;
    }

    private void ResetGuard()
    {
        _spawnedForContract = null;
        _spawnedForContractName = null;
        _hasSpawnedForContract = false;
    }

    public void SpawnContractCargo(DeliveryContractDefinition contract)
    {
        if (contract == null || contract.requiredItems == null || contract.requiredItems.Length == 0)
            return;

        if (spawnBounds == null)
        {
            Debug.LogWarning("[ContractCargoSpawner] Missing spawn bounds (BoxCollider).");
            return;
        }

        if (clearPrevious)
            ClearSpawned();

        _spawnedForContract = contract;
        _spawnedForContractName = contract.name;
        _hasSpawnedForContract = true;

        var positions = BuildSpawnPositions(CountTotalItems(contract));
        int positionIndex = 0;

        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var req = contract.requiredItems[i];
            if (req == null || req.item == null || req.requiredQty <= 0)
                continue;

            var prefab = req.item.worldPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[ContractCargoSpawner] Missing world prefab for item '{req.item.name}'.");
                continue;
            }

            for (int k = 0; k < req.requiredQty; k++)
            {
                if (positionIndex >= positions.Count)
                {
                    Debug.LogWarning("[ContractCargoSpawner] Not enough space in spawn bounds to place all items.");
                    return;
                }

                Vector3 worldPos = positions[positionIndex];
                positionIndex++;

                GameObject go = Instantiate(prefab, worldPos, Quaternion.identity);
                if (spawnParent != null)
                    go.transform.SetParent(spawnParent, true);

                var wi = go.GetComponent<WorldItem>() ?? go.AddComponent<WorldItem>();
                wi.definition = req.item;
                wi.OnDropped(worldPos, Vector3.zero);

                var cargoMarker = go.GetComponent<ContractCargoMarker>() ?? go.AddComponent<ContractCargoMarker>();
                cargoMarker.ResetPicked();

                if (debugLog)
                    Debug.Log($"[ContractCargoSpawner] Spawned {req.item.name} at {worldPos}");

                _spawned.Add(go);
            }
        }
    }

    private int CountTotalItems(DeliveryContractDefinition contract)
    {
        int total = 0;
        for (int i = 0; i < contract.requiredItems.Length; i++)
        {
            var req = contract.requiredItems[i];
            if (req == null) continue;
            total += Mathf.Max(0, req.requiredQty);
        }
        return total;
    }

    private List<Vector3> BuildSpawnPositions(int count)
    {
        var positions = new List<Vector3>(count);

        Vector3 size = spawnBounds.size;
        Vector3 center = spawnBounds.center;

        float usableX = Mathf.Max(0f, size.x - padding.x * 2f);
        float usableZ = Mathf.Max(0f, size.z - padding.y * 2f);

        float stepX = Mathf.Max(0.01f, spacing.x);
        float stepZ = Mathf.Max(0.01f, spacing.y);

        int cols = Mathf.Max(1, Mathf.FloorToInt(usableX / stepX));
        int rows = Mathf.Max(1, Mathf.FloorToInt(usableZ / stepZ));

        float startX = -usableX * 0.5f + stepX * 0.5f;
        float startZ = -usableZ * 0.5f + stepZ * 0.5f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (positions.Count >= count)
                    return positions;

                float x = startX + c * stepX;
                float z = startZ + r * stepZ;
                Vector3 localPos = new Vector3(center.x + x, center.y + heightOffset, center.z + z);
                positions.Add(transform.TransformPoint(localPos));
            }
        }

        return positions;
    }

    private void ClearSpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }
        _spawned.Clear();

        // If we cleared, allow a future explicit selection change to spawn again.
        ResetGuard();
    }
}
