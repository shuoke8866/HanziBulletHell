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
public partial class HanziRenderer : MonoBehaviour
{
    public string character = "一";
    public Color color = Color.white;
    public int textureSize = 64;
    public float pixelsPerUnit = 32f;

    /// <summary>
    /// 这个渲染器用哪套字库。
    ///
    /// 玩家身上的 HanziRenderer 留空（= 玩家字库）；
    /// 敌人身上那个由 Enemy 设成 <see cref="SetEnemy"/>，
    /// 这样"改敌人的字"和"改玩家的字"互不影响。
    /// </summary>
    public string glyphSet = SetPlayer;

    // ---------------- 数据 ----------------

    /// <summary>
    /// 折线笔画，**支持任意点数**（2 点以上）。
    ///
    /// 早期只支持 2~3 个点（直线 / 一个折角），做不了"竖+横+竖"这种
    /// 多次拐折的笔画。现在点数不限，拖动编辑器可以随时在中间插点。
    /// </summary>
    struct Poly
    {
        public List<Vector2> pts;
        public float t;

        public Poly(Vector2[] p, float th)
        {
            pts = new List<Vector2>(p);
            t = th;
        }

        public Poly(List<Vector2> p, float th)
        {
            pts = p;
            t = th;
        }
    }

    /// <summary>最终用于光栅化的带宽度线段，两端可以不同粗。</summary>
    struct Seg
    {
        public float x0, y0, x1, y1, t0, t1;
    }

    /// <summary>
    /// 字库 —— **按"库名"分成多套**。
    ///
    /// 为什么不是单个 Dictionary：
    /// 玩家和敌人**必须能独立编辑**。原来两边共用同一份字形数据，
    /// 于是"改敌人的「木」"会连带"改掉玩家的「木」" ——
    /// 这是共享可变状态（shared mutable state），改一处影响两处。
    ///
    /// 现在：
    ///   HanziRenderer.SetPlayer  玩家的字库（背包、快捷栏、字形编辑器默认）
    ///   HanziRenderer.SetEnemy   敌人的字库（敌人定义器用）
    ///
    /// 两套可以用**同名**的字（都叫"木"），但内容完全独立。
    /// </summary>
    public const string SetPlayer = "player";
    public const string SetEnemy = "enemy";

    static readonly Dictionary<string, Dictionary<string, Poly[]>> sets =
        new Dictionary<string, Dictionary<string, Poly[]>>();

    /// <summary>BuildDefinitions 正在构建哪个库（Save 靠它决定写进哪套）</summary>
    static string buildingSet = SetPlayer;

    /// <summary>取某个库（没有就构建）。传空 = 玩家库。</summary>
    static Dictionary<string, Poly[]> SetOf(string setName)
    {
        if (string.IsNullOrEmpty(setName)) setName = SetPlayer;

        Dictionary<string, Poly[]> d;
        if (sets.TryGetValue(setName, out d) && d != null) return d;

        d = BuildDefinitions(setName);
        sets[setName] = d;
        return d;
    }

    /// <summary>这个渲染器实例用哪套字库（空 = 玩家库）</summary>
    Dictionary<string, Poly[]> MySet
    {
        get { return SetOf(string.IsNullOrEmpty(glyphSet) ? SetPlayer : glyphSet); }
    }

    /// <summary>玩家字库（兼容旧代码：原来所有静态方法都直接用这个）</summary>
    static Dictionary<string, Poly[]> defs
    {
        get { return SetOf(SetPlayer); }
        set
        {
            // 旧代码里 `defs = BuildDefinitions()` 表示"重新构建玩家库"，
            // `defs = null` 表示"丢掉缓存，下次用到时重建"。
            // 两种语义都保持住，免得改一堆调用点。
            sets[SetPlayer] = value ?? BuildDefinitions(SetPlayer);
        }
    }


    SpriteRenderer spriteRenderer;
    Sprite currentSprite;

    /// <summary>当前字形的破损风格（敌人用）。默认全 0 = 完整字形。</summary>
    GlyphStyle style = GlyphStyle.None;

    // ---------------- 生命周期 ----------------

    /// <summary>
    /// 保证 <see cref="spriteRenderer"/> 已经拿到（没有就补一个）。
    ///
    /// 原来只在 Awake() 里初始化，于是**在 Awake 之前调用任何公开方法都会崩**
    /// （MissingComponentException: no SpriteRenderer on "Glyph"）。
    /// Enemy.SetGlyph 就是踩这个 —— 它在 Instantiate 之后立刻被调用，
    /// 而 Unity 那时还没跑 Awake。
    ///
    /// 改成按需初始化：入口方法各自先调一次，顺序就无所谓了。
    /// </summary>
    void EnsureRenderer()
    {
        if (spriteRenderer != null) return;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        if (defs == null) defs = BuildDefinitions();
    }

    void Awake()
    {
        EnsureRenderer();

        if (!spriteRenderer.enabled) spriteRenderer.enabled = true;

        SetCharacter(character);
    }

    /// <summary>
    /// 给字库里没有的字补一个**占位字形**。
    ///
    /// SetCharacter 遇到缺字时的行为是「打警告 + **保持当前显示**」，
    /// 表现是 BOSS 该显示「官」却顶着上一个字「一」—— 玩家看不出是缺字，
    /// 只会觉得「字错了」。
    ///
    /// 补一个占位形状至少能看出「这个字还没画」，而不是误以为显示了别的字。
    /// 玩家在字形编辑器里画好之后，RebuildAll 会把占位覆盖掉。
    /// </summary>
    public static void EnsurePlaceholder(string ch)
    {
        if (string.IsNullOrEmpty(ch)) return;

        var set = SetOf(SetPlayer);
        if (set.ContainsKey(ch)) return;

        // 占位形状：一个方框 + 中间一横（像「缺字」的记号）
        set[ch] = new Poly[]
        {
            new Poly(new Vector2[] { new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.26f) }, 0.085f),
            new Poly(new Vector2[] { new Vector2(0.74f, 0.26f), new Vector2(0.74f, 0.74f) }, 0.085f),
            new Poly(new Vector2[] { new Vector2(0.74f, 0.74f), new Vector2(0.26f, 0.74f) }, 0.085f),
            new Poly(new Vector2[] { new Vector2(0.26f, 0.74f), new Vector2(0.26f, 0.26f) }, 0.085f),
            new Poly(new Vector2[] { new Vector2(0.36f, 0.50f), new Vector2(0.64f, 0.50f) }, 0.075f),
        };

        // 贴图缓存是按「字库|字|尺寸|ppu|风格」做键的，加了新字要清一下
        ClearAllCaches();
    }

    public static bool IsAllowed(string ch)
    {
        return IsAllowed(ch, SetPlayer);
    }

    public static bool IsAllowed(string ch, string setName)
    {
        if (string.IsNullOrEmpty(ch)) return false;
        return SetOf(setName).ContainsKey(ch);
    }

    public void SetStrokeColor(Color newColor)
    {
        color = newColor;
        EnsureRenderer();
        spriteRenderer.color = newColor;
    }

    public void SetCharacter(string ch)
    {
        SetCharacter(ch, GlyphStyle.None);
    }

    /// <summary>
    /// 换字并指定破损风格。
    ///
    /// 风格变了必须重建贴图（残缺是画进去的，不是运行时滤镜），
    /// 所以缓存键里带上风格参数。
    /// </summary>
    public void SetCharacter(string ch, GlyphStyle newStyle)
    {
        EnsureRenderer();

        var mine = MySet;
        if (string.IsNullOrEmpty(ch) || !mine.ContainsKey(ch))
        {
            Debug.LogWarning($"HanziRenderer: 字库「{glyphSet}」里没有「{ch}」，保持当前显示。");
            return;
        }

        character = ch;
        style = newStyle;
        BuildSprite(ch, mine[ch]);
        spriteRenderer.color = color;

        if (!spriteRenderer.enabled) spriteRenderer.enabled = true;
    }

    public static Sprite CreateSpriteFor(string ch, int size = 64, float ppu = 32f)
    {
        return CreateSpriteFor(ch, size, ppu, default(GlyphStyle));
    }

    /// <summary>带破损风格的版本</summary>
    public static Sprite CreateSpriteFor(string ch, int size, float ppu, GlyphStyle style)
    {
        return CreateSpriteFor(ch, size, ppu, style, SetPlayer);
    }

    /// <summary>
    /// 指定字库的版本。
    /// 敌人定义器的预览要用 SetEnemy，否则"预览里看到的"和"游戏里看到的"不是一回事。
    /// </summary>
    public static Sprite CreateSpriteFor(string ch, int size, float ppu, GlyphStyle style,
                                         string setName)
    {
        var one = SetOf(setName);
        if (string.IsNullOrEmpty(ch)) return null;

        // 缓存：同一个字只光栅化一次，之后直接复用 Sprite。
        // 拾取笔画会频繁重建字形，不做缓存会每帧 new Texture2D + SetPixels + Apply，
        // 在主线程上造成明显卡顿。
        //
        // 键里必须带字库名，否则两套字库同名的字会互相顶掉。
        string key = setName + "|" + ch + "|" + size + "|" + ppu
                   + (style.IsPlain ? "" : "|" + style.Key);
        Sprite cached;
        if (spriteCache.TryGetValue(key, out cached) && cached != null)
            return cached;

        Poly[] polys;
        if (!one.TryGetValue(ch, out polys)) return null;

        Texture2D tex = Rasterize(polys, size, style);
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
        return RenderGlyph(ch, size, default(GlyphStyle));
    }

    /// <summary>带破损风格的版本（敌人编辑器的预览要用，才看得到残缺效果）</summary>
    public static Texture2D RenderGlyph(string ch, int size, GlyphStyle style)
    {
        return RenderGlyph(ch, size, style, SetPlayer);
    }

    /// <summary>指定字库的版本</summary>
    public static Texture2D RenderGlyph(string ch, int size, GlyphStyle style, string setName)
    {
        var one = SetOf(setName);

        Poly[] polys;
        if (!one.TryGetValue(ch, out polys)) return null;

        return Rasterize(polys, size, style);
    }

    /// <summary>字形库里所有字（按笔画数、再按 Unicode 排序，方便预览时对齐）</summary>
    public static List<string> AllGlyphsSorted()
    {
        return AllGlyphsSorted(SetPlayer);
    }

    public static List<string> AllGlyphsSorted(string setName)
    {
        var one = SetOf(setName);

        var list = new List<string>(one.Keys);
        list.Sort((a, b) =>
        {
            int ca = one[a].Length, cb = one[b].Length;
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

    /// <summary>
    /// 笔画描述（给编辑器审计 / 拖动工具用）。
    ///
    /// **点数不限** —— 用 pts 存整条折线，拖动编辑器可以随时在中间插点，
    /// 所以"竖+横+竖"这类需要多次拐折的笔画也能拖出来。
    ///
    /// kind 只用来决定"拖动时的约束"和"写回源码时用哪个构造器"：
    ///   "H"  横 —— 拖动时保持水平
    ///   "V"  竖 —— 拖动时保持垂直
    ///   "D"  粗斜（撇捺）—— 端点自由
    ///   "DS" 细斜（点）—— 端点自由，笔画更细
    ///   "Z"  折 —— 多段折线，端点自由
    /// </summary>
    public struct StrokeInfo
    {
        public string kind;
        public List<Vector2> pts;
        public float thickness;

        public Vector2 P(int i) { return pts[i]; }
        public int Count { get { return pts != null ? pts.Count : 0; } }
    }

    /// <summary>
    /// 取某个字的原始笔画坐标，供编辑器工具读写。
    ///
    /// 注意：这里返回的是**拷贝**。改完之后要调用 ApplyStrokes 才会生效。
    /// </summary>
    public static List<StrokeInfo> DebugStrokes(string ch)
    {
        return DebugStrokes(ch, SetPlayer);
    }

    public static List<StrokeInfo> DebugStrokes(string ch, string setName)
    {
        var one = SetOf(setName);

        Poly[] polys;
        if (!one.TryGetValue(ch, out polys)) return null;

        var list = new List<StrokeInfo>();

        foreach (Poly p in polys)
        {
            var si = new StrokeInfo
            {
                thickness = p.t,
                pts = new List<Vector2>(p.pts)     // 拷贝，别改到原数据
            };

            if (p.pts.Count == 2)
            {
                float dx = p.pts[1].x - p.pts[0].x;
                float dy = p.pts[1].y - p.pts[0].y;

                if (Mathf.Abs(dy) < 1e-4f) si.kind = "H";
                else if (Mathf.Abs(dx) < 1e-4f) si.kind = "V";
                else si.kind = Mathf.Abs(p.t - 0.062f) < 0.001f ? "DS" : "D";
            }
            else
            {
                si.kind = "Z";
            }

            list.Add(si);
        }

        return list;
    }

    /// <summary>
    /// 用编辑器改好的笔画替换某个字的定义（内存中生效，可立即看到效果）。
    /// </summary>
    public static bool ApplyStrokes(string ch, List<StrokeInfo> strokes)
    {
        return ApplyStrokes(ch, strokes, SetPlayer);
    }

    public static bool ApplyStrokes(string ch, List<StrokeInfo> strokes, string setName)
    {
        var one = SetOf(setName);

        // 敌人字库里还没画过的字，允许**新建**（玩家字库则要求已存在）——
        // 否则"在编辑器里画一个新敌人字"这个最基本的操作都做不了。
        bool exists = one.ContainsKey(ch);

        if (!exists && setName != SetEnemy) return false;
        if (strokes == null || strokes.Count == 0) return false;

        var arr = new Poly[strokes.Count];

        for (int i = 0; i < strokes.Count; i++)
        {
            StrokeInfo s = strokes[i];
            float t = s.thickness > 0f ? s.thickness : 0.078f;

            if (s.pts == null || s.pts.Count < 2)
            {
                // 点数不合法就跳过（保持原来的笔画数，避免整张表错位）
                arr[i] = new Poly(new[] { Vector2.zero, Vector2.zero }, t);
                continue;
            }

            arr[i] = new Poly(new List<Vector2>(s.pts), t);
        }

        one[ch] = arr;

        // 贴图缓存必须清掉，否则还是显示旧字形
        spriteCache.Clear();
        return true;
    }

    /// <summary>把某个字恢复成源码里的原始定义</summary>
    public static bool ResetGlyph(string ch)
    {
        if (defs == null) defs = BuildDefinitions();
        if (!defs.ContainsKey(ch)) return false;

        // 重建整张表 = 丢掉所有运行时修改
        defs = BuildDefinitions();
        spriteCache.Clear();
        return true;
    }

    /// <summary>
    /// 重建**两套**字库，并清空贴图缓存。
    ///
    /// 注意是"两套" —— 原来只重建了玩家字库（`defs = BuildDefinitions()`），
    /// 敌人字库留在内存里不动。后果：字形编辑器的"丢弃改动重载"，
    /// 以及写回源码之后调用它，玩家那边恢复了、**敌人那边还是旧的内存数据**，
    /// 看起来就像"敌人的编辑没生效"。
    ///
    /// 这类 bug 叫**状态不完整重置** —— 有多个状态源时只重置了一部分。
    /// </summary>
    public static void RebuildAll()
    {
        // 两套都清掉；下一次访问时 SetOf 会自动重建
        sets.Remove(SetPlayer);
        sets.Remove(SetEnemy);

        spriteCache.Clear();

        // 立刻重建玩家库：大量调用点紧接着就要取字形
        defs = BuildDefinitions(SetPlayer);
    }

    /// <summary>
    /// 彻底清空与字形有关的所有内存状态。
    ///
    /// 拖动编辑器会通过 ApplyStrokes 改内存里的 defs。如果某个字怎么改都不对、
    /// 想"从零重做"，光改源码不够 —— 旧的 defs 与 spriteCache 还在，
    /// 场景里的字仍会显示旧贴图。这个方法把两处缓存都清掉，
    /// 下一次取字形时从 BuildDefinitions() 完整重建。
    /// </summary>
    public static void ClearAllCaches()
    {
        defs = null;
        spriteCache.Clear();
    }

    void BuildSprite(string ch, Poly[] polys)
    {
        // 复用缓存的 Sprite，避免每次换字都重建贴图（这是拾取卡顿的主因）
        // 缓存键**必须带上字库名** —— 否则玩家和敌人同名的字会撞缓存，
        // 结果是"改了一边、另一边跟着变"，正是要避免的那个 bug。
        string key = glyphSet + "|" + ch + "|" + textureSize + "|" + pixelsPerUnit
                   + (style.IsPlain ? "" : "|" + style.Key);

        Sprite sp;
        if (spriteCache.TryGetValue(key, out sp) && sp != null)
        {
            currentSprite = sp;
            if (spriteRenderer != null) spriteRenderer.sprite = currentSprite;
            return;
        }

        Texture2D tex = Rasterize(polys, textureSize, style);
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
        List<Vector2> v = p.pts;
        if (v == null || v.Count < 2) return new Seg[0];

        var segs = new List<Seg>();

        // 找出所有"真正的拐角"：与前后两点连线偏离超过阈值的顶点。
        //
        // 阈值 0.06 是经验值：小于它说明这三点的走向基本一致（比如拖动编辑器里
        // 在一条直线上多加的点），当作"同一段直笔"，不必在中间掐一刀。
        const float cornerThreshold = 0.06f;

        var corners = new List<int>();

        if (v.Count == 2)
        {
            segs.Add(new Seg
            {
                x0 = v[0].x, y0 = v[0].y,
                x1 = v[1].x, y1 = v[1].y,
                t0 = p.t, t1 = p.t
            });
            return segs.ToArray();
        }

        for (int i = 1; i < v.Count - 1; i++)
        {
            Vector2 prev = v[i - 1], cur = v[i], next = v[i + 1];
            Vector2 chord = next - prev;
            float len = chord.magnitude;

            float dev = len < 1e-6f
                ? (cur - prev).magnitude
                : Mathf.Abs((cur - prev).x * chord.y - (cur - prev).y * chord.x) / len;

            if (dev > cornerThreshold) corners.Add(i);
        }

        // 没有拐角 → 一整段直线（中间那些点只是编辑器留下的冗余点）
        if (corners.Count == 0)
        {
            segs.Add(new Seg
            {
                x0 = v[0].x, y0 = v[0].y,
                x1 = v[v.Count - 1].x, y1 = v[v.Count - 1].y,
                t0 = p.t, t1 = p.t
            });
            return segs.ToArray();
        }

        // 相邻拐角之间是一段直笔；拐角处两段都保持满宽。
        //
        // 关于等宽的处理（演进过程）：
        //   1. 最初写成"前半段满宽→半宽、后半段半宽→满宽"想让拐角接上，
        //      结果整条折线在拐角处掐细到一半 —— 口/日/田/白 的方框
        //      "腰部细一圈"，这是"字形不规整"的主要来源。
        //   2. 改成等宽 + 端点外扩半个笔宽填缝，缝隙没了，但笔画外侧
        //      会顶出一个小凸起（方框角上多个"钩"）。
        //   3. 现在是等宽 + 不外扩：光栅化用圆头端点，两段在拐角处本就
        //      重叠出一个圆角方块，直角已经接得住，不需要额外外扩。
        int from = 0;
        for (int c = 0; c < corners.Count; c++)
        {
            int to = corners[c];
            segs.Add(new Seg
            {
                x0 = v[from].x, y0 = v[from].y,
                x1 = v[to].x, y1 = v[to].y,
                t0 = p.t, t1 = p.t
            });
            from = to;
        }
        // 最后一段：最后一个拐角 → 终点
        segs.Add(new Seg
        {
            x0 = v[from].x, y0 = v[from].y,
            x1 = v[v.Count - 1].x, y1 = v[v.Count - 1].y,
            t0 = p.t, t1 = p.t
        });

        return segs.ToArray();
    }

    // ---------------- 光栅化 ----------------

    /// <summary>
    /// 把一个字形画成贴图。
    ///
    /// <paramref name="corruption"/> &gt; 0 时会**随机丢掉一些笔画**，
    /// 做出"残缺 / 混乱"的字形 —— 给敌人用。
    ///
    /// 为什么"掉整条笔画"就够：Poly 本来就代表一条笔画，
    /// 所以丢掉一个 Poly 天然就是"字少了一笔"，
    /// 比去改顶点数据简单得多，观感也正是想要的"残缺感"。
    /// </summary>
    /// <summary>
    /// 字形的"破损风格" —— 让同一个字形能长出不同观感，不用为每个敌人都手画一遍。
    ///
    /// 三个维度：
    ///   corruption  残缺度：每条笔画有多大概率整条丢失
    ///   jitter      错位：字在框里随机偏移的幅度（0.5 = 最多偏半个字）
    ///   spin        倾斜：整体旋转角度
    ///
    /// 都是**生成贴图时**应用的，所以改完立刻看到，不需要重画字形。
    /// </summary>
    [System.Serializable]
    public struct GlyphStyle
    {
        public float corruption;
        public float jitter;
        public float spin;

        /// <summary>
        /// 随机种子 —— **决定"具体缺哪几笔"**。
        ///
        /// 为什么需要它：残缺原来是 `Random.value`，
        /// 于是同一个敌人在不同关卡、甚至重进游戏后，掉的笔画都不一样。
        /// 玩家要的是**固定**：那只"缺了一横的王"每次都是缺那一横，
        /// 这样才有辨识度、才记得住。
        ///
        /// 由 Enemy 按 defId 算出来（同种敌人永远同一个种子）。
        /// </summary>
        public int seed;

        public bool IsPlain
        {
            get
            {
                return corruption <= 0.001f && jitter <= 0.001f && Mathf.Abs(spin) <= 0.01f;
            }
        }

        public string Key
        {
            get { return $"{corruption:0.###}_{jitter:0.###}_{spin:0.#}_{seed}"; }
        }

        public static GlyphStyle None
        {
            get { return new GlyphStyle(); }
        }
    }
    static Texture2D Rasterize(Poly[] polys, int size)
    {
        return Rasterize(polys, size, default(GlyphStyle));
    }

    static Texture2D Rasterize(Poly[] polys, int size, GlyphStyle style)
    {
        float corruption = style.corruption;

        // 确定性随机源：种子来自 style.seed
        var det = new System.Random(style.seed * 31 + polys.Length * 7919 + 12345);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        // 先把所有折线展开成线段，避免逐像素重复计算
        var all = new List<Seg>();
        for (int i = 0; i < polys.Length; i++)
        {
            // 残缺：按概率整条丢掉这个笔画。
            //
            // 用**由 seed 派生的确定性随机**，不是 Random.value ——
            // 后者每次生成贴图都不同，会让"同一只怪"的残破样子每局都不一样。
            // 现在同种敌人（同 seed）永远掉同样的笔画。
            if (corruption > 0f && det.NextDouble() < corruption) continue;

            Seg[] s = BuildOutline(polys[i]);
            for (int k = 0; k < s.Length; k++) all.Add(s[k]);
        }

        Color[] pixels = new Color[size * size];

        // 错位：整个字在框里随机偏一点，做出"没对齐"的观感。
        // 用固定种子保证同一个风格每次生成的偏移一致，不会每帧乱跳。
        float offX = 0f, offY = 0f;
        if (style.jitter > 0.001f)
        {
            offX = ((float)det.NextDouble() * 2f - 1f) * style.jitter * 0.5f;
            offY = ((float)det.NextDouble() * 2f - 1f) * style.jitter * 0.5f;
        }

        // 倾斜：绕中心旋转
        float spinRad = style.spin * Mathf.Deg2Rad;
        float cosS = Mathf.Cos(spinRad), sinS = Mathf.Sin(spinRad);

        for (int y = 0; y < size; y++)
        {
            float py = (y + 0.5f) / size;

            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size;

                // 先把采样点反向变换回去，等于把整个字平移+旋转
                float sx = px - 0.5f - offX;
                float sy = py - 0.5f - offY;

                if (Mathf.Abs(style.spin) > 0.01f)
                {
                    float rx = sx * cosS + sy * sinS;
                    float ry = -sx * sinS + sy * cosS;
                    sx = rx; sy = ry;
                }

                px = sx + 0.5f;
                py = sy + 0.5f;

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
        return BuildDefinitions(SetPlayer);
    }

    /// <summary>
    /// 构建某一套字库。
    ///
    /// 玩家的字库就是原来那 49 个字；敌人的字库**自成一个方法**
    /// （见下面的敌人字库区），所以两边可以长得完全不一样。
    ///
    /// <paramref name="setName"/> 会记进 <see cref="buildingSet"/>，
    /// 这样构建过程中调用的 Save("一") 知道该写进哪一套。
    /// </summary>
    static Dictionary<string, Poly[]> BuildDefinitions(string setName)
    {
        buildingSet = string.IsNullOrEmpty(setName) ? SetPlayer : setName;

        if (buildingSet == SetEnemy) return BuildEnemyDefinitions();

        var d = new Dictionary<string, Poly[]>();

        // 笔画粗细：按标准汉字字面约 1/11 ~ 1/12 取。
        // 原来的 0.105 偏粗，笔画之间几乎糊在一起，缩小到 32px 显示时更明显。
        const float T = 0.078f;   // 标准笔画粗
        const float TT = 0.058f;  // 细一点（点、短撇）

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

        System.Action<string> Save = key =>
        {
            d[key] = L.ToArray();
            L = new List<Poly>();
            // 顺手挂进总表，这样构建期间也能被 SetOf 查到
            sets[buildingSet] = d;
        };

        // --- 通用折线构造器：点数不限 ---
        //
        // 为什么不用 P(L, 粗, params float[])：
        //   C# 的 params 不能直接变成可赋值的委托（Action<...>），
        //   写 P(L, 0.078f, 0.5f,0.9f, ...) 会被编译器当成 10 个参数而报错
        //   （CS1593: Delegate 'Action<List<Poly>, float, float[]>' does not take 10 arguments）。
        //   所以按点数拆成 P3/P4/P5 三个重载，超过 5 点用 AddPoly 兜底。
        //
        // 拖动编辑器生成的代码就用这些形式，例如：
        //   P4(L, 0.078f, 0.50f,0.90f, 0.50f,0.55f, 0.78f,0.55f, 0.78f,0.20f)

        System.Action<List<Poly>, float, List<Vector2>> AddPoly =
            (l, th, pts) => l.Add(new Poly(pts, th));

        System.Action<List<Poly>, float, float, float, float, float, float, float> P3 =
            (l, th, x0, y0, x1, y1, x2, y2) => AddPoly(l, th, new List<Vector2>
            {
                new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x2, y2)
            });

        System.Action<List<Poly>, float, float, float, float, float, float, float, float, float> P4 =
            (l, th, x0, y0, x1, y1, x2, y2, x3, y3) => AddPoly(l, th, new List<Vector2>
            {
                new Vector2(x0, y0), new Vector2(x1, y1),
                new Vector2(x2, y2), new Vector2(x3, y3)
            });

        System.Action<List<Poly>, float, float, float, float, float, float, float, float, float, float, float> P5 =
            (l, th, x0, y0, x1, y1, x2, y2, x3, y3, x4, y4) => AddPoly(l, th, new List<Vector2>
            {
                new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x2, y2),
                new Vector2(x3, y3), new Vector2(x4, y4)
            });

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
        // 丿
        P4(L, 0.078f, 0.52f, 0.86f, 0.50f, 0.38f, 0.42f, 0.24f, 0.32f, 0.14f);
        Save("丿");
        // 捺：从左上到右下
        // 乀
        P4(L, 0.078f, 0.30f, 0.80f, 0.46f, 0.48f, 0.56f, 0.28f, 0.78f, 0.16f);
        Save("乀");
        // 点：短促的斜点，别太细
        DS(L, 0.46f, 0.58f, 0.54f, 0.42f); Save("丶");
        // 折：先横后竖，横细竖粗，拐角由 BuildOutline 自动接上
        // 乛
        Z(L, 0.20f, 0.72f, 0.76f, 0.72f, 0.76f, 0.24f);
        Save("乛");

        // ============================================================
        // 二画
        // ============================================================
        // 二
        H(L, 0.26f, 0.70f, 0.70f);
        H(L, 0.10f, 0.90f, 0.24f);
        Save("二");

        // 十
        H(L, 0.19f, 0.83f, 0.54f);
        V(L, 0.50f, 0.14f, 0.92f);
        Save("十");

        // 人
        P3(L, 0.078f, 0.48f, 0.88f, 0.44f, 0.58f, 0.18f, 0.20f);
        P4(L, 0.078f, 0.44f, 0.58f, 0.62f, 0.38f, 0.72f, 0.28f, 0.86f, 0.20f);
        Save("人");

        // 八
        D(L, 0.42f, 0.74f, 0.21f, 0.28f);
        D(L, 0.52f, 0.88f, 0.81f, 0.27f);
        Save("八");

        // 丁
        H(L, 0.15f, 0.83f, 0.82f);
        P3(L, 0.078f, 0.35f, 0.29f, 0.49f, 0.19f, 0.49f, 0.81f);
        Save("丁");

        // 了
        // 注意：这条笔画有 6 个点，超过了 P5。请手动用 AddPoly(...) 写，或删掉几个点。
        // 了
        // 注意：这条笔画有 6 个点，超过了 P5。请手动用 AddPoly(...) 写，或删掉几个点。
        AddPoly(L, 0.078f, new List<Vector2> { new Vector2(0.18f, 0.86f), new Vector2(0.76f, 0.86f), new Vector2(0.50f, 0.56f), new Vector2(0.50f, 0.36f), new Vector2(0.50f, 0.18f), new Vector2(0.30f, 0.38f) });
        Save("了");

        // 刀
        P3(L, 0.078f, 0.76f, 0.86f, 0.76f, 0.26f, 0.60f, 0.36f);
        H(L, 0.76f, 0.16f, 0.86f);
        D(L, 0.48f, 0.84f, 0.22f, 0.32f);
        Save("刀");

        // ============================================================
        // 三画
        // ============================================================
        // 三：上中短、下最长
        H(L, 0.26f, 0.74f, 0.78f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.10f, 0.90f, 0.22f); Save("三");

        // 大
        H(L, 0.20f, 0.78f, 0.68f);
        P3(L, 0.078f, 0.48f, 0.96f, 0.48f, 0.66f, 0.28f, 0.30f);
        P4(L, 0.078f, 0.50f, 0.65f, 0.60f, 0.43f, 0.68f, 0.35f, 0.78f, 0.29f);
        Save("大");

        // 工
        H(L, 0.26f, 0.74f, 0.74f);
        H(L, 0.14f, 0.86f, 0.26f);
        V(L, 0.50f, 0.26f, 0.74f);
        Save("工");

        // 干
        H(L, 0.25f, 0.73f, 0.85f);
        H(L, 0.10f, 0.90f, 0.55f);
        V(L, 0.50f, 0.13f, 0.85f);
        Save("干");

        // 士
        H(L, 0.20f, 0.82f, 0.53f);
        H(L, 0.35f, 0.67f, 0.27f);
        V(L, 0.51f, 0.25f, 0.85f);
        // 官：宀（宝盖头）+ 两个口上下叠
        H(L, 0.12f, 0.88f, 0.86f);      // 宀 长横
        V(L, 0.50f, 0.88f, 0.78f);      // 宀 中间的点
        H(L, 0.12f, 0.50f, 0.74f);      // 宀 左半横
        H(L, 0.50f, 0.88f, 0.74f);      // 宀 右半横
        V(L, 0.88f, 0.74f, 0.66f);      // 宀 右端下钩

        // 上口
        V(L, 0.33f, 0.60f, 0.36f);
        V(L, 0.67f, 0.60f, 0.36f);
        H(L, 0.33f, 0.67f, 0.60f);
        H(L, 0.33f, 0.67f, 0.36f);

        // 下口
        V(L, 0.35f, 0.26f, 0.06f);
        V(L, 0.65f, 0.26f, 0.06f);
        H(L, 0.35f, 0.65f, 0.26f);
        H(L, 0.35f, 0.65f, 0.06f);
        Save("官");

        Save("士");

        // 土
        H(L, 0.27f, 0.75f, 0.64f);
        H(L, 0.12f, 0.92f, 0.25f);
        V(L, 0.51f, 0.25f, 0.97f);
        Save("土");

        // 口
        V(L, 0.28f, 0.28f, 0.76f);
        P4(L, 0.078f, 0.28f, 0.76f, 0.50f, 0.76f, 0.72f, 0.76f, 0.72f, 0.28f);
        H(L, 0.28f, 0.72f, 0.28f);
        Save("口");

        // 山：中竖最高，两侧竖短，底部横托住
        V(L, 0.50f, 0.22f, 0.86f);
        Z(L, 0.22f, 0.62f, 0.22f, 0.22f, 0.78f, 0.22f);
        V(L, 0.78f, 0.22f, 0.62f); Save("山");

        // 川
        P3(L, 0.078f, 0.24f, 0.84f, 0.24f, 0.40f, 0.16f, 0.16f);
        V(L, 0.50f, 0.16f, 0.84f);
        V(L, 0.78f, 0.16f, 0.84f);
        Save("川");

        // 小
        P4(L, 0.078f, 0.38f, 0.26f, 0.50f, 0.14f, 0.50f, 0.40f, 0.50f, 0.80f);
        D(L, 0.29f, 0.56f, 0.21f, 0.28f);
        D(L, 0.68f, 0.54f, 0.78f, 0.33f);
        Save("小");

        // 上
        V(L, 0.48f, 0.21f, 0.87f);
        H(L, 0.50f, 0.74f, 0.54f);
        H(L, 0.14f, 0.86f, 0.20f);
        Save("上");

        // 下
        H(L, 0.20f, 0.80f, 0.80f);
        V(L, 0.50f, 0.24f, 0.80f);
        D(L, 0.53f, 0.59f, 0.66f, 0.50f);
        Save("下");

        // ============================================================
        // 四画
        // ============================================================
        // 王：三横一竖，上中短、下最长，竖贯通三横
        H(L, 0.28f, 0.72f, 0.80f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.10f, 0.90f, 0.20f);
        V(L, 0.50f, 0.20f, 0.80f); Save("王");

        // 丰
        H(L, 0.22f, 0.80f, 0.68f);
        H(L, 0.28f, 0.74f, 0.50f);
        H(L, 0.21f, 0.82f, 0.32f);
        V(L, 0.51f, 0.10f, 0.90f);
        Save("丰");

        // 木
        H(L, 0.16f, 0.84f, 0.62f);
        V(L, 0.50f, 0.12f, 0.90f);
        D(L, 0.50f, 0.62f, 0.26f, 0.28f);
        P3(L, 0.078f, 0.50f, 0.61f, 0.64f, 0.40f, 0.82f, 0.28f);
        Save("木");

        // 天
        H(L, 0.30f, 0.70f, 0.80f);
        H(L, 0.15f, 0.87f, 0.56f);
        P3(L, 0.078f, 0.50f, 0.80f, 0.50f, 0.56f, 0.28f, 0.22f);
        P4(L, 0.078f, 0.50f, 0.56f, 0.64f, 0.34f, 0.72f, 0.26f, 0.84f, 0.22f);
        Save("天");

        // 太
        H(L, 0.22f, 0.76f, 0.68f);
        P3(L, 0.078f, 0.48f, 0.90f, 0.48f, 0.68f, 0.26f, 0.28f);
        P3(L, 0.078f, 0.48f, 0.68f, 0.64f, 0.42f, 0.78f, 0.28f);
        P3(L, 0.058f, 0.39f, 0.46f, 0.45f, 0.38f, 0.48f, 0.34f);
        Save("太");

        // 兀
        H(L, 0.14f, 0.90f, 0.82f);
        D(L, 0.42f, 0.82f, 0.28f, 0.26f);
        P4(L, 0.078f, 0.66f, 0.82f, 0.66f, 0.26f, 0.78f, 0.26f, 0.86f, 0.36f);
        Save("兀");

        // 日：窄长方框，中间一横
        V(L, 0.32f, 0.14f, 0.86f);
        Z(L, 0.32f, 0.86f, 0.68f, 0.86f, 0.68f, 0.14f);
        H(L, 0.32f, 0.68f, 0.50f);
        H(L, 0.32f, 0.68f, 0.14f); Save("日");

        // 中
        V(L, 0.32f, 0.44f, 0.68f);
        P3(L, 0.078f, 0.32f, 0.68f, 0.76f, 0.68f, 0.76f, 0.44f);
        H(L, 0.32f, 0.76f, 0.44f);
        V(L, 0.54f, 0.17f, 0.91f);
        Save("中");

        // 水
        V(L, 0.50f, 0.24f, 0.91f);
        P3(L, 0.078f, 0.22f, 0.66f, 0.42f, 0.67f, 0.18f, 0.46f);
        P3(L, 0.078f, 0.77f, 0.75f, 0.61f, 0.64f, 0.87f, 0.43f);
        D(L, 0.51f, 0.25f, 0.35f, 0.37f);
        Save("水");

        // 少
        V(L, 0.47f, 0.40f, 0.80f);
        P3(L, 0.058f, 0.38f, 0.64f, 0.32f, 0.58f, 0.26f, 0.52f);
        P3(L, 0.078f, 0.56f, 0.70f, 0.62f, 0.62f, 0.68f, 0.54f);
        D(L, 0.65f, 0.52f, 0.31f, 0.24f);
        Save("少");

        // 火
        D(L, 0.26f, 0.70f, 0.38f, 0.58f);
        D(L, 0.64f, 0.72f, 0.54f, 0.60f);
        P3(L, 0.078f, 0.45f, 0.87f, 0.45f, 0.58f, 0.27f, 0.30f);
        D(L, 0.46f, 0.58f, 0.68f, 0.30f);
        Save("火");

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

        // 五
        H(L, 0.26f, 0.74f, 0.86f);
        P4(L, 0.078f, 0.28f, 0.52f, 0.42f, 0.52f, 0.74f, 0.53f, 0.74f, 0.17f);
        H(L, 0.32f, 0.74f, 0.52f);
        P3(L, 0.078f, 0.44f, 0.85f, 0.44f, 0.49f, 0.45f, 0.17f);
        H(L, 0.16f, 0.96f, 0.17f);
        Save("五");

        // 正
        H(L, 0.24f, 0.74f, 0.86f);
        V(L, 0.50f, 0.20f, 0.86f);
        V(L, 0.29f, 0.19f, 0.54f);
        H(L, 0.48f, 0.76f, 0.54f);
        H(L, 0.10f, 0.90f, 0.18f);
        Save("正");

        // 白
        D(L, 0.56f, 0.96f, 0.42f, 0.84f);
        V(L, 0.32f, 0.26f, 0.76f);
        P4(L, 0.078f, 0.32f, 0.76f, 0.50f, 0.76f, 0.68f, 0.76f, 0.68f, 0.26f);
        H(L, 0.32f, 0.68f, 0.52f);
        H(L, 0.33f, 0.69f, 0.26f);
        Save("白");

        // 田
        V(L, 0.26f, 0.18f, 0.74f);
        P3(L, 0.078f, 0.26f, 0.74f, 0.74f, 0.74f, 0.74f, 0.16f);
        H(L, 0.26f, 0.74f, 0.18f);
        V(L, 0.50f, 0.18f, 0.74f);
        H(L, 0.25f, 0.73f, 0.45f);
        Save("田");

        // 目：比日多一横，更高更窄
        V(L, 0.32f, 0.12f, 0.88f);
        Z(L, 0.32f, 0.88f, 0.68f, 0.88f, 0.68f, 0.12f);
        H(L, 0.32f, 0.68f, 0.63f);
        H(L, 0.32f, 0.68f, 0.38f);
        H(L, 0.32f, 0.68f, 0.12f); Save("目");
        // ==== 圭 ====
        // 结构：两个「土」上下相叠，上小下大。上土 y 0.64~0.98，下土 y 0.10~0.43。
        // 下土的竖只到自己的上横（0.43），**不能**穿过上土的下横（0.72），
        // 否则两个土会被一根竖串起来，就不像"两个土"了。
        // 圭
        H(L, 0.29f, 0.69f, 0.71f);
        H(L, 0.24f, 0.80f, 0.53f);
        V(L, 0.50f, 0.54f, 0.88f);
        H(L, 0.27f, 0.75f, 0.30f);
        H(L, 0.14f, 0.86f, 0.10f);
        V(L, 0.50f, 0.10f, 0.43f);
        Save("圭");


        // 由：田 的中竖向上出头
        V(L, 0.26f, 0.14f, 0.68f);
        Z(L, 0.26f, 0.68f, 0.74f, 0.68f, 0.74f, 0.14f);
        H(L, 0.26f, 0.74f, 0.14f);
        V(L, 0.50f, 0.14f, 0.90f);
        H(L, 0.26f, 0.74f, 0.42f); Save("由");

        // ============================================================
        // 七画：兵
        // ============================================================
        // 兵
        D(L, 0.59f, 0.96f, 0.33f, 0.86f);
        V(L, 0.33f, 0.43f, 0.86f);
        H(L, 0.32f, 0.70f, 0.67f);
        V(L, 0.69f, 0.42f, 0.68f);
        H(L, 0.13f, 0.89f, 0.43f);
        D(L, 0.38f, 0.36f, 0.26f, 0.13f);
        D(L, 0.60f, 0.35f, 0.79f, 0.13f);
        Save("兵");
        // ==== 里 ====
        // 结构：上「田」下「土」。田占 y 0.46~0.94，土占 y 0.10~0.36，部件间隙 0.10。
        // 土的横取与田框同宽（0.24~0.76），下盘才托得住；所有平行笔画间距 >= 0.12。
        // 里
        V(L, 0.31f, 0.47f, 0.76f);
        P3(L, 0.078f, 0.30f, 0.76f, 0.69f, 0.76f, 0.69f, 0.48f);
        H(L, 0.32f, 0.69f, 0.49f);
        V(L, 0.50f, 0.46f, 0.76f);
        H(L, 0.30f, 0.70f, 0.62f);
        H(L, 0.37f, 0.61f, 0.35f);
        H(L, 0.23f, 0.79f, 0.19f);
        V(L, 0.50f, 0.20f, 0.46f);
        Save("里");


        // ============================================================
        // 九画：皇
        //   上「白」下「王」。字理：**上窄下宽**，白瘦、王宽，靠宽度差分层。
        //   白的三横间距 0.15 以上才不糊；王的竖只在下半部，不穿白的底横。
        // ============================================================
        // 白（上部，瘦）
        DS(L, 0.50f, 0.99f, 0.34f, 0.90f);              // 撇
        V(L, 0.32f, 0.56f, 0.90f);                      // 左竖
        Z(L, 0.32f, 0.90f, 0.68f, 0.90f, 0.68f, 0.56f); // 横折
        H(L, 0.32f, 0.68f, 0.73f);                      // 白 中横
        H(L, 0.32f, 0.68f, 0.56f);                      // 白 底横
        // 王（下部，宽）
        H(L, 0.24f, 0.76f, 0.40f);                      // 王 上横
        H(L, 0.32f, 0.68f, 0.24f);                      // 王 中横
        V(L, 0.50f, 0.10f, 0.40f);                      // 王 竖（只在下半部）
        H(L, 0.10f, 0.90f, 0.10f); Save("皇");          // 王 底横

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
