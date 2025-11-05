// Assets/Scripts/UI/InteractionPromptUI.cs
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
using TMPro;
#endif

[DisallowMultipleComponent]
public class InteractionPromptUI : MonoBehaviour
{
    [Header("References")]
    public CameraSwitcher cameraSwitcher;      // active camera source
    public PlayerController player;           // to reuse distance/mask and player forward
    public CarrierController carrier;         // to switch text when loading cargo

    [Header("Detection (fallback if Player not set)")]
    public float interactDistance = 3f;
    public LayerMask interactMask = ~0;       // usually "Interactable"

    [Header("Z-Align Pickup (player forward)")]
    public bool zAlignEnabled = true;
    [Tooltip("Apply Z-align assist only in third person.")]
    public bool zAlignOnlyInThird = true;
    [Range(0.5f, 6f)] public float zAlignMaxDistance = 3.0f;
    [Range(0f, 25f)] public float zAlignMaxAngleDeg = 10f;
    [Range(0.15f, 1.0f)] public float zAlignSphereRadius = 0.45f;
    [Range(0.25f, 1.5f)] public float zAlignStep = 0.8f;
    [Range(1, 6)] public int zAlignSamples = 3;
    [Tooltip("Layers that block line of sight (exclude Interactable).")]
    public LayerMask zAlignLosBlockMask = ~0;

    [Header("UI")]
    public CanvasGroup canvasGroup;           // optional: fade in/out
    public float fadeSpeed = 10f;
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
    public TextMeshProUGUI tmpLabel;          // preferred
#endif
    public Text uiLabel;                       // fallback if TMP is not used
    public bool showItemName = true;

    [Header("Texts (English)")]
    public string pickUpText = "Press E to pick up";
    public string loadText = "Press E to load";
    public string pickUpCarrierText = "Press E to pick up carrier";

    private bool visible;

    void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
        tmpLabel = GetComponent<TextMeshProUGUI>();
#endif
        uiLabel = GetComponent<Text>();
    }

    void Update()
    {
        // If Player is set, always use the exact same detection as PlayerController (single source of truth)
        if (player && player.FindInteractCandidate(out var world) && TryBuildText(world, out string text))
        {
            SetText(text);
            SetVisible(true);
            SmoothFade();
            return;
        }

        // Fallback (rare): no Player reference ¡æ keep the old local detection if you want,
        // or simply hide the prompt.
        SetVisible(false);
        SmoothFade();
    }


    bool TryBuildText(WorldItem world, out string text)
    {
        text = null;
        if (!world) return false;

        var def = world.definition;
        // Choose base text
        string baseText;
        bool carrierActive = carrier && carrier.IsActive;

        if (def != null && def.isCarrier)
            baseText = pickUpCarrierText;
        else
            baseText = carrierActive ? loadText : pickUpText;

        // Append item name if requested
        if (showItemName && def != null && !string.IsNullOrEmpty(def.displayName))
            text = $"{baseText} [{def.displayName}]";
        else
            text = baseText;

        return true;
    }

    bool TryFindZAlignedItem(out WorldItem best, float maxDist, LayerMask mask)
    {
        best = null;
        if (!player) return false;

        Vector3 origin = player.transform.position + Vector3.up * 1.4f; // chest-ish origin
        Vector3 fwd = player.transform.forward;

        float cosThresh = Mathf.Cos(zAlignMaxAngleDeg * Mathf.Deg2Rad);
        float bestScore = float.PositiveInfinity;

        int samples = Mathf.Max(1, zAlignSamples);
        float step = Mathf.Max(0.1f, zAlignStep);

        for (int s = 1; s <= samples; s++)
        {
            float d = Mathf.Min(maxDist, s * step);
            Vector3 center = origin + fwd * d;

            var hits = Physics.OverlapSphere(center, zAlignSphereRadius, mask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) continue;

            for (int i = 0; i < hits.Length; i++)
            {
                var wi = hits[i].GetComponentInParent<WorldItem>();
                if (!wi) continue;

                Vector3 itemPos = wi.transform.position;
                Vector3 toItem = itemPos - origin;
                float dist = toItem.magnitude;
                if (dist <= 0.001f || dist > maxDist) continue;

                Vector3 dir = toItem / dist;
                float dot = Vector3.Dot(fwd, dir);
                if (dot < cosThresh) continue; // angle too wide from player Z

                // LOS check
                if (Physics.Linecast(origin, itemPos, out RaycastHit block, zAlignLosBlockMask, QueryTriggerInteraction.Ignore))
                {
                    if (!block.transform || block.transform.GetComponentInParent<WorldItem>() != wi)
                        continue;
                }

                // score: prefer alignment then closeness
                float angleScore = 1f - dot;
                float distScore = dist / maxDist;
                float score = angleScore * 2.0f + distScore * 0.5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = wi;
                }
            }
        }

        return best != null;
    }

    void SetText(string s)
    {
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
        if (tmpLabel) { tmpLabel.text = s; return; }
#endif
        if (uiLabel) uiLabel.text = s;
    }

    void SetVisible(bool v)
    {
        visible = v;
        if (!canvasGroup)
        {
#if TMP_PRESENT || TEXTMESHPRO_PRESENT
            if (!v && tmpLabel) tmpLabel.text = string.Empty;
#endif
            if (!v && uiLabel) uiLabel.text = string.Empty;
        }
    }

    void SmoothFade()
    {
        if (!canvasGroup) return;
        float target = visible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, fadeSpeed * Time.deltaTime);
    }
}
