using UnityEngine;

/// <summary>
/// Helper utility for spawning structures / monsters / items
/// based on MapProfile and voxel surface data.
///
/// 현재 버전은 컴파일 에러를 없애기 위한 "스텁" 구현이다.
/// 실제 스폰 로직은 이후 단계에서 채워 넣으면 된다.
/// </summary>
public static class VoxelSpawnUtility
{
    /// <summary>
    /// Entry point used by VoxelTerrainModule.
    /// Called after the voxel chunk and VoxelSurfaceData have been built.
    /// </summary>
    /// <param name="profile">Map profile used for this planet.</param>
    /// <param name="rng">Deterministic RNG instance.</param>
    /// <param name="terrainParent">Parent transform that owns the generated terrain.</param>
    /// <param name="chunk">VoxelChunk that holds voxel data and mesh.</param>
    /// <param name="surfaceData">Surface classification (floor/wall/ceiling, etc.).</param>
    public static void SpawnFromProfile(
        MapProfile profile,
        Rng rng,
        Transform terrainParent,
        VoxelChunk chunk,
        VoxelSurfaceData surfaceData)
    {
        if (profile == null || terrainParent == null || chunk == null || surfaceData == null)
        {
            Debug.LogWarning(
                "VoxelSpawnUtility.SpawnFromProfile: " +
                "one or more arguments are null. No spawning performed.");
            return;
        }

        // ─────────────────────────────────────
        // TODO: 실제 스폰 로직
        //  - profile 쪽에 정의한 구조물/몬스터/아이템 스폰 설정을 읽고
        //  - surfaceData 의 바닥/벽/천장 + 방/통로/지상/동굴 플래그를 참고해서
        //  - 조건에 맞는 voxel 위치를 골라 prefab Instantiate
        //
        //  지금은 컴파일만 통과시키기 위한 더미 구현이므로,
        //  단순히 디버그 로그만 남긴다.
        // ─────────────────────────────────────

#if UNITY_EDITOR
        Debug.Log(
            $"[VoxelSpawnUtility] SpawnFromProfile called for profile '{profile.name}'. " +
            "Stub implementation: no prefabs are spawned yet.");
#endif
    }
}
