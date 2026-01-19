// Assets/Scripts/Player/PlayerEnergyUI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Player energy HUD:
/// - Slider for current energy
/// - Optional text (e.g., "99 / 100")
/// - Bolt icons under the bar that reflect current drain units
///
/// This version is robust against overlap by:
/// - Forcing sane RectTransform anchors/pivot for each icon
/// - Optionally forcing icon size
/// - Auto laying out horizontally when no LayoutGroup is present
/// - If a LayoutGroup is present, it will add/adjust a LayoutElement so the group can size/space correctly
/// </summary>
[DisallowMultipleComponent]
public class PlayerEnergyUI : MonoBehaviour
{
    [Header("Binding")]
    public PlayerController player;
    public Energy energy;
    public bool autoBind = true;

    [Header("UI")]
    public Slider energySlider;
    public TextMeshProUGUI energyText;

    [Tooltip("Parent transform that will contain bolt icons.")]
    public RectTransform boltsRoot;

    [Tooltip("Prefab for a single bolt icon (UI Image).")]
    public GameObject boltPrefab;

    [Min(1)]
    public int maxBoltsToShow = 12;

    [Tooltip("Optional canvas group for show/hide.")]
    public CanvasGroup canvasGroup;

    [Header("Bolt Layout")]
    [Tooltip("If boltsRoot has no LayoutGroup, icons will be positioned manually so they don't overlap.")]
    public bool autoLayoutBolts = true;

    [Min(0f)]
    public float boltSpacing = 22f;

    public Vector2 boltStartOffset = Vector2.zero;

    [Tooltip("Force each bolt icon to a fixed size (recommended if icons appear tiny or inconsistent).")]
    public bool forceBoltSize = true;

    public Vector2 boltSize = new Vector2(24f, 24f);

    private readonly List<GameObject> boltPool = new List<GameObject>(16);

    private float lastCur = -999f;
    private float lastMax = -999f;
    private int lastUnits = int.MinValue;

    void Awake()
    {
        if (autoBind)
            ResolveReferences();

        ResolveUIReferences();
        ForceRefresh();
    }

    void OnEnable()
    {
        if (autoBind)
            ResolveReferences();

        ForceRefresh();
    }

    void Update()
    {
        if (!energy) return;

        if (!Mathf.Approximately(lastCur, energy.currentEnergy) || !Mathf.Approximately(lastMax, energy.maxEnergy))
            RefreshEnergy();

        if (lastUnits != energy.CurrentDrainUnits)
            RefreshBolts();
    }

    public void ForceRefresh()
    {
        if (autoBind)
            ResolveReferences();

        RefreshEnergy();
        RefreshBolts();
    }

    private void ResolveReferences()
    {
        if (!player)
            player = FindFirstObjectByType<PlayerController>();

        if (!energy && player)
            energy = player.energy != null ? player.energy : player.GetComponent<Energy>();

        if (!energy && !player)
            energy = FindFirstObjectByType<Energy>();
    }

    private void ResolveUIReferences()
    {
        if (!energySlider)
            energySlider = GetComponentInChildren<Slider>(true);

        if (!energyText)
            energyText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    private void RefreshEnergy()
    {
        if (!energy)
        {
            if (energySlider)
            {
                energySlider.minValue = 0f;
                energySlider.maxValue = 1f;
                energySlider.value = 0f;
            }

            if (energyText)
                energyText.text = "";

            SetVisible(true);
            return;
        }

        float max = Mathf.Max(1f, energy.maxEnergy);
        float cur = Mathf.Clamp(energy.currentEnergy, 0f, max);

        lastCur = cur;
        lastMax = max;

        if (energySlider)
        {
            energySlider.minValue = 0f;
            energySlider.maxValue = max;
            energySlider.value = cur;
        }

        if (energyText)
            energyText.text = $"{Mathf.RoundToInt(cur)} / {Mathf.RoundToInt(max)}";

        SetVisible(true);
    }

    private void RefreshBolts()
    {
        if (!energy)
        {
            lastUnits = 0;
            SetBoltCount(0);
            return;
        }

        lastUnits = energy.CurrentDrainUnits;
        SetBoltCount(lastUnits);
    }

    private void SetBoltCount(int units)
    {
        if (!boltsRoot || !boltPrefab || maxBoltsToShow <= 0)
            return;

        units = Mathf.Clamp(units, 0, maxBoltsToShow);

        EnsurePoolSize(maxBoltsToShow);

        for (int i = 0; i < boltPool.Count; i++)
        {
            bool active = (i < units);
            if (boltPool[i].activeSelf != active)
                boltPool[i].SetActive(active);
        }

        // Layout every time in case the user added/removed LayoutGroup at runtime.
        if (autoLayoutBolts)
        {
            var layoutGroup = boltsRoot.GetComponent<LayoutGroup>();
            if (layoutGroup != null)
            {
                EnsureLayoutElementSizes();
            }
            else
            {
                LayoutBoltsManually();
            }
        }
    }

    private void EnsurePoolSize(int targetSize)
    {
        while (boltPool.Count < targetSize)
        {
            GameObject go = Instantiate(boltPrefab);
            go.transform.SetParent(boltsRoot, false);
            go.SetActive(false);

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt)
            {
                rt.localScale = Vector3.one;
            }

            boltPool.Add(go);
        }
    }

    private void EnsureLayoutElementSizes()
    {
        if (!forceBoltSize) return;

        for (int i = 0; i < boltPool.Count; i++)
        {
            if (!boltPool[i]) continue;

            RectTransform rt = boltPool[i].GetComponent<RectTransform>();
            if (rt)
            {
                rt.localScale = Vector3.one;
                rt.sizeDelta = boltSize;
            }

            LayoutElement le = boltPool[i].GetComponent<LayoutElement>();
            if (!le) le = boltPool[i].AddComponent<LayoutElement>();

            le.preferredWidth = boltSize.x;
            le.preferredHeight = boltSize.y;
            le.minWidth = boltSize.x;
            le.minHeight = boltSize.y;
        }
    }

    private void LayoutBoltsManually()
    {
        for (int i = 0; i < boltPool.Count; i++)
        {
            RectTransform rt = boltPool[i].GetComponent<RectTransform>();
            if (!rt) continue;

            // Force sane anchors/pivot so anchoredPosition works predictably.
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);

            rt.localScale = Vector3.one;

            if (forceBoltSize)
                rt.sizeDelta = boltSize;

            rt.anchoredPosition = boltStartOffset + new Vector2(i * boltSpacing, 0f);
        }
    }

    private void SetVisible(bool visible)
    {
        if (!canvasGroup) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
