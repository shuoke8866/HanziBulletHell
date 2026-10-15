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

    // ================= 敌人底形 =================
    //
    // 敌人原来只有身上的汉字，外形无从区分。这里用代码画几个多边形垫在背后 ——
    // 不需要任何美术资源，换个形状就是换个函数。

    /// <summary>按 <see cref="EnemyBodyShape"/> 取底形贴图</summary>
    public static Sprite ForBodyShape(EnemyBodyShape shape)
    {
        switch (shape)
        {
            case EnemyBodyShape.Circle: return BodyCircle;
            case EnemyBodyShape.Square: return BodySquare;
            case EnemyBodyShape.Diamond: return BodyDiamond;
            case EnemyBodyShape.Triangle: return BodyTriangle;
            case EnemyBodyShape.Hexagon: return BodyHexagon;
            case EnemyBodyShape.Star: return BodyStar;
            case EnemyBodyShape.Ring: return BodyRing;
            default: return null;      // None → 不加底形
        }
    }

    // 多边形判定用"极坐标 + 边数"统一写：把角度映射到一个凸多边形上，
    // 比逐个手写半平面判定短得多，也好加新形状。
    static float Polygon(float nx, float ny, int sides, float innerRadius)
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        if (r < 0.0001f) return 1f;

        float ang = Mathf.Atan2(ny, nx);
        float seg = Mathf.PI * 2f / sides;

        // 把角度折到 [-seg/2, seg/2] 再取余，得到这个方向上多边形的边界半径
        float a = Mathf.Repeat(ang + seg * 0.5f, seg) - seg * 0.5f;
        float edge = innerRadius * Mathf.Cos(seg * 0.5f) / Mathf.Cos(a);

        return Mathf.Clamp01((edge - r) * 40f);   // 抗锯齿靠这个陡峭的过渡
    }

    public static Sprite BodyCircle
    {
        get { return Build("body_circle", 64, (x, y) => Polygon(x, y, 32, 1f), false); }
    }

    public static Sprite BodySquare
    {
        get { return Build("body_square", 64, (x, y) => Polygon(x, y, 4, 1f), false); }
    }

    public static Sprite BodyDiamond
    {
        get { return Build("body_diamond", 64, (x, y) => Polygon(x, y, 4, 0.72f), false); }
    }

    public static Sprite BodyTriangle
    {
        get { return Build("body_triangle", 64, (x, y) => Polygon(x, y, 3, 1f), false); }
    }

    public static Sprite BodyHexagon
    {
        get { return Build("body_hexagon", 64, (x, y) => Polygon(x, y, 6, 1f), false); }
    }

    /// <summary>五角星：外圈用 5 边形，内圈挖掉一圈</summary>
    public static Sprite BodyStar
    {
        get
        {
            return Build("body_star", 64, (x, y) =>
            {
                float outer = Polygon(x, y, 5, 1f);

                // 内圈（半径 0.45 的 5 边形）挖空
                float r = Mathf.Sqrt(x * x + y * y);
                float ang = Mathf.Atan2(y, x);
                float seg = Mathf.PI * 2f / 5f;
                float a = Mathf.Repeat(ang + seg * 0.5f, seg) - seg * 0.5f;
                float innerEdge = 0.45f * Mathf.Cos(seg * 0.5f) / Mathf.Cos(a);
                float inner = Mathf.Clamp01((r - innerEdge) * 40f);

                return outer * inner;
            }, false);
        }
    }

    /// <summary>空心圆环</summary>
    public static Sprite BodyRing
    {
        get
        {
            return Build("body_ring", 64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float outer = Mathf.Clamp01((1f - r) * 40f);
                float inner = Mathf.Clamp01((r - 0.62f) * 40f);
                return outer * inner;
            }, false);
        }
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
