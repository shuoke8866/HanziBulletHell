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

    /// <summary>
    /// 把某个字形渲染成贴图（给编辑器预览工具用）。
    ///
    /// 之所以要暴露这个接口：字形是手写坐标，改完以后光看代码很难判断
    /// 笔画到底对齐没有。有了它就能把所有字渲染成一张对比图实际看一遍。
    /// 返回 null 表示字形库里没有这个字。
    /// </summary>
    public static Texture2D RenderGlyph(string ch, int size)
    {
        if (defs == null) defs = BuildDefinitions();

        Poly[] polys;
        if (!defs.TryGetValue(ch, out polys)) return null;

        return Rasterize(polys, size);
    }

    /// <summary>字形库里所有字（按笔画数、再按 Unicode 排序，方便预览时对齐）</summary>
    public static List<string> AllGlyphsSorted()
    {
        if (defs == null) defs = BuildDefinitions();

        var list = new List<string>(defs.Keys);
        list.Sort((a, b) =>
        {
            int ca = defs[a].Length, cb = defs[b].Length;
            if (ca != cb) return ca.CompareTo(cb);
            return string.CompareOrdinal(a, b);
        });
        return list;
    }

    /// <summary>某个字形的笔画段数（用于校验，比如确认没有多画/漏画）</summary>
    public static int SegmentCount(string ch)
    {
        if (defs == null) defs = BuildDefinitions();

        Poly[] polys;
        if (!defs.TryGetValue(ch, out polys)) return -1;

        int n = 0;
        foreach (Poly p in polys) n += BuildOutline(p).Length;
        return n;
    }

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

        // 等宽、拐角处不做外扩。
        //
        // 演进过程：
        //   1. 最初是"前半段满宽→半宽、后半段半宽→满宽"，想让拐角接上，
        //      结果整条折线在拐角处掐细到一半 —— 口/日/田/白 的方框
        //      看起来"腰部细一圈"，这是"字形不规整"的主要来源。
        //   2. 改成等宽 + 端点外扩半个笔宽填缝，缝隙没了，
        //      但笔画外侧会顶出一个小凸起（方框角上多个"钩"）。
        //   3. 现在是等宽 + 不外扩：光栅化用的是**圆头端点**，
        //      两段在拐角处本就重叠出一个圆角方块，直角已经接得住，
        //      不需要额外外扩，也就没有凸起。
        segs.Add(new Seg
        {
            x0 = v[0].x, y0 = v[0].y,
            x1 = v[corner].x, y1 = v[corner].y,
            t0 = p.t, t1 = p.t
        });
        segs.Add(new Seg
        {
            x0 = v[corner].x, y0 = v[corner].y,
            x1 = v[v.Length - 1].x, y1 = v[v.Length - 1].y,
            t0 = p.t, t1 = p.t
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

        // 笔画粗细：按标准汉字字面约 1/11 ~ 1/12 取。
        // 原来的 0.105 偏粗，笔画之间几乎糊在一起，缩小到 32px 显示时更明显。
        const float T = 0.085f;   // 标准笔画粗
        const float TT = 0.062f;  // 细一点（点、短撇）

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
        // 字形坐标规范
        //
        // 所有笔画都吸附到下面这套参考网格上，保证不同字之间、同一个字内部
        // 都能对齐。之前 47 个字都是各自手写坐标，没有共同基准，
        // 于是出现"工的两横挤成一个框""士的底横太靠上""皇的竖穿过白"这类问题。
        //
        //   竖向参考线（字宽）：0.10  0.30  0.50  0.70  0.90
        //   横向参考线（字高）：0.10  0.30  0.50  0.70  0.90
        //
        // 约定：
        //   - 汉字主体永远落在 0.10 ~ 0.90 之间，居中于 0.50
        //   - 单笔的撇捺可以出到 0.16 ~ 0.84 增加舒展感
        //   - 上下/左右结构按参考线切分：上部 0.50~0.90、下部 0.10~0.50 等
        //   - 横画长度约定：长横 0.10~0.90，次长 0.16~0.84，中横 0.26~0.74，短横 0.34~0.66
        // ============================================================

        // ============================================================
        // 一画
        // ============================================================
        H(L, 0.10f, 0.90f, 0.50f); Save("一");
        V(L, 0.50f, 0.10f, 0.90f); Save("丨");
        // 撇：从右上到左下
        D(L, 0.62f, 0.84f, 0.34f, 0.16f); Save("丿");
        // 捺：从左上到右下
        D(L, 0.38f, 0.84f, 0.66f, 0.16f); Save("乀");
        // 点：短促的斜点，别太细
        DS(L, 0.46f, 0.58f, 0.54f, 0.42f); Save("丶");

        // ============================================================
        // 二画
        // ============================================================
        // 二：上短下长
        H(L, 0.34f, 0.66f, 0.76f);
        H(L, 0.10f, 0.90f, 0.24f); Save("二");

        // 十：横竖都在中线上，竖上下出头
        H(L, 0.10f, 0.90f, 0.50f);
        V(L, 0.50f, 0.10f, 0.90f); Save("十");

        // 人：撇捺从顶点分开
        D(L, 0.50f, 0.88f, 0.18f, 0.12f);
        D(L, 0.50f, 0.88f, 0.82f, 0.12f); Save("人");

        // 八：两笔分开、不相连
        D(L, 0.42f, 0.80f, 0.24f, 0.16f);
        D(L, 0.58f, 0.80f, 0.76f, 0.16f); Save("八");

        // 丁：横在上，竖钩从横中间垂下
        H(L, 0.10f, 0.90f, 0.82f);
        V(L, 0.50f, 0.18f, 0.82f); Save("丁");

        // 了：横折折向左下（第二笔是弯钩，用折线近似）
        Z(L, 0.24f, 0.86f, 0.76f, 0.86f, 0.44f, 0.12f); Save("了");

        // 刀：横折 + 撇
        Z(L, 0.24f, 0.86f, 0.76f, 0.86f, 0.46f, 0.12f);
        D(L, 0.62f, 0.62f, 0.20f, 0.20f); Save("刀");

        // ============================================================
        // 三画
        // ============================================================
        // 三：上中短、下最长
        H(L, 0.26f, 0.74f, 0.78f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.10f, 0.90f, 0.22f); Save("三");

        // 大：横在上部，撇捺从横中点散开
        H(L, 0.16f, 0.84f, 0.66f);
        D(L, 0.50f, 0.66f, 0.20f, 0.12f);
        D(L, 0.50f, 0.66f, 0.80f, 0.12f); Save("大");

        // 工：上横短、下横长，竖居中连接两横
        H(L, 0.26f, 0.74f, 0.74f);
        H(L, 0.10f, 0.90f, 0.26f);
        V(L, 0.50f, 0.26f, 0.74f); Save("工");

        // 干：上横短、竖向下出头
        H(L, 0.26f, 0.74f, 0.72f);
        H(L, 0.10f, 0.90f, 0.40f);
        V(L, 0.50f, 0.14f, 0.86f); Save("干");

        // 士：上横长、下横短（与"土"相反）
        H(L, 0.10f, 0.90f, 0.74f);
        H(L, 0.34f, 0.66f, 0.26f);
        V(L, 0.50f, 0.26f, 0.86f); Save("士");

        // 土：上横短、下横长
        H(L, 0.26f, 0.74f, 0.72f);
        H(L, 0.10f, 0.90f, 0.24f);
        V(L, 0.50f, 0.14f, 0.86f); Save("土");

        // 口：左右竖 + 上下横，方框
        V(L, 0.28f, 0.28f, 0.76f);
        Z(L, 0.28f, 0.76f, 0.72f, 0.76f, 0.72f, 0.28f);
        H(L, 0.28f, 0.72f, 0.28f); Save("口");

        // 山：中竖最高，两侧竖短，底部横托住
        V(L, 0.50f, 0.22f, 0.86f);
        Z(L, 0.22f, 0.62f, 0.22f, 0.22f, 0.78f, 0.22f);
        V(L, 0.78f, 0.22f, 0.62f); Save("山");

        // 川：三竖，左为撇、中右为竖
        D(L, 0.22f, 0.82f, 0.20f, 0.16f);
        V(L, 0.50f, 0.16f, 0.84f);
        V(L, 0.78f, 0.16f, 0.84f); Save("川");

        // 小：中竖钩 + 左右两点
        V(L, 0.50f, 0.30f, 0.86f);
        DS(L, 0.30f, 0.60f, 0.16f, 0.18f);
        DS(L, 0.70f, 0.60f, 0.84f, 0.18f); Save("小");

        // 上：竖在下、短横在竖中间、长横在底部
        //     （短横要在长横之上，整体是"下宽上尖"）
        V(L, 0.50f, 0.20f, 0.86f);
        H(L, 0.50f, 0.74f, 0.52f);
        H(L, 0.14f, 0.86f, 0.20f); Save("上");

        // 下：长横在上、竖从中点垂下、点在竖右侧
        H(L, 0.10f, 0.90f, 0.80f);
        V(L, 0.50f, 0.16f, 0.80f);
        DS(L, 0.58f, 0.62f, 0.64f, 0.46f); Save("下");

        // ============================================================
        // 四画
        // ============================================================
        // 王：三横一竖，上中短、下最长，竖贯通三横
        H(L, 0.28f, 0.72f, 0.80f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.10f, 0.90f, 0.20f);
        V(L, 0.50f, 0.20f, 0.80f); Save("王");

        // 丰：三横等长、竖上下都出头
        H(L, 0.16f, 0.84f, 0.78f);
        H(L, 0.16f, 0.84f, 0.50f);
        H(L, 0.16f, 0.84f, 0.22f);
        V(L, 0.50f, 0.10f, 0.90f); Save("丰");

        // 木：横 + 竖 + 撇捺从交叉点散开
        H(L, 0.16f, 0.84f, 0.62f);
        V(L, 0.50f, 0.12f, 0.90f);
        D(L, 0.50f, 0.62f, 0.20f, 0.14f);
        D(L, 0.50f, 0.62f, 0.80f, 0.14f); Save("木");

        // 天：两横在上，撇捺从下横散开
        H(L, 0.30f, 0.70f, 0.86f);
        H(L, 0.14f, 0.86f, 0.64f);
        D(L, 0.50f, 0.64f, 0.20f, 0.12f);
        D(L, 0.50f, 0.64f, 0.80f, 0.12f); Save("天");

        // 太：大 + 一点（点在右下）
        H(L, 0.16f, 0.84f, 0.68f);
        D(L, 0.50f, 0.68f, 0.20f, 0.14f);
        D(L, 0.50f, 0.68f, 0.80f, 0.14f);
        DS(L, 0.70f, 0.40f, 0.78f, 0.24f); Save("太");

        // 兀：一 + 撇 + 竖折（下方是撇和竖折两条腿）
        H(L, 0.10f, 0.90f, 0.82f);
        D(L, 0.36f, 0.82f, 0.20f, 0.20f);
        Z(L, 0.66f, 0.82f, 0.66f, 0.20f, 0.88f, 0.20f); Save("兀");

        // 日：窄长方框，中间一横
        V(L, 0.32f, 0.14f, 0.86f);
        Z(L, 0.32f, 0.86f, 0.68f, 0.86f, 0.68f, 0.14f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.32f, 0.68f, 0.14f); Save("日");

        // 中：口 + 竖贯穿（竖上下都出头）
        V(L, 0.32f, 0.34f, 0.76f);
        Z(L, 0.32f, 0.76f, 0.68f, 0.76f, 0.68f, 0.34f);
        H(L, 0.32f, 0.68f, 0.34f);
        V(L, 0.50f, 0.10f, 0.90f); Save("中");

        // 水：中竖钩 + 左右撇捺
        V(L, 0.50f, 0.22f, 0.88f);
        D(L, 0.50f, 0.56f, 0.20f, 0.12f);
        D(L, 0.50f, 0.56f, 0.80f, 0.12f);
        DS(L, 0.42f, 0.36f, 0.18f, 0.22f); Save("水");

        // 少：小 + 长撇（长撇从中间穿到左下）
        V(L, 0.46f, 0.42f, 0.88f);
        DS(L, 0.30f, 0.62f, 0.18f, 0.50f);
        D(L, 0.46f, 0.52f, 0.76f, 0.20f);
        D(L, 0.46f, 0.42f, 0.22f, 0.10f); Save("少");

        // 火：左右两点 + 撇捺
        DS(L, 0.24f, 0.74f, 0.30f, 0.58f);
        DS(L, 0.76f, 0.74f, 0.70f, 0.58f);
        D(L, 0.50f, 0.64f, 0.22f, 0.12f);
        D(L, 0.50f, 0.64f, 0.78f, 0.12f); Save("火");

        // ============================================================
        // 五画
        // ============================================================
        // 主：点 + 王
        DS(L, 0.50f, 0.94f, 0.50f, 0.84f);
        H(L, 0.26f, 0.74f, 0.74f);
        H(L, 0.32f, 0.68f, 0.48f);
        H(L, 0.10f, 0.90f, 0.18f);
        V(L, 0.50f, 0.18f, 0.74f); Save("主");

        // 玉：王 + 一点（点在右下）
        H(L, 0.28f, 0.72f, 0.80f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.10f, 0.90f, 0.20f);
        V(L, 0.50f, 0.20f, 0.80f);
        DS(L, 0.66f, 0.38f, 0.74f, 0.24f); Save("玉");

        // 五：上横 + 竖折 + 横折 + 底横
        H(L, 0.26f, 0.74f, 0.86f);
        Z(L, 0.32f, 0.86f, 0.32f, 0.52f, 0.74f, 0.52f);
        H(L, 0.32f, 0.74f, 0.52f);
        Z(L, 0.30f, 0.52f, 0.30f, 0.16f, 0.78f, 0.16f);
        H(L, 0.10f, 0.90f, 0.14f); Save("五");

        // 正：上横最短、下横最长，中间是"止"
        H(L, 0.30f, 0.70f, 0.86f);
        V(L, 0.50f, 0.52f, 0.86f);
        V(L, 0.30f, 0.26f, 0.68f);
        H(L, 0.30f, 0.82f, 0.68f);
        H(L, 0.10f, 0.90f, 0.18f); Save("正");

        // 白：撇 + 日（比日略宽一点）
        DS(L, 0.50f, 0.94f, 0.34f, 0.84f);
        V(L, 0.32f, 0.16f, 0.84f);
        Z(L, 0.32f, 0.84f, 0.68f, 0.84f, 0.68f, 0.16f);
        H(L, 0.32f, 0.68f, 0.52f);
        H(L, 0.32f, 0.68f, 0.16f); Save("白");

        // 田：口 + 中竖 + 中横
        V(L, 0.26f, 0.18f, 0.82f);
        Z(L, 0.26f, 0.82f, 0.74f, 0.82f, 0.74f, 0.18f);
        H(L, 0.26f, 0.74f, 0.18f);
        V(L, 0.50f, 0.18f, 0.82f);
        H(L, 0.26f, 0.74f, 0.50f); Save("田");

        // 目：比日多一横，更高更窄
        V(L, 0.32f, 0.12f, 0.88f);
        Z(L, 0.32f, 0.88f, 0.68f, 0.88f, 0.68f, 0.12f);
        H(L, 0.32f, 0.68f, 0.63f);
        H(L, 0.32f, 0.68f, 0.38f);
        H(L, 0.32f, 0.68f, 0.12f); Save("目");

        // ============================================================
        // 六画
        // ============================================================
        // 圭：两个土，上土窄、下土宽，中间留出间隔
        H(L, 0.30f, 0.70f, 0.92f);
        H(L, 0.20f, 0.80f, 0.70f);
        V(L, 0.50f, 0.62f, 0.96f);
        H(L, 0.30f, 0.70f, 0.38f);
        H(L, 0.10f, 0.90f, 0.10f);
        V(L, 0.50f, 0.04f, 0.44f); Save("圭");

        // 由：田 的中竖向上出头
        V(L, 0.26f, 0.14f, 0.68f);
        Z(L, 0.26f, 0.68f, 0.74f, 0.68f, 0.74f, 0.14f);
        H(L, 0.26f, 0.74f, 0.14f);
        V(L, 0.50f, 0.14f, 0.90f);
        H(L, 0.26f, 0.74f, 0.42f); Save("由");

        // ============================================================
        // 七画：兵
        // ============================================================
        DS(L, 0.50f, 0.96f, 0.36f, 0.86f);
        V(L, 0.38f, 0.62f, 0.90f);
        H(L, 0.12f, 0.88f, 0.68f);
        V(L, 0.30f, 0.42f, 0.68f);
        H(L, 0.12f, 0.88f, 0.42f);
        DS(L, 0.46f, 0.34f, 0.26f, 0.10f);
        DS(L, 0.54f, 0.34f, 0.74f, 0.10f); Save("兵");

        // ============================================================
        // 八画：里
        //   上「田」下「土」。
        //   比例关系（这次按字理调，不再凭感觉）：
        //     - 「里」是**上宽下窄**的稳定结构：田要宽（占满字面），土略窄
        //     - 田 是主体，高度约占 55%：y 0.44~0.98
        //     - 土 托底，两横等宽、比田略窄：x 0.32~0.68
        //     - 部件间隙 0.10，平行笔画间隙 >= 0.12
        // ============================================================
        // 田（上部，主体，最宽）
        V(L, 0.24f, 0.44f, 0.92f);
        Z(L, 0.24f, 0.92f, 0.76f, 0.92f, 0.76f, 0.44f);
        H(L, 0.24f, 0.76f, 0.44f);
        V(L, 0.50f, 0.44f, 0.92f);
        H(L, 0.24f, 0.76f, 0.68f);
        // 土（下部，托底，略窄于田）
        H(L, 0.32f, 0.68f, 0.32f);
        H(L, 0.26f, 0.74f, 0.10f);
        V(L, 0.50f, 0.10f, 0.32f); Save("里");

        // ============================================================
        // 九画：皇
        //   上「白」下「王」。同样按字理：
        //     - 白 要瘦（x 0.32~0.68），王 要宽（x 0.12~0.88），靠宽度差分层
        //     - 白 内部三横间距 0.15，不糊
        //     - 王的竖只在下半部（0.10~0.40），**不穿**白的底横
        // ============================================================
        // 白（上部，瘦）
        DS(L, 0.50f, 0.99f, 0.34f, 0.90f);              // 撇
        V(L, 0.32f, 0.56f, 0.90f);                      // 左竖
        Z(L, 0.32f, 0.90f, 0.68f, 0.90f, 0.68f, 0.56f); // 横折
        H(L, 0.32f, 0.68f, 0.73f);                      // 白 中横
        H(L, 0.32f, 0.68f, 0.56f);                      // 白 底横
        // 王（下部，宽）
        H(L, 0.26f, 0.74f, 0.40f);                      // 王 上横
        H(L, 0.32f, 0.68f, 0.24f);                      // 王 中横
        V(L, 0.50f, 0.10f, 0.40f);                      // 王 竖（只在下半部）
        H(L, 0.12f, 0.88f, 0.10f); Save("皇");          // 王 底横

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
