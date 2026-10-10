using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 汉字字形渲染器。
///
/// 核心：每个笔画是一条**折线**（1~3 个点），渲染时
///   1) 用 miter 方式把折线变成带宽度的轮廓线段（拐角能正确接上，所以"折"像样）
///   2) 再对这些有宽度的线段做光栅化
///
/// 这样撇、捺、点、折都能真正画出来，比例也能控制，
/// 不会再出现"三的三横一样长""五不像五""折画断开"这类问题。
/// 字形完全不依赖字体文件，所以永远不会出现"豆腐块"□。
/// </summary>
public class HanziRenderer : MonoBehaviour
{
    public string character = "一";
    public Color color = Color.white;
    public int textureSize = 64;
    public float pixelsPerUnit = 32f;

    // ---------------- 数据 ----------------

    /// <summary>折线笔画（2~3 个点），渲染时会被展开成有宽度的轮廓线段。</summary>
    struct Poly
    {
        public Vector2[] pts;
        public float t;
        public Poly(Vector2[] p, float th) { pts = p; t = th; }
    }

    /// <summary>最终用于光栅化的带宽度线段，两端可以不同粗。</summary>
    struct Seg
    {
        public float x0, y0, x1, y1, t0, t1;
    }

    static Dictionary<string, Poly[]> defs;

    SpriteRenderer spriteRenderer;
    Sprite currentSprite;

    // ---------------- 生命周期 ----------------

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        if (defs == null) defs = BuildDefinitions();
        if (!spriteRenderer.enabled) spriteRenderer.enabled = true;

        SetCharacter(character);
    }

    public static bool IsAllowed(string ch)
    {
        if (defs == null) defs = BuildDefinitions();
        return !string.IsNullOrEmpty(ch) && defs.ContainsKey(ch);
    }

    public void SetStrokeColor(Color newColor)
    {
        color = newColor;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.color = newColor;
    }

    public void SetCharacter(string ch)
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (defs == null) defs = BuildDefinitions();

        if (string.IsNullOrEmpty(ch) || !defs.ContainsKey(ch))
        {
            Debug.LogWarning($"HanziRenderer: 字形库里没有「{ch}」，保持当前显示。");
            return;
        }

        character = ch;
        BuildSprite(ch, defs[ch]);
        spriteRenderer.color = color;

        if (!spriteRenderer.enabled) spriteRenderer.enabled = true;
    }

    public static Sprite CreateSpriteFor(string ch, int size = 64, float ppu = 32f)
    {
        if (defs == null) defs = BuildDefinitions();
        if (string.IsNullOrEmpty(ch)) return null;

        // 缓存：同一个字只光栅化一次，之后直接复用 Sprite。
        // 拾取笔画会频繁重建字形，不做缓存会每帧 new Texture2D + SetPixels + Apply，
        // 在主线程上造成明显卡顿。
        string key = ch + "|" + size + "|" + ppu;
        Sprite cached;
        if (spriteCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        Poly[] polys;
        if (!defs.TryGetValue(ch, out polys)) return null;

        Texture2D tex = Rasterize(polys, size);
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        spriteCache[key] = sp;
        return sp;
    }

    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    void BuildSprite(string ch, Poly[] polys)
    {
        // 复用缓存的 Sprite，避免每次换字都重建贴图（这是拾取卡顿的主因）
        string key = ch + "|" + textureSize + "|" + pixelsPerUnit;

        Sprite sp;
        if (spriteCache.TryGetValue(key, out sp) && sp != null)
        {
            currentSprite = sp;
            if (spriteRenderer != null) spriteRenderer.sprite = currentSprite;
            return;
        }

        Texture2D tex = Rasterize(polys, textureSize);
        tex.filterMode = FilterMode.Point;

        currentSprite = Sprite.Create(
            tex, new Rect(0, 0, textureSize, textureSize),
            new Vector2(0.5f, 0.5f), pixelsPerUnit);

        spriteCache[key] = currentSprite;

        if (spriteRenderer != null) spriteRenderer.sprite = currentSprite;
    }

    // ---------------- 折线 → 带宽度线段（miter 拐角）----------------

    static Seg[] BuildOutline(Poly p)
    {
        Vector2[] v = p.pts;
        if (v == null || v.Length < 2) return new Seg[0];

        var segs = new List<Seg>();

        if (v.Length == 2)
        {
            segs.Add(new Seg { x0 = v[0].x, y0 = v[0].y, x1 = v[1].x, y1 = v[1].y, t0 = p.t, t1 = p.t });
            return segs.ToArray();
        }

        // 找到离首尾连线最远的顶点作为拐点
        Vector2 a = v[0], b = v[v.Length - 1];
        Vector2 ab = b - a;
        float abLen = ab.magnitude;

        int corner = 1;
        float best = -1f;
        for (int i = 1; i < v.Length - 1; i++)
        {
            float d = abLen < 1e-6f
                ? (v[i] - a).magnitude
                : Mathf.Abs((v[i] - a).x * ab.y - (v[i] - a).y * ab.x) / abLen;
            if (d > best) { best = d; corner = i; }
        }

        // 前半段：满宽 → 半宽；后半段：半宽 → 满宽（这样拐角处两边能接上）
        segs.Add(new Seg
        {
            x0 = v[0].x, y0 = v[0].y, x1 = v[corner].x, y1 = v[corner].y,
            t0 = p.t, t1 = p.t * 0.5f
        });
        segs.Add(new Seg
        {
            x0 = v[corner].x, y0 = v[corner].y, x1 = v[v.Length - 1].x, y1 = v[v.Length - 1].y,
            t0 = p.t * 0.5f, t1 = p.t
        });

        return segs.ToArray();
    }

    // ---------------- 光栅化 ----------------

    static Texture2D Rasterize(Poly[] polys, int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        // 先把所有折线展开成线段，避免逐像素重复计算
        var all = new List<Seg>();
        for (int i = 0; i < polys.Length; i++)
        {
            Seg[] s = BuildOutline(polys[i]);
            for (int k = 0; k < s.Length; k++) all.Add(s[k]);
        }

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float py = (y + 0.5f) / size;

            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size;
                bool on = false;

                for (int i = 0; i < all.Count; i++)
                {
                    Seg s = all[i];
                    float dx = s.x1 - s.x0;
                    float dy = s.y1 - s.y0;
                    float lenSq = dx * dx + dy * dy;

                    float t;
                    if (lenSq < 1e-9f) t = 0f;
                    else
                    {
                        t = ((px - s.x0) * dx + (py - s.y0) * dy) / lenSq;
                        t = t < 0f ? 0f : (t > 1f ? 1f : t);
                    }

                    float cx = s.x0 + t * dx;
                    float cy = s.y0 + t * dy;
                    float half = Mathf.Lerp(s.t0, s.t1, t) * 0.5f;

                    float ex = px - cx, ey = py - cy;
                    if (ex * ex + ey * ey <= half * half) { on = true; break; }
                }

                pixels[y * size + x] = on ? Color.white : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ---------------- 字形定义 ----------------

    static Dictionary<string, Poly[]> BuildDefinitions()
    {
        var d = new Dictionary<string, Poly[]>();

        const float T = 0.105f;   // 标准笔画粗
        const float TT = 0.075f;  // 细一点（点、短撇）

        var L = new List<Poly>();

        // ---- 构造器（坐标 0~1，y 向上）----
        System.Action<List<Poly>, float, float, float> H =
            (l, x0, x1, y) => l.Add(new Poly(new[] { new Vector2(x0, y), new Vector2(x1, y) }, T));

        System.Action<List<Poly>, float, float, float> V =
            (l, x, y0, y1) => l.Add(new Poly(new[] { new Vector2(x, y0), new Vector2(x, y1) }, T));

        // 撇 / 捺 / 斜线：两点即可，方向天然不同
        System.Action<List<Poly>, float, float, float, float> D =
            (l, x0, y0, x1, y1) => l.Add(new Poly(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, T));

        // 细斜线（点、短撇）
        System.Action<List<Poly>, float, float, float, float> DS =
            (l, x0, y0, x1, y1) => l.Add(new Poly(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, TT));

        // 折：三点折线，拐角会自动接上
        System.Action<List<Poly>, float, float, float, float, float, float> Z =
            (l, x0, y0, x1, y1, x2, y2) => l.Add(new Poly(new[]
            {
                new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x2, y2)
            }, T));

        System.Action<string> Save = key => { d[key] = L.ToArray(); L = new List<Poly>(); };

        // ============================================================
        // 一画
        // ============================================================
        H(L, 0.12f, 0.88f, 0.50f); Save("一");
        V(L, 0.50f, 0.12f, 0.88f); Save("丨");
        DS(L, 0.62f, 0.78f, 0.40f, 0.22f); Save("丿");
        DS(L, 0.38f, 0.78f, 0.60f, 0.22f); Save("乀");
        DS(L, 0.49f, 0.62f, 0.51f, 0.46f); Save("丶");

        // ============================================================
        // 二画
        // ============================================================
        // 二：上短下长
        H(L, 0.24f, 0.76f, 0.70f);
        H(L, 0.12f, 0.88f, 0.30f); Save("二");

        // 十
        H(L, 0.12f, 0.88f, 0.50f);
        V(L, 0.50f, 0.12f, 0.88f); Save("十");

        // 人：撇捺从同一点散开
        D(L, 0.50f, 0.82f, 0.24f, 0.16f);
        D(L, 0.50f, 0.82f, 0.76f, 0.16f); Save("人");

        // 八
        DS(L, 0.46f, 0.78f, 0.26f, 0.18f);
        DS(L, 0.54f, 0.78f, 0.74f, 0.18f); Save("八");

        // 丁
        H(L, 0.12f, 0.88f, 0.80f);
        V(L, 0.50f, 0.12f, 0.80f); Save("丁");

        // 了：横折 + 竖钩
        Z(L, 0.24f, 0.84f, 0.74f, 0.84f, 0.44f, 0.16f); Save("了");

        // 刀
        Z(L, 0.26f, 0.84f, 0.74f, 0.84f, 0.44f, 0.16f);
        DS(L, 0.44f, 0.50f, 0.26f, 0.30f); Save("刀");

        // ============================================================
        // 三画
        // ============================================================
        // 三：上中短、下最长（正确比例）
        H(L, 0.26f, 0.74f, 0.78f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.12f, 0.88f, 0.22f); Save("三");

        // 大
        H(L, 0.14f, 0.86f, 0.64f);
        D(L, 0.50f, 0.64f, 0.26f, 0.14f);
        D(L, 0.50f, 0.64f, 0.74f, 0.14f); Save("大");

        // 工：上横短下横长
        H(L, 0.28f, 0.72f, 0.78f);
        H(L, 0.10f, 0.90f, 0.24f);
        V(L, 0.50f, 0.24f, 0.78f); Save("工");

        // 干：上横短、竖出头
        H(L, 0.30f, 0.70f, 0.72f);
        H(L, 0.10f, 0.90f, 0.34f);
        V(L, 0.50f, 0.34f, 0.90f); Save("干");

        // 士：上横长下横短
        H(L, 0.10f, 0.90f, 0.74f);
        H(L, 0.28f, 0.72f, 0.24f);
        V(L, 0.50f, 0.24f, 0.84f); Save("士");

        // 土：上横短下横最长
        H(L, 0.22f, 0.78f, 0.72f);
        H(L, 0.10f, 0.90f, 0.20f);
        V(L, 0.50f, 0.20f, 0.90f); Save("土");

        // 口：左竖 + 横折 + 底横（真正的方框）
        V(L, 0.28f, 0.24f, 0.76f);
        Z(L, 0.28f, 0.76f, 0.72f, 0.76f, 0.72f, 0.24f);
        H(L, 0.28f, 0.72f, 0.24f); Save("口");

        // 山
        V(L, 0.50f, 0.16f, 0.82f);
        Z(L, 0.24f, 0.64f, 0.24f, 0.20f, 0.76f, 0.20f);
        V(L, 0.76f, 0.20f, 0.64f); Save("山");

        // 川
        DS(L, 0.24f, 0.80f, 0.20f, 0.20f);
        V(L, 0.50f, 0.18f, 0.82f);
        V(L, 0.80f, 0.18f, 0.82f); Save("川");

        // 小
        V(L, 0.50f, 0.36f, 0.86f);
        DS(L, 0.32f, 0.58f, 0.16f, 0.18f);
        DS(L, 0.68f, 0.58f, 0.84f, 0.18f); Save("小");

        // 上
        V(L, 0.44f, 0.18f, 0.84f);
        H(L, 0.44f, 0.80f, 0.56f);
        H(L, 0.14f, 0.86f, 0.18f); Save("上");

        // 下
        H(L, 0.14f, 0.86f, 0.84f);
        V(L, 0.50f, 0.16f, 0.84f);
        DS(L, 0.60f, 0.68f, 0.66f, 0.54f); Save("下");

        // ============================================================
        // 四画
        // ============================================================
        // 王：三横一竖，上中短、下最长
        H(L, 0.24f, 0.76f, 0.84f);
        H(L, 0.30f, 0.70f, 0.50f);
        H(L, 0.12f, 0.88f, 0.16f);
        V(L, 0.50f, 0.16f, 0.84f); Save("王");

        // 丰：三横一竖，竖上下出头
        H(L, 0.16f, 0.84f, 0.74f);
        H(L, 0.16f, 0.84f, 0.50f);
        H(L, 0.16f, 0.84f, 0.26f);
        V(L, 0.50f, 0.08f, 0.92f); Save("丰");

        // 木
        H(L, 0.14f, 0.86f, 0.66f);
        V(L, 0.50f, 0.12f, 0.90f);
        D(L, 0.50f, 0.60f, 0.22f, 0.18f);
        D(L, 0.50f, 0.60f, 0.78f, 0.18f); Save("木");

        // 天：两横 + 撇捺
        H(L, 0.28f, 0.72f, 0.86f);
        H(L, 0.14f, 0.86f, 0.64f);
        D(L, 0.50f, 0.64f, 0.24f, 0.12f);
        D(L, 0.50f, 0.64f, 0.76f, 0.12f); Save("天");

        // 中：口 + 竖贯穿
        V(L, 0.32f, 0.34f, 0.72f);
        Z(L, 0.32f, 0.72f, 0.68f, 0.72f, 0.68f, 0.34f);
        H(L, 0.32f, 0.68f, 0.34f);
        V(L, 0.50f, 0.08f, 0.92f); Save("中");

        // 日：左竖 + 横折 + 中横 + 底横
        V(L, 0.30f, 0.16f, 0.86f);
        Z(L, 0.30f, 0.86f, 0.70f, 0.86f, 0.70f, 0.16f);
        H(L, 0.30f, 0.70f, 0.52f);
        H(L, 0.30f, 0.70f, 0.16f); Save("日");

        // 水：竖 + 两侧撇捺 + 左侧短撇
        V(L, 0.50f, 0.24f, 0.88f);
        D(L, 0.50f, 0.54f, 0.22f, 0.14f);
        D(L, 0.50f, 0.54f, 0.78f, 0.14f);
        DS(L, 0.44f, 0.34f, 0.20f, 0.22f); Save("水");

        // 火：两点 + 撇捺
        DS(L, 0.22f, 0.68f, 0.26f, 0.54f);
        DS(L, 0.78f, 0.68f, 0.74f, 0.54f);
        D(L, 0.48f, 0.62f, 0.24f, 0.14f);
        D(L, 0.52f, 0.62f, 0.76f, 0.14f); Save("火");

        // 少：小 + 长撇
        V(L, 0.46f, 0.44f, 0.86f);
        DS(L, 0.32f, 0.62f, 0.22f, 0.52f);
        D(L, 0.46f, 0.54f, 0.72f, 0.22f);
        D(L, 0.46f, 0.44f, 0.24f, 0.12f); Save("少");

        // 太：大 + 一点
        H(L, 0.14f, 0.86f, 0.64f);
        D(L, 0.50f, 0.64f, 0.24f, 0.14f);
        D(L, 0.50f, 0.64f, 0.76f, 0.14f);
        DS(L, 0.56f, 0.30f, 0.62f, 0.18f); Save("太");

        // 兀：一 + 撇 + 竖折
        H(L, 0.12f, 0.88f, 0.84f);
        D(L, 0.36f, 0.84f, 0.22f, 0.22f);
        Z(L, 0.66f, 0.84f, 0.66f, 0.22f, 0.88f, 0.22f); Save("兀");

        // 玉：王 + 一点
        H(L, 0.24f, 0.76f, 0.86f);
        H(L, 0.30f, 0.70f, 0.54f);
        H(L, 0.12f, 0.88f, 0.16f);
        V(L, 0.50f, 0.16f, 0.86f);
        DS(L, 0.62f, 0.38f, 0.70f, 0.28f); Save("玉");

        // 主：丶 + 王
        DS(L, 0.50f, 0.98f, 0.50f, 0.88f);
        H(L, 0.22f, 0.78f, 0.80f);
        H(L, 0.28f, 0.72f, 0.50f);
        H(L, 0.12f, 0.88f, 0.16f);
        V(L, 0.50f, 0.16f, 0.80f); Save("主");

        // ============================================================
        // 五画
        // ============================================================
        // 五：上横 + 竖折 + 横折 + 底横（真正像"五"）
        H(L, 0.16f, 0.84f, 0.88f);
        Z(L, 0.34f, 0.88f, 0.34f, 0.52f, 0.74f, 0.52f);
        H(L, 0.30f, 0.74f, 0.52f);
        Z(L, 0.30f, 0.52f, 0.30f, 0.18f, 0.80f, 0.18f);
        H(L, 0.12f, 0.86f, 0.14f); Save("五");

        // 正：上横 + 中竖 + 左竖 + 中横 + 底横
        H(L, 0.16f, 0.84f, 0.86f);
        V(L, 0.50f, 0.54f, 0.86f);
        V(L, 0.28f, 0.28f, 0.70f);
        H(L, 0.28f, 0.84f, 0.62f);
        H(L, 0.14f, 0.86f, 0.16f); Save("正");

        // 白：撇 + 左竖 + 横折 + 中横 + 底横
        DS(L, 0.46f, 0.94f, 0.30f, 0.84f);
        V(L, 0.32f, 0.30f, 0.84f);
        Z(L, 0.32f, 0.84f, 0.68f, 0.84f, 0.68f, 0.30f);
        H(L, 0.32f, 0.68f, 0.58f);
        H(L, 0.32f, 0.68f, 0.30f); Save("白");

        // 田：口 + 中竖 + 中横
        V(L, 0.26f, 0.20f, 0.80f);
        Z(L, 0.26f, 0.80f, 0.74f, 0.80f, 0.74f, 0.20f);
        H(L, 0.26f, 0.74f, 0.20f);
        V(L, 0.50f, 0.20f, 0.80f);
        H(L, 0.26f, 0.74f, 0.50f); Save("田");

        // 目：日 + 一横
        V(L, 0.30f, 0.16f, 0.86f);
        Z(L, 0.30f, 0.86f, 0.70f, 0.86f, 0.70f, 0.16f);
        H(L, 0.30f, 0.70f, 0.62f);
        H(L, 0.30f, 0.70f, 0.39f);
        H(L, 0.30f, 0.70f, 0.16f); Save("目");

        // ============================================================
        // 六画
        // ============================================================
        // 圭：两个土
        H(L, 0.26f, 0.74f, 0.90f);
        H(L, 0.16f, 0.84f, 0.70f);
        V(L, 0.50f, 0.66f, 0.96f);
        H(L, 0.26f, 0.74f, 0.44f);
        H(L, 0.16f, 0.84f, 0.14f);
        V(L, 0.50f, 0.10f, 0.48f); Save("圭");

        // 由：田 的竖向上出头
        V(L, 0.26f, 0.16f, 0.70f);
        Z(L, 0.26f, 0.70f, 0.74f, 0.70f, 0.74f, 0.16f);
        H(L, 0.26f, 0.74f, 0.16f);
        V(L, 0.50f, 0.16f, 0.94f);
        H(L, 0.26f, 0.74f, 0.44f); Save("由");

        // ============================================================
        // 七画：兵（皇 召唤的士兵）
        // ============================================================
        DS(L, 0.48f, 0.98f, 0.34f, 0.86f);
        V(L, 0.36f, 0.62f, 0.90f);
        H(L, 0.14f, 0.86f, 0.68f);
        V(L, 0.30f, 0.44f, 0.68f);
        H(L, 0.14f, 0.86f, 0.44f);
        DS(L, 0.46f, 0.36f, 0.28f, 0.12f);
        DS(L, 0.54f, 0.36f, 0.72f, 0.12f); Save("兵");

        // ============================================================
        // 八画：里
        // ============================================================
        V(L, 0.28f, 0.60f, 0.94f);
        Z(L, 0.28f, 0.94f, 0.72f, 0.94f, 0.72f, 0.60f);
        H(L, 0.28f, 0.72f, 0.60f);
        V(L, 0.50f, 0.60f, 0.94f);
        H(L, 0.28f, 0.72f, 0.78f);
        H(L, 0.22f, 0.78f, 0.46f);
        H(L, 0.12f, 0.88f, 0.14f);
        V(L, 0.50f, 0.14f, 0.46f); Save("里");

        // ============================================================
        // 九画：皇（第三层 BOSS）
        //   白（撇+竖+横折+中横+底横） + 王（横+横+竖+横）
        // ============================================================
        DS(L, 0.44f, 1.00f, 0.30f, 0.88f);      // 撇
        V(L, 0.32f, 0.56f, 0.88f);              // 左竖
        Z(L, 0.32f, 0.88f, 0.68f, 0.88f, 0.68f, 0.56f);  // 横折
        H(L, 0.32f, 0.68f, 0.72f);              // 白 中横
        H(L, 0.32f, 0.68f, 0.56f);              // 白 底横
        H(L, 0.22f, 0.78f, 0.44f);              // 王 上横
        H(L, 0.28f, 0.72f, 0.28f);              // 王 中横
        V(L, 0.50f, 0.10f, 0.52f);              // 王 竖（上端接住"白"的底横）
        H(L, 0.12f, 0.88f, 0.10f); Save("皇");  // 王 底横

        return d;
    }

    public static IEnumerable<string> AllGlyphs
    {
        get
        {
            if (defs == null) defs = BuildDefinitions();
            return defs.Keys;
        }
    }
}
