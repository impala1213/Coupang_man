using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI panel renderer for a DestinationTerminal.
/// Manual bindings:
/// - Wire fixed category icon slots (like Ship monitor).
/// - Layout must stay; we only change alpha.
/// 
/// Alpha rules (per requested spec):
/// - Not assigned to this terminal: alphaUnassigned (0)  (slot stays but invisible)
/// - Assigned but not yet satisfied: alphaAssigned (10)
/// - Assigned and satisfied: alphaSatisfied (255)
/// 
/// Note text:
/// - Shows per-item-type progress (current/required) for the items assigned to this terminal.
/// </summary>
public class DestinationTerminalPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class CategoryRow
    {
        public ItemCategory category = ItemCategory.None;
        public Image icon;
        public TMP_Text countText; // optional
    }

    [Header("Bindings")]
    public CategoryRow[] rows;

    [Tooltip("Optional note text under the icons. Shows per-item-type progress.")]
    public TMP_Text noteText;

    [Header("Alpha")]
    [Range(0,255)] public int alphaSatisfied = 255;
    [Range(0,255)] public int alphaAssigned = 10;
    [Range(0,255)] public int alphaUnassigned = 0;

    [Header("Count Display (optional per row)")]
    public bool showCurrentOverRequired = true; // "2/4"
    public bool hideRowCountTextIfUnassigned = true;

    private DestinationTerminalInteractable _terminal;

    private readonly Dictionary<ItemDefinition, int> _deliveredByDef = new Dictionary<ItemDefinition, int>(64);
    private readonly StringBuilder _sb = new StringBuilder(512);

    public void Bind(DestinationTerminalInteractable terminal)
    {
        _terminal = terminal;
        Refresh();
    }

    public void Refresh()
    {
        RefreshLive();
    }

    private void RefreshLive()
    {
        if (_terminal == null)
            return;

        var dz = _terminal.dropZone;
        if (dz != null)
            dz.GetDeliveredUnitsByDefinition(_deliveredByDef);
        else
            _deliveredByDef.Clear();

        var quotaByDef = _terminal.GetQuotaByDefinition();
        var quotaByCat = _terminal.GetQuotaByCategory();

        float aSat = Mathf.Clamp01(alphaSatisfied / 255f);
        float aAss = Mathf.Clamp01(alphaAssigned / 255f);
        float aUn = Mathf.Clamp01(alphaUnassigned / 255f);

        // Icons
        if (rows != null)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                if (r == null) continue;

                int requiredCat = 0;
                bool assigned = quotaByCat != null && quotaByCat.TryGetValue(r.category, out requiredCat) && requiredCat > 0;

                float a;
                int cur = 0;

                if (!assigned)
                {
                    a = aUn;
                }
                else
                {
                    cur = ComputeCategoryProgress(r.category, quotaByDef, _deliveredByDef);
                    bool satisfied = cur >= requiredCat;
                    a = satisfied ? aSat : aAss;
                }

                if (r.icon != null)
                {
                    var c = r.icon.color;
                    c.a = a;
                    r.icon.color = c;
                }

                if (r.countText != null)
                {
                    if (!assigned && hideRowCountTextIfUnassigned)
                    {
                        r.countText.text = string.Empty;
                        var tc = r.countText.color;
                        tc.a = aUn;
                        r.countText.color = tc;
                    }
                    else
                    {
                        r.countText.text = showCurrentOverRequired ? $"{cur}/{requiredCat}" : $"{requiredCat}";
                        var tc = r.countText.color;
                        tc.a = a;
                        r.countText.color = tc;
                    }
                }
            }
        }

        // Note (per item type)
        if (noteText != null)
        {
            _sb.Clear();

            if (quotaByDef == null || quotaByDef.Count == 0)
            {
                noteText.text = string.Empty;
            }
            else
            {
                // Group by category for readability
                foreach (var cat in System.Enum.GetValues(typeof(ItemCategory)))
                {
                    var ccat = (ItemCategory)cat;
                    if (ccat == ItemCategory.None) continue;

                    bool anyInCat = false;

                    // First pass: detect
                    foreach (var kv in quotaByDef)
                    {
                        var def = kv.Key;
                        if (def == null) continue;
                        if (def.category != ccat) continue;

                        int req = Mathf.Max(0, kv.Value);
                        if (req <= 0) continue;

                        anyInCat = true;
                        break;
                    }

                    if (!anyInCat) continue;

                    _sb.Append(ccat.ToString()).AppendLine();

                    foreach (var kv in quotaByDef)
                    {
                        var def = kv.Key;
                        if (def == null) continue;
                        if (def.category != ccat) continue;

                        int req = Mathf.Max(0, kv.Value);
                        if (req <= 0) continue;

                        int cur = _deliveredByDef.TryGetValue(def, out int got) ? Mathf.Max(0, got) : 0;

                        _sb.Append(" - ")
                           .Append(string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName)
                           .Append("  ")
                           .Append(cur)
                           .Append("/")
                           .Append(req)
                           .AppendLine();
                    }

                    _sb.AppendLine();
                }

                noteText.text = _sb.ToString().TrimEnd();
            }
        }
    }

    private static int ComputeCategoryProgress(ItemCategory category, IReadOnlyDictionary<ItemDefinition, int> quotaByDef, Dictionary<ItemDefinition, int> deliveredByDef)
    {
        if (quotaByDef == null || deliveredByDef == null)
            return 0;

        int sum = 0;
        foreach (var kv in quotaByDef)
        {
            var def = kv.Key;
            if (def == null) continue;
            if (def.category != category) continue;

            int req = Mathf.Max(0, kv.Value);
            if (req <= 0) continue;

            int got = deliveredByDef.TryGetValue(def, out int g) ? Mathf.Max(0, g) : 0;
            sum += Mathf.Min(req, got);
        }

        return sum;
    }
}
