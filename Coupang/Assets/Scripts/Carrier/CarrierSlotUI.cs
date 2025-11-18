// Assets/Scripts/UI/CarrierSlotInspectorUI.cs
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class CarrierSlotUI : MonoBehaviour
{
    [Header("Refs")]
    public TextMeshProUGUI label;
    public CanvasGroup canvasGroup;

    [Header("Config")]
    public float fadeSpeed = 10f;
    public float autoCloseDistance = 6f;

    private CarrierController currentCarrier;
    private Transform viewer;
    private bool visible;

    void Awake()
    {
        if (!label) label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    void Update()
    {
        if (!visible)
        {
            if (canvasGroup && canvasGroup.alpha > 0f)
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, fadeSpeed * Time.deltaTime);
            return;
        }

        if (currentCarrier == null || viewer == null)
        {
            Close();
            return;
        }

        float dist = Vector3.Distance(viewer.position, currentCarrier.transform.position);
        if (dist > autoCloseDistance)
        {
            Close();
            return;
        }

        if (canvasGroup && canvasGroup.alpha < 1f)
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, fadeSpeed * Time.deltaTime);
    }

    public void Open(CarrierController carrier, Transform viewerTransform)
    {
        currentCarrier = carrier;
        viewer = viewerTransform;
        visible = true;

        if (label != null && carrier != null)
        {
            label.text = carrier.GetSlotDebugString();
        }

        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    public void Close()
    {
        visible = false;
        currentCarrier = null;
        viewer = null;

        if (canvasGroup)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }
}
