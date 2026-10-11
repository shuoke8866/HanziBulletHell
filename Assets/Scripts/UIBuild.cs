using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时 UI 构建工具。
///
/// 项目里所有界面（背包、快捷栏、提示条）都是代码生成而非预制体，
/// 这里把重复的样板收拢到一处，避免每个界面各写一遍。
/// 字体不显式指定 —— TMP 默认字体已被 HanziFontFixer 设为 NotoSansSC。
/// </summary>
public static class UIBuild
{
    /// <summary>建一个铺满父级的 RectTransform 容器</summary>
    public static RectTransform FullScreen(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>建一个指定大小的居中容器</summary>
    public static RectTransform CenteredPanel(Transform parent, string name,
                                             float width, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, height);
        return rt;
    }

    /// <summary>加一张纯色背景（可作遮罩）</summary>
    public static Image Background(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>建文本</summary>
    public static TextMeshProUGUI Text(Transform parent, string name, string content,
                                      float size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        return t;
    }

    /// <summary>把文本锚定到父级顶部（从上往下排）</summary>
    public static void AnchorTop(RectTransform rt, float y, float width, float height)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, height);
    }

    /// <summary>把文本锚定到父级底部（从下往上排）</summary>
    public static void AnchorBottom(RectTransform rt, float y, float width, float height)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, height);
    }

    /// <summary>把一个 Sprite 显示成 Image（用于展示汉字字形预览）</summary>
    public static Image GlyphImage(Transform parent, string name, Sprite sprite,
                                   float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    /// <summary>
    /// 取一个可用的 Canvas。场景里没有就临时建一个（带 CanvasScaler），
    /// 保证覆盖层在任何场景都能正常显示。
    /// </summary>
    public static Canvas GetOrCreateCanvas()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas != null) return canvas;

        var go = new GameObject("RuntimeCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        go.AddComponent<GraphicRaycaster>();
        Object.DontDestroyOnLoad(go);
        return canvas;
    }
}
