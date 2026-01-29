using System;
using UnityEngine;

/// <summary>
/// Runtime (Ship-scene) state for choosing the next planet before pulling the lever.
/// This avoids cross-scene drag references: GameSession consumes this selection at launch.
/// </summary>
public static class PlanetSelectionState
{
    public struct Offer
    {
        public PlanetCatalog.PlanetEntry planet;
        public DeliveryContractDefinition contract;

        public bool IsValid => planet != null && !string.IsNullOrEmpty(planet.gameplaySceneName);

        public override string ToString()
        {
            string p = planet != null ? planet.GetDisplayLabel() : "None";
            string s = planet != null ? planet.gameplaySceneName : "(null)";
            string c = contract != null ? contract.displayName : "None";
            return $"{p} ({s}) / Contract: {c}";
        }
    }

    /// <summary>
    /// Raised whenever candidates/selection/lock changes.
    /// Use this to update ship monitors without polling every frame.
    /// </summary>
    public static event Action Changed;

    private static Offer[] s_candidates;
    private static int s_selectedIndex = -1;

    // "Confirmed" == "Locked" (once player clicks an option, they cannot change it)
    private static bool s_confirmed;

    private static bool s_hasLastSelection;
    private static Offer s_lastSelection;

    /// <summary>
    /// True when exactly 2 valid candidates exist.
    /// </summary>
    public static bool HasCandidates =>
        s_candidates != null &&
        s_candidates.Length == 2 &&
        s_candidates[0].IsValid &&
        s_candidates[1].IsValid;

    /// <summary>
    /// True when a valid candidate is selected.
    /// </summary>
    public static bool HasSelection =>
        HasCandidates &&
        s_selectedIndex >= 0 &&
        s_selectedIndex < 2 &&
        s_candidates[s_selectedIndex].IsValid;

    /// <summary>
    /// True when the current selection is confirmed/locked.
    /// </summary>
    public static bool HasConfirmed => HasSelection && s_confirmed;

    public static bool HasLastSelection => s_hasLastSelection && s_lastSelection.IsValid;

    public static Offer GetLastSelection() => s_lastSelection;

    public static Offer GetCandidate(int index)
    {
        if (s_candidates == null || index < 0 || index >= s_candidates.Length)
            return default;
        return s_candidates[index];
    }

    public static int GetSelectedIndex() => s_selectedIndex;

    /// <summary>
    /// Assign two candidates. By default clears selection + confirmation.
    /// </summary>
    public static void SetCandidates(Offer a, Offer b, bool clearSelection = true)
    {
        s_candidates = new Offer[2] { a, b };
        if (clearSelection)
        {
            s_selectedIndex = -1;
            s_confirmed = false;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Selects a candidate (0 or 1). Does nothing if already locked.
    /// </summary>
    public static void Select(int index)
    {
        if (!HasCandidates) return;
        if (index < 0 || index >= 2) return;
        if (!s_candidates[index].IsValid) return;

        // No changes after locking in.
        if (s_confirmed) return;

        s_selectedIndex = index;

        // Do NOT auto-lock here; call TryLockSelection() for the new UI behavior.
        RaiseChanged();
    }

    /// <summary>
    /// Selects and immediately locks the choice (cannot change afterwards).
    /// Returns true if the selection was locked.
    /// </summary>
    public static bool TryLockSelection(int index)
    {
        if (!HasCandidates) return false;
        if (index < 0 || index >= 2) return false;
        if (!s_candidates[index].IsValid) return false;

        if (s_confirmed) return false;

        s_selectedIndex = index;
        s_confirmed = true;
        RaiseChanged();
        return true;
    }

    // Compatibility helpers (older UI scripts may call these)
    public static void SetSelectedIndex(int index) => Select(index);

    public static void SetConfirmed(bool confirmed)
    {
        // Once locked, do not allow unlocking through UI.
        if (s_confirmed) return;

        if (!HasSelection)
        {
            s_confirmed = false;
            RaiseChanged();
            return;
        }

        s_confirmed = confirmed;
        RaiseChanged();
    }

    /// <summary>
    /// Returns the selected offer and clears the selection state (one-time consume).
    /// Confirmation is NOT required for consumption; enforce it in GameSession if desired.
    /// </summary>
    public static bool TryConsumeSelection(out Offer offer)
    {
        offer = default;

        if (!HasSelection)
            return false;

        offer = s_candidates[s_selectedIndex];

        s_lastSelection = offer;
        s_hasLastSelection = offer.IsValid;

        // Clear (so next trip requires a new selection)
        s_candidates = null;
        s_selectedIndex = -1;
        s_confirmed = false;

        RaiseChanged();
        return offer.IsValid;
    }

    public static void Clear()
    {
        s_candidates = null;
        s_selectedIndex = -1;
        s_confirmed = false;
        s_lastSelection = default;
        s_hasLastSelection = false;
        RaiseChanged();
    }

    public static void ClearLastSelection()
    {
        s_lastSelection = default;
        s_hasLastSelection = false;
        RaiseChanged();
    }

    private static void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
