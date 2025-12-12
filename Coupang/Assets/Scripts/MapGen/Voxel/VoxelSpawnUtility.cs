using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helper class that spawns voxel-based structures / monsters / items
/// using MapProfile rules + VoxelSurfaceData surface classification.
/// </summary>
public static class VoxelSpawnUtility
{
    /// <summary>
    /// Main entry point called from VoxelTerrainModule after terrain + surface data is built.
    /// 
    /// - profile: MapProfile with voxel spawn entries
    /// - rng: Rng instance (already split by caller)
    /// - terrainParent: parent transform used by VoxelTerrainModule (chunk is a child of this)
    /// - chunk: VoxelChunk generated for this planet
    /// - surfaceData: VoxelSurfaceData built via VoxelSurfaceBuilder
    /// </summary>
    public static void SpawnFromProfile(
        MapProfile profile,
        Rng rng,
        Transform terrainParent,
        VoxelChunk chunk,
        VoxelSurfaceData surfaceData)
    {
        if (profile == null || rng == null || terrainParent == null || chunk == null || surfaceData == null)
        {
            Debug.LogWarning("VoxelSpawnUtility.SpawnFromProfile: invalid arguments (null).");
            return;
        }

        if (surfaceData.cells == null || surfaceData.cells.Length == 0)
        {
            // 표면 후보가 아예 없으면 스폰도 없음
            Debug.Log("[VoxelSpawnUtility] No surface cells available, skipping spawn.");
            return;
        }

        // 월드 루트(최상위 WorldRoot) 찾기: 없으면 terrainParent 사용
        Transform worldRoot = ResolveWorldRoot(terrainParent);

        // 구조물 / 몬스터 / 아이템 각각 정리해서 넣을 폴더 생성
        Transform structuresRoot = GetOrCreateChild(worldRoot, "SpawnedStructures");
        Transform monstersRoot = GetOrCreateChild(worldRoot, "SpawnedMonsters");
        Transform itemsRoot = GetOrCreateChild(worldRoot, "SpawnedItems");

        // 행성 중심 (반경 필터용)
        Vector3 planetCenter = terrainParent.position;
        float landingRadius = Mathf.Max(0f, profile.landingRadius);

        // ─────────────────────────────────────
        // Structures
        // ─────────────────────────────────────
        if (profile.voxelStructures != null && profile.voxelStructures.Length > 0)
        {
            foreach (var entry in profile.voxelStructures)
            {
                if (entry == null || entry.prefab == null)
                    continue;

                SpawnFromEntry(
                    profile,
                    entry,
                    entry.prefab,
                    rng.Split(1000 + HashId(entry.id, 1)),
                    structuresRoot,
                    surfaceData,
                    planetCenter,
                    landingRadius
                );
            }
        }

        // ─────────────────────────────────────
        // Monsters
        // ─────────────────────────────────────
        if (profile.voxelMonsters != null && profile.voxelMonsters.Length > 0)
        {
            foreach (var entry in profile.voxelMonsters)
            {
                if (entry == null || entry.prefab == null)
                    continue;

                SpawnFromEntry(
                    profile,
                    entry,
                    entry.prefab,
                    rng.Split(2000 + HashId(entry.id, 2)),
                    monstersRoot,
                    surfaceData,
                    planetCenter,
                    landingRadius
                );
            }
        }

        // ─────────────────────────────────────
        // Items (ItemDefinition → worldPrefab)
        // ─────────────────────────────────────
        if (profile.voxelItems != null && profile.voxelItems.Length > 0)
        {
            foreach (var entry in profile.voxelItems)
            {
                if (entry == null || entry.itemDefinition == null)
                    continue;

                GameObject prefab = entry.itemDefinition.worldPrefab;
                if (prefab == null)
                {
                    Debug.LogWarning($"[VoxelSpawnUtility] ItemDefinition '{entry.itemDefinition.name}' has no worldPrefab.");
                    continue;
                }

                SpawnFromEntry(
                    profile,
                    entry,
                    prefab,
                    rng.Split(3000 + HashId(entry.id, 3)),
                    itemsRoot,
                    surfaceData,
                    planetCenter,
                    landingRadius
                );
            }
        }
    }

    // =====================================================================
    // 내부 유틸
    // =====================================================================

    /// <summary>
    /// TerrainParent로부터 최상위 WorldRoot를 찾는다.
    /// 이름이 'WorldRoot'인 Transform을 위로 타고 올라가며 탐색.
    /// 없으면 그냥 terrainParent 쪽 계층의 최상위 Transform 사용.
    /// </summary>
    private static Transform ResolveWorldRoot(Transform terrainParent)
    {
        if (terrainParent == null)
            return null;

        Transform t = terrainParent;
        Transform last = t;

        while (t != null)
        {
            last = t;
            if (t.name == "WorldRoot")
                return t;

            t = t.parent;
        }

        // WorldRoot를 못 찾으면 최상위(또는 terrainParent) 사용
        return last != null ? last : terrainParent;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name)
                return c;
        }

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>
    /// 문자열 id를 간단히 해시해서 seed에 섞어 쓰기 위한 함수.
    /// id가 비어있으면 salt만 사용.
    /// </summary>
    private static int HashId(string id, int salt)
    {
        if (string.IsNullOrEmpty(id))
            return salt * 73856093;

        unchecked
        {
            int h = 17;
            for (int i = 0; i < id.Length; i++)
            {
                h = h * 31 + id[i];
            }
            h ^= salt * 73856093;
            return h;
        }
    }

    // =====================================================================
    // 하나의 SpawnEntry에 대한 공통 스폰 로직
    // =====================================================================

    private static void SpawnFromEntry(
        MapProfile profile,
        MapProfile.VoxelSpawnEntryBase entry,
        GameObject prefab,
        Rng rng,
        Transform parent,
        VoxelSurfaceData surfaceData,
        Vector3 planetCenter,
        float landingRadius)
    {
        if (entry == null || prefab == null || surfaceData == null || surfaceData.cells == null)
            return;

        // 스폰 개수 결정
        int minCount = Mathf.Max(0, entry.minCount);
        int maxCount = Mathf.Max(minCount, entry.maxCount);
        if (maxCount == 0)
            return;

        int spawnCount = (minCount == maxCount)
            ? minCount
            : rng.NextInt(minCount, maxCount + 1); // max 포함

        if (spawnCount <= 0)
            return;

        // 필터에 맞는 SurfaceCell 후보 모으기
        List<int> candidateIndices = CollectCandidateIndices(
            profile,
            entry.filter,
            surfaceData,
            planetCenter,
            landingRadius
        );

        if (candidateIndices.Count == 0)
        {
            // Debug.Log($"[VoxelSpawnUtility] Entry '{entry.id}' has no valid spawn candidates.");
            return;
        }

        int spawned = 0;

        // 후보 리스트에서 랜덤으로 뽑아서 스폰, 중복 스폰 방지 위해 swap-remove
        for (int i = 0; i < spawnCount && candidateIndices.Count > 0; i++)
        {
            int pickIndex = rng.NextInt(0, candidateIndices.Count);
            int cellIndex = candidateIndices[pickIndex];

            candidateIndices[pickIndex] = candidateIndices[candidateIndices.Count - 1];
            candidateIndices.RemoveAt(candidateIndices.Count - 1);

            VoxelSurfaceData.SurfaceCell cell = surfaceData.cells[cellIndex];

            // 실제 스폰 위치/회전 계산
            Vector3 pos;
            Quaternion rot;
            ComputeSpawnTransform(surfaceData, cell, entry, rng, out pos, out rot);

            // 프리팹 인스턴스 생성 (부모는 WorldRoot 아래의 구조/몬스터/아이템 폴더들)
            GameObject instance = Object.Instantiate(prefab, pos, rot, parent);

            // 🔒 안전장치: 프리팹이 비활성 상태여도 인스턴스는 무조건 활성화
            if (!instance.activeSelf)
            {
                instance.SetActive(true);
            }

            spawned++;
        }

        // Debug.Log($"[VoxelSpawnUtility] Spawned {spawned}/{spawnCount} of '{entry.id}' ({prefab.name})");
    }

    // =====================================================================
    // 후보 SurfaceCell 필터링
    // =====================================================================

    private static List<int> CollectCandidateIndices(
        MapProfile profile,
        MapProfile.VoxelSpawnFilter filter,
        VoxelSurfaceData surfaceData,
        Vector3 planetCenter,
        float landingRadius)
    {
        List<int> result = new List<int>();

        if (surfaceData.cells == null || surfaceData.cells.Length == 0)
            return result;

        float minY = filter.minWorldY;
        float maxY = filter.maxWorldY;
        float minR = filter.minRadiusFromCenter;
        float maxR = filter.maxRadiusFromCenter;

        for (int i = 0; i < surfaceData.cells.Length; i++)
        {
            var cell = surfaceData.cells[i];

            Vector3 p = cell.worldPosition;
            float y = p.y;

            // 1) 높이 필터
            if (y < minY || y > maxY)
                continue;

            // 2) 행성 중심 반경 필터
            Vector2 v = new Vector2(p.x - planetCenter.x, p.z - planetCenter.z);
            float r = v.magnitude;

            if (r < minR || r > maxR)
                continue;

            if (filter.avoidLandingZone && r < landingRadius)
                continue;

            // 3) 표면 타입 (Floor / Wall / Ceiling)
            if (!SurfaceTypeMatches(filter, cell.surfaceFlags))
                continue;

            // 4) Region 타입 (Room / Corridor / GenericCave / Exterior)
            if (!RegionTypeMatches(filter, cell.regionFlags))
                continue;

            result.Add(i);
        }

        return result;
    }

    private static bool SurfaceTypeMatches(
        MapProfile.VoxelSpawnFilter filter,
        VoxelSurfaceFlags surfaceFlags)
    {
        bool wantFloor = filter.allowOnFloor;
        bool wantWall = filter.allowOnWall;
        bool wantCeiling = filter.allowOnCeiling;

        if (!wantFloor && !wantWall && !wantCeiling)
        {
            // 아무 것도 허용 안 하면 그냥 false
            return false;
        }

        if ((surfaceFlags & VoxelSurfaceFlags.Floor) != 0 && wantFloor)
            return true;

        if ((surfaceFlags & VoxelSurfaceFlags.Wall) != 0 && wantWall)
            return true;

        if ((surfaceFlags & VoxelSurfaceFlags.Ceiling) != 0 && wantCeiling)
            return true;

        return false;
    }

    private static bool RegionTypeMatches(
        MapProfile.VoxelSpawnFilter filter,
        VoxelRegionFlags regionFlags)
    {
        // regionFlags 가 None 이면 "어디에도 속하지 않는 공기" → 기본적으로 스폰하지 않음
        if (regionFlags == VoxelRegionFlags.None)
            return false;

        if ((regionFlags & VoxelRegionFlags.Room) != 0 && filter.allowInRooms)
            return true;

        if ((regionFlags & VoxelRegionFlags.Corridor) != 0 && filter.allowInCorridors)
            return true;

        if ((regionFlags & VoxelRegionFlags.GenericCave) != 0 && filter.allowInGenericCaves)
            return true;

        if ((regionFlags & VoxelRegionFlags.Exterior) != 0 && filter.allowInExterior)
            return true;

        return false;
    }

    // =====================================================================
    // 실제 스폰 위치 / 회전 계산
    // =====================================================================

    private static void ComputeSpawnTransform(
        VoxelSurfaceData surfaceData,
        VoxelSurfaceData.SurfaceCell cell,
        MapProfile.VoxelSpawnEntryBase entry,
        Rng rng,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 normal = cell.normal;
        if (normal.sqrMagnitude < 1e-4f)
        {
            // 안전빵으로 위쪽
            normal = Vector3.up;
        }
        normal.Normalize();

        // 기본 위치: SurfaceCell 이 가진 worldPosition
        position = cell.worldPosition;

        // 바닥/벽/천장 구분에 따라 살짝 띄워서 생성 (바닥 관통/튕김 방지)
        // Floor인 경우는 조금 더 크게 띄워준다.
        float offsetMul = 0.05f;
        if ((cell.surfaceFlags & VoxelSurfaceFlags.Floor) != 0)
        {
            // marching cubes + Rigidbody가 섞여도 안정적으로 안 뚫리게 살짝 여유 줌
            offsetMul = 0.3f;
        }

        float baseOffset = Mathf.Max(0.01f, surfaceData.voxelSize * offsetMul);
        position += normal * baseOffset;

        // entry에서 지정한 추가 오프셋
        position += entry.extraOffset;

        // 회전 계산
        Quaternion baseRot;
        if (entry.alignToSurfaceNormal)
        {
            Vector3 up = normal;
            // up에 수직인 forward 벡터 하나 만들기
            Vector3 forward = Vector3.Cross(Vector3.right, up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.Cross(Vector3.forward, up);
            forward.Normalize();

            baseRot = Quaternion.LookRotation(forward, up);
        }
        else
        {
            baseRot = Quaternion.identity;
        }

        if (entry.randomYawAroundNormal)
        {
            float angle = rng.NextFloat(0f, 360f);
            Quaternion yaw = Quaternion.AngleAxis(angle, normal);
            rotation = yaw * baseRot;
        }
        else
        {
            rotation = baseRot;
        }
    }
}
