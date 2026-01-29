using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Optional always-on ship monitor display for the selected destination.
/// Attach this to a world-space canvas or monitor UI that should keep showing the choice even when the modal UI is closed.
///
/// Contract UI rule (project): no contract name. Instead, show cargo requirements as category icons + counts.
/// </summary>
public class ShipMonitorDestinationDisplay : MonoBehaviour
{
    [Header("Text")]
    public TMP_Text destinationText;

    [Header("Contract Cargo Icons (Optional)")]
    [Tooltip("Icon library for each ItemCategory. If null, cargo icons will not render.")]
    public ItemCategoryIconLibrary cargoIconLibrary;

    [Tooltip("Prefab that has CategoryIconCountWidget (Image + TMP count).")]
    public GameObject cargoCategoryIconPrefab;

    [Tooltip("Root transform where cargo category icons will be placed.")]
    public Transform cargoIconsRoot;

    [Header("Cargo Icon Layout")]
    [Tooltip("If the cargo icons root has no LayoutGroup, icons will be positioned manually so they line up inside the bar.")]
    public bool autoLayoutCargoIcons = true;

    [Min(0f)] public float cargoIconSpacing = 34f;
    public Vector2 cargoIconStartOffset = Vector2.zero;

    [Tooltip("Force each cargo icon widget to a fixed size. Useful when using a plain Image prefab.")]
    public bool forceCargoIconSize = true;
    public Vector2 cargoIconSize = new Vector2(32f, 32f);

    [Header("Danger Icons (Optional)")]
    public Transform dangerIconsRoot;
    public GameObject dangerIconPrefab;

    [Tooltip("Max warning icons. With the default 0-100 risk and 20 risk per icon, this should be 5.")]
    [Min(0)] public int maxDangerIcons = 5;

    [Header("Danger Icon Layout")]
    [Tooltip("If the danger icons root has no LayoutGroup, icons will be positioned manually so they line up inside the bar.")]
    public bool autoLayoutDangerIcons = true;

    [Min(0f)] public float dangerIconSpacing = 26f;
    public Vector2 dangerIconStartOffset = Vector2.zero;

    [Tooltip("Force each danger icon to a fixed size. Useful when using a plain Image prefab.")]
    public bool forceDangerIconSize = true;
    public Vector2 dangerIconSize = new Vector2(32f, 32f);

    [Header("Danger Rules")]
    [Tooltip("Max risk value. 0-100 by default.")]
    public int maxRisk = 100;

    [Tooltip("Each N risk adds one warning icon (ceil). 20 means: 1-20=1, 21-40=2, etc.")]
    public int riskPerIcon = 20;

    [Header("Behavior")]
    [Tooltip("If false, displays destination only after the player has locked a selection.")]
    public bool showEvenIfNotConfirmed = false;

    public string noSelectionDestinationLabel = "NO DESTINATION";

    private readonly List<GameObject> _dangerIcons = new List<GameObject>();
    private readonly List<CategoryIconCountWidget> _cargoIcons = new List<CategoryIconCountWidget>();
    private readonly Dictionary<ItemCategory, int> _tmpCounts = new Dictionary<ItemCategory, int>();

    private void OnEnable()
    {
        PlanetSelectionState.Changed -= Refresh;
        PlanetSelectionState.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PlanetSelectionState.Changed -= Refresh;
    }

    public void Refresh()
    {
        bool hasSelection = PlanetSelectionState.HasSelection;
        bool isConfirmed = PlanetSelectionState.HasConfirmed;
        bool hasLastSelection = PlanetSelectionState.HasLastSelection;

        if (!hasSelection && hasLastSelection)
        {
            var last = PlanetSelectionState.GetLastSelection();
            ApplyOffer(last);
            return;
        }

        if (!hasSelection || (!showEvenIfNotConfirmed && !isConfirmed))
        {
            if (destinationText != null) destinationText.text = noSelectionDestinationLabel;
            EnsureCargoIcons(null);
            EnsureDangerIcons(0);
            return;
        }

        int sel = PlanetSelectionState.GetSelectedIndex();
        var offer = PlanetSelectionState.GetCandidate(sel);

        ApplyOffer(offer);
    }

    private void ApplyOffer(PlanetSelectionState.Offer offer)
    {
        if (destinationText != null)
            destinationText.text = offer.planet != null ? offer.planet.GetDisplayLabel() : "(None)";

        EnsureCargoIcons(offer.contract);

        int risk = (offer.planet != null) ? Mathf.Clamp(offer.planet.riskLevel, 0, maxRisk) : 0;
        EnsureDangerIcons(RiskToWarningIconCount(risk));
    }

    private void EnsureCargoIcons(DeliveryContractDefinition contract)
    {
        if (cargoIconsRoot == null || cargoIconLibrary == null || cargoCategoryIconPrefab == null)
        {
            // Hide if not configured
            for (int i = 0; i < _cargoIcons.Count; i++)
                if (_cargoIcons[i] != null) _cargoIcons[i].gameObject.SetActive(false);
            return;
        }

        ContractCargoCategoryUtil.BuildCategoryCounts(contract, _tmpCounts);

        List<ItemCategory> showCats = new List<ItemCategory>(8);
        for (int i = 0; i < ContractCargoCategoryUtil.DisplayOrder.Length; i++)
        {
            ItemCategory cat = ContractCargoCategoryUtil.DisplayOrder[i];
            if (!_tmpCounts.TryGetValue(cat, out int count) || count <= 0)
                continue;

            if (cat == ItemCategory.None)
                continue;

            Sprite icon = cargoIconLibrary.GetIcon(cat);
            if (icon == null)
                continue;

            showCats.Add(cat);
        }

        while (_cargoIcons.Count < showCats.Count)
        {
            GameObject go = Instantiate(cargoCategoryIconPrefab);
            go.transform.SetParent(cargoIconsRoot, false);

            var widget = go.GetComponent<CategoryIconCountWidget>();
            if (!widget)
                widget = go.AddComponent<CategoryIconCountWidget>();

            go.SetActive(false);

            RectTransform rtNew = go.GetComponent<RectTransform>();
            if (rtNew != null)
                rtNew.localScale = Vector3.one;

            _cargoIcons.Add(widget);
        }

        for (int i = 0; i < _cargoIcons.Count; i++)
        {
            var widget = _cargoIcons[i];
            if (!widget) continue;

            bool active = i < showCats.Count;
            widget.gameObject.SetActive(active);

            if (!active)
                continue;

            ItemCategory cat = showCats[i];
            int count = _tmpCounts.TryGetValue(cat, out int c) ? c : 0;
            Sprite icon = cargoIconLibrary.GetIcon(cat);
            widget.Set(icon, count);

            var rt = widget.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                if (forceCargoIconSize)
                    rt.sizeDelta = cargoIconSize;
            }

            var le = widget.GetComponent<LayoutElement>();
            if (!le) le = widget.gameObject.AddComponent<LayoutElement>();
            if (forceCargoIconSize)
            {
                le.preferredWidth = cargoIconSize.x;
                le.preferredHeight = cargoIconSize.y;
                le.minWidth = cargoIconSize.x;
                le.minHeight = cargoIconSize.y;
            }
        }

        if (!autoLayoutCargoIcons)
            return;

        RectTransform rootRt = cargoIconsRoot as RectTransform;
        if (rootRt == null)
            rootRt = cargoIconsRoot.GetComponent<RectTransform>();

        if (rootRt == null)
            return;

        if (rootRt.GetComponent<LayoutGroup>() != null)
            return;

        int visibleIndex = 0;
        for (int i = 0; i < _cargoIcons.Count; i++)
        {
            var widget = _cargoIcons[i];
            if (!widget || !widget.gameObject.activeSelf) continue;

            var rt = widget.GetComponent<RectTransform>();
            if (!rt) continue;

            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localScale = Vector3.one;

            if (forceCargoIconSize)
                rt.sizeDelta = cargoIconSize;

            rt.anchoredPosition = cargoIconStartOffset + new Vector2(visibleIndex * cargoIconSpacing, 0f);
            visibleIndex++;
        }
    }

    private int RiskToWarningIconCount(int risk)
    {
        if (riskPerIcon <= 0) return 0;
        if (risk <= 0) return 0;

        int icons = Mathf.CeilToInt(risk / (float)riskPerIcon);
        return Mathf.Clamp(icons, 0, maxDangerIcons);
    }

    private void EnsureDangerIcons(int desiredCount)
    {
        if (dangerIconsRoot == null || dangerIconPrefab == null)
            return;

        desiredCount = Mathf.Clamp(desiredCount, 0, maxDangerIcons);

        // Grow pool
        while (_dangerIcons.Count < desiredCount)
        {
            GameObject go = Instantiate(dangerIconPrefab);
            go.transform.SetParent(dangerIconsRoot, false);
            go.SetActive(false);

            RectTransform rtNew = go.GetComponent<RectTransform>();
            if (rtNew != null)
                rtNew.localScale = Vector3.one;

            _dangerIcons.Add(go);
        }

        // Ensure parent + active state
        for (int i = 0; i < _dangerIcons.Count; i++)
        {
            var go = _dangerIcons[i];
            if (go == null) continue;

            if (go.transform.parent != dangerIconsRoot)
                go.transform.SetParent(dangerIconsRoot, worldPositionStays: false);

            go.SetActive(i < desiredCount);
        }

        if (!autoLayoutDangerIcons)
            return;

        RectTransform rootRt = dangerIconsRoot as RectTransform;
        if (rootRt == null)
            rootRt = dangerIconsRoot.GetComponent<RectTransform>();

        if (rootRt == null)
            return;

        var layoutGroup = rootRt.GetComponent<UnityEngine.UI.LayoutGroup>();
        if (layoutGroup != null)
        {
            if (forceDangerIconSize)
            {
                for (int i = 0; i < _dangerIcons.Count; i++)
                {
                    var go = _dangerIcons[i];
                    if (!go) continue;

                    var rt = go.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.localScale = Vector3.one;
                        rt.sizeDelta = dangerIconSize;
                    }

                    var le = go.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (!le) le = go.AddComponent<UnityEngine.UI.LayoutElement>();
                    le.preferredWidth = dangerIconSize.x;
                    le.preferredHeight = dangerIconSize.y;
                    le.minWidth = dangerIconSize.x;
                    le.minHeight = dangerIconSize.y;
                }
            }
            return;
        }

        int visibleIndex = 0;
        for (int i = 0; i < _dangerIcons.Count; i++)
        {
            var go = _dangerIcons[i];
            if (!go || !go.activeSelf) continue;

            var rt = go.GetComponent<RectTransform>();
            if (!rt) continue;

            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.localScale = Vector3.one;

            if (forceDangerIconSize)
                rt.sizeDelta = dangerIconSize;

            rt.anchoredPosition = dangerIconStartOffset + new Vector2(visibleIndex * dangerIconSpacing, 0f);
            visibleIndex++;
        }
    }
}
