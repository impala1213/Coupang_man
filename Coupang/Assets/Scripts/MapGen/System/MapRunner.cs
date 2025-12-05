using UnityEngine;

public class MapRunner : MonoBehaviour
{
    [Header("Targets")]
    public Transform terrainParent;

    [Header("Modules")]
    public TerrainModule defaultTerrainModule;
    public CaveModule defaultCaveModule;

    [Header("Profile Override")]
    public bool overrideProfile;
    public MapProfile profileOverride;

    [Header("Options")]
    [Tooltip("If false, cave modules are never generated, even if assigned in the profile.")]
    public bool generateCaves = true;

    public TerrainModule LastTerrainModuleUsed { get; private set; }
    public CaveModule LastCaveModuleUsed { get; private set; }
    public MapProfile LastProfileUsed { get; private set; }

    // Backward compatibility (LandingHelper 등)
    public TerrainModule LastModuleUsed => LastTerrainModuleUsed;

    public void Run(MapProfile profile, int seed)
    {
        if (terrainParent == null)
            terrainParent = transform;

        MapProfile effectiveProfile = profile;
        if (overrideProfile && profileOverride != null)
        {
            effectiveProfile = profileOverride;
        }

        if (effectiveProfile == null)
        {
            Debug.LogError("MapRunner: effective profile is null.");
            return;
        }

        LastProfileUsed = effectiveProfile;

        Rng rng = new Rng(seed);

        // 1) Surface terrain
        TerrainModule terrainModule = effectiveProfile.terrainModule != null
            ? effectiveProfile.terrainModule
            : defaultTerrainModule;

        if (terrainModule == null)
        {
            Debug.LogError("MapRunner: no TerrainModule assigned.");
            return;
        }

        LastTerrainModuleUsed = terrainModule;

        Rng terrainRng = rng.Split(1);
        terrainModule.GenerateTerrain(effectiveProfile, terrainRng, terrainParent);

        // 2) Caves
        LastCaveModuleUsed = null;

        if (generateCaves)
        {
            CaveModule caveModule = effectiveProfile.caveModule != null
                ? effectiveProfile.caveModule
                : defaultCaveModule;

            if (caveModule != null)
            {
                LastCaveModuleUsed = caveModule;
                Rng caveRng = rng.Split(2);
                caveModule.GenerateCaves(effectiveProfile, caveRng, terrainParent);
            }
        }

        // 3) Entrance portals (surface <-> cave)
        SpawnEntrances(effectiveProfile, rng.Split(3));
    }

    private void SpawnEntrances(MapProfile profile, Rng rng)
    {
        if (profile == null)
            return;

        if (terrainParent == null)
            return;

        if (LastCaveModuleUsed == null)
            return;

        int layerCount = LastCaveModuleUsed.GetLayerCount(profile);
        if (layerCount <= 0)
            return;

        // 착륙 위치 (컨테이너 스폰 기준)
        Vector3 landingPos =
            (LastTerrainModuleUsed != null)
            ? LastTerrainModuleUsed.GetLandingHint(profile, terrainParent)
            : terrainParent.position;

        // --- Surface entrance position ---
        Vector3 surfaceEntrancePos = landingPos;
        bool gotFromTerrain = false;

        // 1순위: TerrainModule이 생성 중에 정해둔 입구(Hint)가 있으면 그걸 사용
        if (LastTerrainModuleUsed != null
            && LastTerrainModuleUsed.TryGetCaveEntranceHint(profile, terrainParent, out Vector3 hintPos))
        {
            surfaceEntrancePos = hintPos;

            // 혹시나 메쉬/콜라이더와 살짝 어긋났을 수 있으니, 위에서 아래로 한 번 보정
            Vector3 rayStart = surfaceEntrancePos + Vector3.up * 50f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f))
            {
                surfaceEntrancePos = hit.point;
            }

            gotFromTerrain = true;
        }

        // 2순위: TerrainModule에서 힌트를 안 주면, 착륙 지점 기준 랜덤 샘플 방식으로 찾기
        if (!gotFromTerrain)
        {
            surfaceEntrancePos = FindSurfaceEntrancePosition(profile, rng, landingPos);
        }

        // 첫 번째 동굴 층(지하 1층) 입구 위치 (HeightmapCaveModule에서 계산)
        Vector3 caveLevel0Pos = LastCaveModuleUsed.GetLayerEntranceHint(profile, terrainParent, 0);

        // --- Surface entrance prefab ---
        if (profile.surfaceEntrancePrefab != null)
        {
            Vector3 pos = surfaceEntrancePos + Vector3.up * profile.surfaceEntranceYOffset;
            GameObject surfaceEntrance = Instantiate(
                profile.surfaceEntrancePrefab,
                pos,
                Quaternion.identity,
                terrainParent
            );

            EntrancePortal portal = surfaceEntrance.GetComponent<EntrancePortal>();
            if (portal != null)
            {
                portal.targetPosition = caveLevel0Pos + Vector3.up * profile.caveEntranceYOffset;
            }
        }

        // --- Cave entrance prefab (to go back up) ---
        if (profile.caveEntrancePrefab != null)
        {
            Vector3 pos = caveLevel0Pos + Vector3.up * profile.caveEntranceYOffset;
            GameObject caveEntrance = Instantiate(
                profile.caveEntrancePrefab,
                pos,
                Quaternion.identity,
                terrainParent
            );

            EntrancePortal portal = caveEntrance.GetComponent<EntrancePortal>();
            if (portal != null)
            {
                portal.targetPosition = landingPos + Vector3.up * profile.surfaceEntranceYOffset;
            }
        }
    }

    /// <summary>
    /// 기존 랜덤 방식: 착륙 위치 기준으로 일정 거리 떨어진 지형 위의 위치를 찾는다.
    /// (TerrainModule에서 힌트를 주지 않을 때만 사용)
    /// </summary>
    private Vector3 FindSurfaceEntrancePosition(MapProfile profile, Rng rng, Vector3 landingPos)
    {
        float minDist = Mathf.Max(0f, profile.surfaceEntranceMinDistanceFromLanding);
        float maxDist = Mathf.Max(minDist, profile.surfaceEntranceMaxDistanceFromLanding);
        int attempts = Mathf.Max(1, profile.surfaceEntrancePlacementAttempts);

        Vector3 result = landingPos;

        for (int i = 0; i < attempts; i++)
        {
            float angle = rng.NextFloat(0f, Mathf.PI * 2f);
            float dist = rng.NextFloat(minDist, maxDist);

            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 sampleHorizontal = landingPos + dir * dist;

            // 위에서 아래로 레이 쏴서 지형 높이 찾기
            Vector3 rayStart = sampleHorizontal + Vector3.up * 100f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 300f))
            {
                result = hit.point;
                return result;
            }
        }

        // 실패하면 착륙 위치 바로 아래 지형으로 fallback
        {
            Vector3 rayStart = landingPos + Vector3.up * 100f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 300f))
            {
                result = hit.point;
            }
            else
            {
                // 정말 아무것도 못 찾으면 그냥 landingPos 그대로
                result = landingPos;
            }
        }

        return result;
    }
}
