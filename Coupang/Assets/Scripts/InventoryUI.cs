using UnityEngine;
using UnityEngine.UI;


public class InventoryUI : MonoBehaviour
{
    public Image[] slotIcons; // 5°³
    public Color emptyColor = new Color(1, 1, 1, 0.1f);
    public Color filledColor = Color.white;
    public Color activeOutline = Color.yellow;


    public void Refresh(InventorySystem inv, int active)
    {
        for (int i = 0; i < slotIcons.Length; i++)
        {
            var img = slotIcons[i];
            var item = inv.GetSlotRef(i);
            if (item != null)
            {
                img.sprite = item.def.icon;
                img.color = filledColor;
            }
            else
            {
                img.sprite = null;
                img.color = emptyColor;
            }
            var outline = img.GetComponent<Outline>();
            if (outline)
            {
                outline.effectColor = (i == active) ? activeOutline : new Color(0, 0, 0, 0.5f);
            }
        }
    }
}