using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class InventoryUI : MonoBehaviour
{
    public Image[] slotIcons;       // assign 5 Images
    public int activeColorA = 255;  // highlight alpha
    public int inactiveColorA = 128;

    private InventorySystem inv;

    public void Bind(InventorySystem inventory)
    {
        inv = inventory;
        if (inv) inv.OnInventoryChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        if (!inv || slotIcons == null) return;

        for (int i = 0; i < slotIcons.Length; i++)
        {
            var img = slotIcons[i];
            if (!img) continue;
            var sp = inv.GetIconAt(i);
            img.sprite = sp;
            img.enabled = sp != null;

            var c = img.color;
            c.a = (i == inv.ActiveIndex) ? activeColorA / 255f : inactiveColorA / 255f;
            img.color = c;
        }
    }
}
