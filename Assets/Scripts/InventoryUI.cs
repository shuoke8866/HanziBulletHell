using UnityEngine;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public TextMeshProUGUI inventoryText;
    public StrokeInventory inventory;   // 拖 Player

    void Update()
    {
        if (inventoryText == null || inventory == null) return;

        if (inventory.strokes.Count == 0)
        {
            inventoryText.text = "背包：空";
            return;
        }

        string text = "背包：";
        for (int i = 0; i < inventory.strokes.Count; i++)
        {
            text += $"[{i + 1}]{StrokeName(inventory.strokes[i])} ";
        }
        inventoryText.text = text;
    }

    string StrokeName(StrokeType s)
    {
        switch (s)
        {
            case StrokeType.Heng: return "横";
            case StrokeType.Shu: return "竖";
            case StrokeType.Pie: return "撇";
            case StrokeType.Na: return "捺";
            case StrokeType.Dian: return "点";
            case StrokeType.Zhe: return "折";
            default: return "?";
        }
    }
}