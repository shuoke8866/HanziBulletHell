using UnityEngine;

/// <summary>
/// 笔画的颜色/名称统一出口。
/// 世界里掉的笔画、HUD 快捷栏、背包格子都必须用同一套颜色，否则玩家对不上。
/// </summary>
public static class StrokeUtil
{
    public static Color ColorOf(StrokeType s)
    {
        switch (s)
        {
            case StrokeType.Heng: return new Color(0.35f, 1f, 0.40f);   // 横 绿
            case StrokeType.Shu:  return new Color(0.30f, 0.90f, 1f);   // 竖 青
            case StrokeType.Pie:  return new Color(0.70f, 0.35f, 1f);   // 撇 紫
            case StrokeType.Na:   return new Color(1f, 0.55f, 0.10f);   // 捺 橙
            case StrokeType.Dian: return new Color(1f, 0.95f, 0.25f);   // 点 黄
            case StrokeType.Zhe:  return new Color(1f, 0.30f, 0.85f);   // 折 粉
            default: return UnityEngine.Color.white;
        }
    }

    public static string NameOf(StrokeType s)
    {
        switch (s)
        {
            case StrokeType.Heng: return "横";
            case StrokeType.Shu:  return "竖";
            case StrokeType.Pie:  return "撇";
            case StrokeType.Na:   return "捺";
            case StrokeType.Dian: return "点";
            case StrokeType.Zhe:  return "折";
            default: return "?";
        }
    }

    // 带颜色标签的富文本（TMP 用）
    public static string RichName(StrokeType s)
    {
        Color c = ColorOf(s);
        string hex = ColorUtility.ToHtmlStringRGB(c);
        return $"<color=#{hex}>{NameOf(s)}</color>";
    }
}
