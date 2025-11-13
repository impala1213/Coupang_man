using UnityEngine;

public class LandingHelper : MonoBehaviour
{
    public Transform terrainParent;
    public Transform planetContainerAnchor;
    public MapRunner mapRunner;

    public bool IsEssentialReady { get; private set; }
    public Transform PlanetContainerAnchor => planetContainerAnchor;

    public void Initialize(MapProfile profile, int seed)
    {
        IsEssentialReady = false;

        if (mapRunner == null)
        {
            mapRunner = GetComponent<MapRunner>();
        }

        if (mapRunner == null)
        {
            Debug.LogError("LandingHelper: MapRunner is not assigned.");
            return;
        }

        if (terrainParent == null)
        {
            terrainParent = mapRunner.terrainParent != null ? mapRunner.terrainParent : mapRunner.transform;
        }

        mapRunner.terrainParent = terrainParent;
        mapRunner.Run(profile, seed);

        SetupAnchor();

        IsEssentialReady = true;
    }

    private void SetupAnchor()
    {
        if (planetContainerAnchor == null)
        {
            GameObject anchor = new GameObject("PlanetContainerAnchor");
            planetContainerAnchor = anchor.transform;
        }

        planetContainerAnchor.SetParent(transform, false);

        Vector3 hint = transform.position;
        if (mapRunner != null && mapRunner.LastModuleUsed != null && mapRunner.LastProfileUsed != null)
        {
            hint = mapRunner.LastModuleUsed.GetLandingHint(mapRunner.LastProfileUsed, terrainParent);
        }

        Vector3 rayOrigin = hint + Vector3.up * 100f;
        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
        {
            planetContainerAnchor.position = hit.point + Vector3.up * 2f;
        }
        else
        {
            planetContainerAnchor.position = hint + Vector3.up * 2f;
        }
    }
}
