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

    /// <summary>
    /// 取（或生成）一个贴图 Sprite。
    /// shape 接收归一化到 [-1,1] 的坐标，返回该像素的不透明度。
    /// </summary>
    static Sprite Get(string key, int size, System.Func<float, float, float> shape)
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

                // 圆形遮罩：保证旋转后边缘仍平滑
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float edge = Mathf.Clamp01((1f - r) * size * 0.35f);
                float a = Mathf.Clamp01(shape(nx, ny)) * edge;

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
