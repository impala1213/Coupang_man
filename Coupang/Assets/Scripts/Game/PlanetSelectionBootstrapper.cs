using UnityEngine;

/// <summary>
/// Ship-scene bootstrapper: generates the two planet candidates (A/B) at scene start so
/// the always-on ship monitor never shows empty "None" offers.
///
/// This is the ONLY place that should generate candidates. All UI reads PlanetSelectionState.
/// </summary>
public class PlanetSelectionBootstrapper : MonoBehaviour
{
    [Header("Data")]
    public PlanetCatalog planetCatalog;

    [Tooltip("Fallback contract directory if the planet entry has none.")]
    public DeliveryContractDirectory fallbackContractDirectory;

    [Header("Behavior")]
    [Tooltip("Generate candidates on Awake.")]
    public bool generateOnAwake = true;

    [Tooltip("If true, regenerate when candidates exist but are invalid (null planet, missing scene name, etc.).")]
    public bool regenerateIfInvalid = true;

    [Tooltip("Optional: if true and a destination is already confirmed, generate a fresh pair anyway.")]
    public bool regenerateIfConfirmed = false;

    private void Awake()
    {
        if (generateOnAwake)
            EnsureCandidates();
    }

    /// <summary>
    /// Ensures PlanetSelectionState has two valid candidates.
    /// Safe to call multiple times.
    /// </summary>
    public void EnsureCandidates()
    {
        if (planetCatalog == null)
        {
            Debug.LogWarning("[PlanetSelectionBootstrapper] PlanetCatalog is missing.");
            return;
        }

        if (regenerateIfConfirmed && PlanetSelectionState.HasConfirmed)
        {
            GenerateCandidates(clearSelection: true);
            return;
        }

        if (!PlanetSelectionState.HasCandidates)
        {
            GenerateCandidates(clearSelection: true);
            return;
        }

        if (regenerateIfInvalid && CandidatesInvalid())
        {
            GenerateCandidates(clearSelection: true);
        }
    }

    private bool CandidatesInvalid()
    {
        if (!PlanetSelectionState.HasCandidates) return true;

        var a = PlanetSelectionState.GetCandidate(0);
        var b = PlanetSelectionState.GetCandidate(1);

        // Offer.IsValid exists in your PlanetSelectionState; if it changes, these null checks still work.
        if (a.planet == null || b.planet == null) return true;
        if (string.IsNullOrEmpty(a.planet.gameplaySceneName)) return true;
        if (string.IsNullOrEmpty(b.planet.gameplaySceneName)) return true;

        return false;
    }

    private void GenerateCandidates(bool clearSelection)
    {
        var p1 = planetCatalog.GetRandomPlanet();
        var p2 = GetRandomPlanetDifferentFrom(p1);

        var a = BuildOffer(p1);
        var b = BuildOffer(p2);

        PlanetSelectionState.SetCandidates(a, b, clearSelection: clearSelection);
    }

    private PlanetCatalog.PlanetEntry GetRandomPlanetDifferentFrom(PlanetCatalog.PlanetEntry other)
    {
        PlanetCatalog.PlanetEntry p = planetCatalog.GetRandomPlanet();
        if (other == null) return p;

        for (int i = 0; i < 8; i++)
        {
            if (p != null && p != other)
                return p;

            p = planetCatalog.GetRandomPlanet();
        }

        // If the catalog has only one planet, this may return the same planet.
        return p;
    }

    private PlanetSelectionState.Offer BuildOffer(PlanetCatalog.PlanetEntry planet)
    {
        PlanetSelectionState.Offer offer = new PlanetSelectionState.Offer();
        offer.planet = planet;
        offer.contract = null;

        if (planet != null)
        {
            DeliveryContractDirectory dir = (planet.contractDirectory != null) ? planet.contractDirectory : fallbackContractDirectory;
            if (dir != null)
                offer.contract = dir.GetRandomContract();
        }

        return offer;
    }
}
