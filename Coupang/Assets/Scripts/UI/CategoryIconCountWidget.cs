using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple UI widget: category icon + count text.
/// Put this on a prefab with an Image + TMP_Text.
/// </summary>
public class CategoryIconCountWidget : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text countText;

    [Tooltip("If true, hides the count when it is 1.")]
    public bool hideCountWhenOne = false;

    [Tooltip("Count text format. Example: '{0}' or 'x{0}'.")]
    public string countFormat = "{0}";

    public void Set(Sprite icon, int count)
    {
        if (iconImage != null)
            iconImage.sprite = icon;

        if (countText != null)
        {
            if (hideCountWhenOne && count <= 1)
            {
                countText.text = string.Empty;
            }
            else
            {
                if (count < 0) count = 0;
                countText.text = string.Format(countFormat, count);
            }
        }
    }

    private void Reset()
    {
        if (!iconImage) iconImage = GetComponentInChildren<Image>(true);
        if (!countText) countText = GetComponentInChildren<TMP_Text>(true);
    }
}
