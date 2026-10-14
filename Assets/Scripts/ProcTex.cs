using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化贴图工具。
///
/// 项目没有美术资源，但粒子/拖尾都需要 Sprite。这里用代码生成几个通用形状
/// 并缓存复用 —— 既避免引入美术文件（体积为零），也不用在 Inspector 里配置。
/// </summary>
public static class ProcTex
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    /// <summary>柔和圆点：适合火花、叠加辉光</summary>
    public static Sprite Dot
    {
        get { return Get("dot", 32, (x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y))); }
    }

    /// <summary>硬边方块：适合碎片、命中碎屑</summary>
    public static Sprite Square
    {
        get { return Get("square", 16, (x, y) => 1f); }
    }

    /// <summary>细长条：适合拖尾、速度线</summary>
    public static Sprite Streak
    {
        get { return Get("streak", 32, (x, y) => Mathf.Clamp01(1f - Mathf.Abs(x) * 3f) * Mathf.Clamp01(1f - Mathf.Abs(y))); }
    }

    // ================= 笔画形状（弹幕外观）=================
    //
    // 需求：子弹外观长成对应笔画的形状 —— 横是细长横条、竖是细长竖条、
    // 撇捺是斜条、点是圆点、折是折角形。发射时整个子弹会按弹道方向旋转，
    // 所以这里只定义"基准朝向"（横条沿 X 轴、斜条沿 45°）。

    /// <summary>横（一）：沿 X 轴的细长横条</summary>
    public static Sprite StrokeHeng
    {
        get
        {
            return GetSharp("stroke_heng", 48, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                if (ax > 0.92f) return 0f;
                float thick = 0.20f * (1f - ax * 0.8f);   // 向两端收细
                return ay <= thick ? 1f : 0f;
            });
        }
    }

    /// <summary>竖（丨）：沿 Y 轴的细长竖条</summary>
    public static Sprite StrokeShu
    {
        get
        {
            return GetSharp("stroke_shu", 48, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                if (ay > 0.92f) return 0f;
                float thick = 0.20f * (1f - ay * 0.5f);
                return ax <= thick ? 1f : 0f;
            });
        }
    }

    /// <summary>撇 / 捺：45° 斜条，尾端收细（像笔锋）</summary>
    public static Sprite StrokeXie
    {
        get
        {
            return GetSharp("stroke_xie", 48, (x, y) =>
            {
                float d = Mathf.Abs(y - x) * 0.7071f;      // 到对角线 y=x 的距离
                float len = Mathf.Abs(x + y) * 0.7071f;    // 沿对角线的位置
                if (len > 0.90f) return 0f;
                float thick = 0.20f * (1f - len * 0.55f);
                return d <= thick ? 1f : 0f;
            });
        }
    }

    /// <summary>点（丶）：实心圆点</summary>
    public static Sprite StrokeDian
    {
        get
        {
            return GetSharp("stroke_dian", 40, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return r <= 0.55f ? 1f : 0f;
            });
        }
    }

    /// <summary>折（乙）：带拐角的折线形</summary>
    public static Sprite StrokeZhe
    {
        get
        {
            return GetSharp("stroke_zhe", 48, (x, y) =>
            {
                bool vertical = Mathf.Abs(x + 0.35f) <= 0.16f && y >= -0.35f && y <= 0.85f;
                bool horizontal = Mathf.Abs(y + 0.35f) <= 0.16f && x >= -0.35f && x <= 0.85f;
                return (vertical || horizontal) ? 1f : 0f;
            });
        }
    }

    /// <summary>按笔画类型取对应的弹幕外观</summary>
    public static Sprite ForStroke(StrokeType type)
    {
        switch (type)
        {
            case StrokeType.Heng: return StrokeHeng;
            case StrokeType.Shu: return StrokeShu;
            case StrokeType.Pie: return StrokeXie;
            case StrokeType.Na: return StrokeXie;
            case StrokeType.Dian: return StrokeDian;
            case StrokeType.Zhe: return StrokeZhe;
            default: return Dot;
        }
    }

    /// <summary>
    /// 取（或生成）一个贴图 Sprite。
    /// shape 接收归一化到 [-1,1] 的坐标，返回该像素的不透明度。
    /// </summary>
    static Sprite Get(string key, int size, System.Func<float, float, float> shape)
    {
        // 圆形遮罩版本：适合点、碎片这类需要边缘平滑的形状
        return Build(key, size, shape, true);
    }

    /// <summary>
    /// 同 Get，但**不加圆形遮罩**。
    ///
    /// 笔画形状（细长横条/竖条/斜条）必须用这个版本：
    /// 圆形遮罩会在离中心较远处把 alpha 淡出，而横条的两端恰好在那片区域，
    /// 结果条纹会被削成"枣核形"，看不出是笔画。
    /// </summary>
    static Sprite GetSharp(string key, int size, System.Func<float, float, float> shape)
    {
        return Build(key, size, shape, false);
    }

    static Sprite Build(string key, int size, System.Func<float, float, float> shape, bool roundMask)
    {
        Sprite cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f - half) / half;
                float ny = (y + 0.5f - half) / half;

                float a;
                if (roundMask)
                {
                    // 圆形遮罩：保证旋转后边缘仍平滑
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float edge = Mathf.Clamp01((1f - r) * size * 0.35f);
                    a = Mathf.Clamp01(shape(nx, ny)) * edge;
                }
                else
                {
                    // 不做圆形淡出，只在最外 1 像素轻微抗锯齿
                    float m = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                    float edge = Mathf.Clamp01((1f - m) * size * 0.25f);
                    a = Mathf.Clamp01(shape(nx, ny)) * Mathf.Min(1f, edge + 0.15f);
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var sp = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);
        cache[key] = sp;
        return sp;
    }
}
