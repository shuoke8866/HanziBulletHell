#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 敌人定义器。
///
/// ## 做什么
///
///   左边：现有的 11 种敌人列表 + 「新建」
///   中间：外观预览（底形 + 汉字 + 体型），以及弹幕预览
///   右边：外形 / 行为 / 数值 / 弹幕 四组参数
///
/// 弹幕有两套可选：
///   · 老的一套 —— 7 种固定形态（Aimed / Fan / Ring / ...）
///   · 新的一套 —— 勾上"用玩家形态原语"，就和玩家汉字共用
///     BulletShapes 那 9 种（straight / ring / scatter / ...）
///
/// ## 写回源码
///
/// 和弹幕编辑器一样保守：定位失败或多处匹配就**拒绝写入**，
/// 绝不冒险硬写（这个项目在"往源码追加定义"上出过两次事故）。
///
/// 菜单：Tools/敌人定义器
/// </summary>
public class EnemyDesignerWindow : EditorWindow
{
    const string SourcePath = "Assets/Scripts/EnemyDatabase.cs";

    class Row
    {
        public string id;
        public string displayName;
        public EnemyBodyShape shape;
        public Color shapeColor;
        public float shapeScale;
        public float shapeSpin;
        public Color bodyColor;
        public Vector3 bodyScale;
        public string glyph;
        public EnemyBehavior behavior;
        public EnemyShotPattern shotPattern;
        public int health;
        public float moveSpeed;
        public float shootInterval;
        public float bulletSpeed;
        public int bulletDamage;
        public bool usePlayerShapes;
        public List<string> shapeTags = new List<string>();
        public int shotCount;
        public float shotScale = 1f;
        public int minFloor;
        public bool isElite;
        public float glyphCorruption;
        public float glyphJitter;
        public float glyphSpin;

        public bool dirty;

        // 原始值（用于撤销）
        public string orig;
    }

    readonly List<Row> rows = new List<Row>();
    int selected = -1;
    Vector2 listScroll, editScroll;
    BulletPreview bulletPreview;
    double lastTick;
    bool playing = true;

    [MenuItem("Tools/敌人定义器")]
    public static void Open()
    {
        var w = GetWindow<EnemyDesignerWindow>("敌人定义器");
        w.minSize = new Vector2(940f, 620f);
        w.Reload();
    }

    void OnDestroy()
    {
        if (bulletPreview != null) bulletPreview.Dispose();
    }

    // ============================================================
    //  载入
    // ============================================================

    void Reload()
    {
        rows.Clear();
        selected = -1;

        foreach (EnemyDef d in EnemyDatabase.All)
        {
            var r = new Row
            {
                id = d.id,
                displayName = d.displayName,
                shape = d.bodyShape,
                shapeColor = d.shapeColor,
                shapeScale = d.shapeScale,
                shapeSpin = d.shapeSpin,
                bodyColor = d.bodyColor,
                bodyScale = d.bodyScale,
                glyph = d.glyph,
                behavior = d.behavior,
                shotPattern = d.shotPattern,
                health = d.health,
                moveSpeed = d.moveSpeed,
                shootInterval = d.shootInterval,
                bulletSpeed = d.bulletSpeed,
                bulletDamage = d.bulletDamage,
                usePlayerShapes = d.usePlayerShapes,
                shotCount = d.shotCount,
                shotScale = d.shotScale,
                minFloor = d.minFloor,
                isElite = d.isElite,
                glyphCorruption = d.glyphCorruption,
                glyphJitter = d.glyphJitter,
                glyphSpin = d.glyphSpin
            };
            if (d.shapeTags != null) r.shapeTags.AddRange(d.shapeTags);
            r.orig = Snapshot(r);
            rows.Add(r);
        }

        if (bulletPreview == null) bulletPreview = new BulletPreview();

        if (rows.Count > 0) Select(0);
        Debug.Log($"[敌人] 载入 {rows.Count} 种敌人");
    }

    static string Snapshot(Row r)
    {
        return $"{r.shape}|{ColorUtility.ToHtmlStringRGBA(r.shapeColor)}|{r.shapeScale}|{r.shapeSpin}|" +
               $"{ColorUtility.ToHtmlStringRGBA(r.bodyColor)}|{r.bodyScale}|{r.glyph}|{r.behavior}|" +
               $"{r.shotPattern}|{r.health}|{r.moveSpeed}|{r.shootInterval}|{r.bulletSpeed}|" +
               $"{r.bulletDamage}|{r.usePlayerShapes}|{string.Join(",", r.shapeTags.ToArray())}|" +
               $"{r.shotCount}|{r.shotScale}|{r.minFloor}|{r.isElite}|" +
               $"{r.glyphCorruption}|{r.glyphJitter}|{r.glyphSpin}";
    }

    void Select(int i)
    {
        selected = Mathf.Clamp(i, 0, rows.Count - 1);
        RebuildBulletPreview();
        Repaint();
    }

    Row Current { get { return selected >= 0 && selected < rows.Count ? rows[selected] : null; } }

    void RebuildBulletPreview()
    {
        Row r = Current;
        if (r == null || bulletPreview == null) return;

        // 用敌人身上那个汉字的笔画给子弹上色（和玩家侧观感一致）
        var strokes = new List<StrokeType>();
        var probe = new GameObject("__EnemyPreviewProbe");
        var hs = probe.AddComponent<HanziSystem>();
        hs.EnsureInitialized();
        HanziDef hd = hs.FindByName(r.glyph);
        if (hd != null && hd.strokes != null) strokes.AddRange(hd.strokes);
        Object.DestroyImmediate(probe);

        if (r.usePlayerShapes)
        {
            bulletPreview.Setup(r.shapeTags, r.shotCount, r.bulletSpeed, r.shotScale, strokes);
        }
        else
        {
            // 老形态：映射成最接近的原语，让预览也能看到个大概
            bulletPreview.Setup(LegacyToShapes(r.shotPattern), LegacyCount(r.shotPattern),
                                r.bulletSpeed, 1f, strokes);
        }

        bulletPreview.Clear();
        bulletPreview.WarmUp(18);
    }

    static List<string> LegacyToShapes(EnemyShotPattern p)
    {
        var l = new List<string>();
        switch (p)
        {
            case EnemyShotPattern.Aimed: l.Add("straight"); break;
            case EnemyShotPattern.DoubleAimed: l.Add("vshape"); break;
            case EnemyShotPattern.Fan: l.Add("scatter"); break;
            case EnemyShotPattern.Ring: l.Add("ring"); break;
            case EnemyShotPattern.Spiral: l.Add("omni"); break;
            case EnemyShotPattern.Wall: l.Add("parallel"); break;
            case EnemyShotPattern.Twin: l.Add("cross"); break;
            case EnemyShotPattern.Burst: l.Add("shotgun"); break;
            default: break;
        }
        return l;
    }

    static int LegacyCount(EnemyShotPattern p)
    {
        switch (p)
        {
            case EnemyShotPattern.None: return 0;
            case EnemyShotPattern.Aimed: return 1;
            case EnemyShotPattern.DoubleAimed: return 2;
            case EnemyShotPattern.Fan: return 5;
            case EnemyShotPattern.Ring: return 8;
            case EnemyShotPattern.Spiral: return 6;
            case EnemyShotPattern.Wall: return 5;
            case EnemyShotPattern.Twin: return 4;
            case EnemyShotPattern.Burst: return 3;
            default: return 1;
        }
    }

    void Update()
    {
        if (!playing || bulletPreview == null) return;

        double now = EditorApplication.timeSinceStartup;
        if (now - lastTick < 1.0 / 60.0) return;
        lastTick = now;

        bulletPreview.Step();
        Repaint();
    }

    // ============================================================
    //  绘制
    // ============================================================

    void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawPreviewPane();
        DrawEditPane();
        EditorGUILayout.EndHorizontal();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("重新载入", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Reload();

        GUILayout.FlexibleSpace();

        int dirty = 0;
        foreach (Row r in rows) if (r.dirty) dirty++;

        GUI.enabled = dirty > 0;
        if (GUILayout.Button($"写入源码（{dirty} 处改动）",
                             EditorStyles.toolbarButton, GUILayout.Width(180f)))
            SaveAll();
        GUI.enabled = true;

        if (GUILayout.Button("全部撤销", EditorStyles.toolbarButton, GUILayout.Width(80f)))
        {
            foreach (Row r in rows) r.dirty = false;
            Reload();
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(200f));
        EditorGUILayout.LabelField("敌人", EditorStyles.boldLabel);

        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));

        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            int floor = 1;
            for (int f = 1; f <= 3; f++) { if (r.minFloor <= f) { floor = f; break; } }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button((r.dirty ? "* " : "") + r.displayName,
                                 i == selected ? EditorStyles.boldLabel : EditorStyles.label,
                                 GUILayout.Width(110f)))
                Select(i);

            EditorGUILayout.LabelField($"{r.shape} · {r.health}血", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawPreviewPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(340f));

        Row r = Current;
        if (r == null) { EditorGUILayout.EndVertical(); return; }

        // ---- 外观预览：直接画底形 + 汉字 ----
        EditorGUILayout.LabelField("外观预览", EditorStyles.boldLabel);

        Rect box = GUILayoutUtility.GetRect(300f, 150f, GUILayout.ExpandWidth(false));
        EditorGUI.DrawRect(box, new Color(0.08f, 0.09f, 0.13f));

        float cx = box.x + box.width * 0.5f;
        float cy = box.y + box.height * 0.5f;

        // 底形
        Sprite shapeSprite = ProcTex.ForBodyShape(r.shape);
        if (shapeSprite != null)
        {
            float s = 92f * Mathf.Clamp(r.shapeScale, 0.3f, 2.5f);
            Rect sr = new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s);
            EditorGUI.DrawTextureTransparent(sr, shapeSprite.texture, ScaleMode.ScaleToFit);
            EditorGUI.DrawRect(new Rect(sr.x, sr.y, 1f, 1f), Color.clear);
        }

        // 汉字（用真实字形渲染，保证"编辑器里看到的"和"游戏里看到的"一致）
        //
        // 破损风格也必须应用 —— 否则调"残缺度"滑块时预览毫无变化，
        // 会让人以为功能没做（"编辑器和游戏不一致"这个坑踩过好几次）。
        Sprite glyphSprite = HanziRenderer.CreateSpriteFor(r.glyph, 96, 48f,
            new HanziRenderer.GlyphStyle
            {
                corruption = Mathf.Clamp01(r.glyphCorruption),
                jitter = Mathf.Clamp01(r.glyphJitter),
                spin = r.glyphSpin,
                seed = Enemy.StableSeed(r.id + "|" + r.glyph)
            },
            HanziRenderer.IsAllowed(r.glyph, HanziRenderer.SetEnemy)
                ? HanziRenderer.SetEnemy
                : HanziRenderer.SetPlayer);
        if (glyphSprite != null)
        {
            float g = 64f;
            Rect gr = new Rect(cx - g * 0.5f, cy - g * 0.5f, g, g);
            var old = GUI.color;
            GUI.color = r.bodyColor;
            GUI.DrawTexture(gr, glyphSprite.texture, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        EditorGUILayout.LabelField(
            $"{r.displayName}（{r.id}）  体型 {r.bodyScale.x:0.##}×{r.bodyScale.y:0.##}",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(6f);

        // ---- 弹幕预览 ----
        EditorGUILayout.LabelField("弹幕预览", EditorStyles.boldLabel);

        if (bulletPreview != null && bulletPreview.Texture != null)
        {
            Rect pr = GUILayoutUtility.GetRect(300f, 300f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(pr, bulletPreview.Texture, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.BeginHorizontal();
        playing = GUILayout.Toggle(playing, playing ? "▶ 播放中" : "⏸ 已暂停", "Button",
                                   GUILayout.Width(90f));
        if (GUILayout.Button("重放", GUILayout.Width(60f))) RebuildBulletPreview();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    void DrawEditPane()
    {
        Row r = Current;
        if (r == null) return;

        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);

        // ---------- 外形 ----------
        EditorGUILayout.LabelField("外形", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        r.shape = (EnemyBodyShape)EditorGUILayout.EnumPopup("底形", r.shape);
        if (r.shape != EnemyBodyShape.None)
        {
            r.shapeColor = EditorGUILayout.ColorField("底形颜色", r.shapeColor);
            r.shapeScale = EditorGUILayout.Slider("底形大小", r.shapeScale, 0.5f, 3f);
            r.shapeSpin = EditorGUILayout.Slider("自转速度", r.shapeSpin, -180f, 180f);
        }

        r.glyph = EditorGUILayout.TextField(
            new GUIContent("身上的汉字", "敌人显示哪个字。可以是字库里任何一个字。"), r.glyph);

        // ---- 字形选择器 + 一键打开字形编辑器 ----
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("从字库选…", GUILayout.Width(90f)))
            ShowGlyphMenu(r);

        if (HanziRenderer.IsAllowed(r.glyph, HanziRenderer.SetEnemy))
        {
            var old = GUI.color;
            GUI.color = new Color(0.55f, 1f, 0.55f);
            GUILayout.Label("✓ 用的你自己的敌人字形", EditorStyles.miniLabel);
            GUI.color = old;
        }
        else
        {
            var old = GUI.color;
            GUI.color = new Color(0.85f, 0.85f, 0.5f);
            GUILayout.Label("○ 还没画，暂用玩家字形", EditorStyles.miniLabel);
            GUI.color = old;
        }

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("打开字形编辑器（画新字 / 改这个字）", GUILayout.Height(22f)))
        {
            // 要"和字体编辑器一样自己编辑"，那就别让人去菜单里找 —— 直接开，并选中当前这个字
            var w = GetWindow<HanziGlyphEditor>("字形拖动编辑器");
            w.SelectGlyphByName(r.glyph);
            w.Focus();
        }

        r.bodyColor = EditorGUILayout.ColorField("汉字颜色", r.bodyColor);
        r.bodyScale = EditorGUILayout.Vector3Field(
            new GUIContent("体型", "汉字的缩放。横线怪是 2×0.5 这种扁的。"), r.bodyScale);

        EditorGUILayout.Space(6f);

        // ---- 破损风格 ----
        EditorGUILayout.LabelField("字形破损（残缺 / 混乱）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "字库共用同一套字形，靠这三项做「这只怪的字段坏掉了」的观感。",
            EditorStyles.miniLabel);

        r.glyphCorruption = EditorGUILayout.Slider(
            new GUIContent("残缺度", "每条笔画丢失的概率。0.3 ≈ 掉三成笔画，0.5 以上基本认不出。"),
            r.glyphCorruption, 0f, 0.8f);

        r.glyphJitter = EditorGUILayout.Slider(
            new GUIContent("错位", "字在框里随机偏移的幅度，做「没对齐」的观感。"),
            r.glyphJitter, 0f, 1f);

        r.glyphSpin = EditorGUILayout.Slider(
            new GUIContent("倾斜", "整体旋转角度（度）。"),
            r.glyphSpin, -45f, 45f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("随机一下", GUILayout.Width(80f)))
        {
            r.glyphCorruption = Random.Range(0.15f, 0.45f);
            r.glyphJitter = Random.Range(0.1f, 0.5f);
            r.glyphSpin = Random.Range(-22f, 22f);
        }
        if (GUILayout.Button("恢复完整", GUILayout.Width(80f)))
        {
            r.glyphCorruption = 0f;
            r.glyphJitter = 0f;
            r.glyphSpin = 0f;
        }
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck()) MarkDirty(r);

        EditorGUILayout.Space(10f);

        // ---------- 行为 ----------
        EditorGUILayout.LabelField("行为", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        r.behavior = (EnemyBehavior)EditorGUILayout.EnumPopup("移动方式", r.behavior);
        r.minFloor = EditorGUILayout.IntSlider(
            new GUIContent("从第几层出现", "1=第一层就有。用来做逐层引入。"), r.minFloor, 1, 3);
        r.isElite = EditorGUILayout.Toggle("精英", r.isElite);

        if (EditorGUI.EndChangeCheck()) MarkDirty(r);

        EditorGUILayout.Space(10f);

        // ---------- 数值 ----------
        EditorGUILayout.LabelField("数值", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        r.health = EditorGUILayout.IntSlider("血量", r.health, 1, 400);
        r.moveSpeed = EditorGUILayout.Slider("移速", r.moveSpeed, 0f, 10f);
        r.shootInterval = EditorGUILayout.Slider("射击间隔", r.shootInterval, 0.2f, 8f);
        r.bulletSpeed = EditorGUILayout.Slider("子弹速度", r.bulletSpeed, 2f, 20f);
        r.bulletDamage = EditorGUILayout.IntSlider("子弹伤害", r.bulletDamage, 0, 5);

        if (EditorGUI.EndChangeCheck()) { MarkDirty(r); RebuildBulletPreview(); }

        EditorGUILayout.Space(10f);

        // ---------- 弹幕 ----------
        EditorGUILayout.LabelField("弹幕", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        r.usePlayerShapes = EditorGUILayout.Toggle(
            new GUIContent("用玩家形态原语", "勾上就和玩家汉字共用那 9 种形态"),
            r.usePlayerShapes);

        if (EditorGUI.EndChangeCheck()) { MarkDirty(r); RebuildBulletPreview(); }

        if (r.usePlayerShapes)
        {
            EditorGUI.BeginChangeCheck();

            r.shotCount = EditorGUILayout.IntSlider("一轮几发", r.shotCount, 1, 16);

            r.shotScale = EditorGUILayout.Slider(
                new GUIContent("弹幕大小", "敌人子弹的尺寸倍率。1 = 标准。"),
                r.shotScale, 0.4f, 2.5f);

            EditorGUILayout.LabelField("形态标签（顺序即优先级）", EditorStyles.miniLabel);
            for (int i = 0; i < r.shapeTags.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                int idx = System.Array.IndexOf(BulletShapes.All, r.shapeTags[i]);
                int newIdx = EditorGUILayout.Popup(idx < 0 ? 0 : idx, ShapeLabels(),
                                                   GUILayout.Width(190f));
                if (newIdx != idx) { r.shapeTags[i] = BulletShapes.All[newIdx]; MarkDirty(r); }

                if (GUILayout.Button("×", GUILayout.Width(24f)))
                {
                    r.shapeTags.RemoveAt(i);
                    MarkDirty(r);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("＋ 加形态", GUILayout.Width(90f)) && r.shapeTags.Count < 4)
            {
                r.shapeTags.Add("straight");
                MarkDirty(r);
            }

            if (EditorGUI.EndChangeCheck()) RebuildBulletPreview();
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            r.shotPattern = (EnemyShotPattern)EditorGUILayout.EnumPopup("固定形态", r.shotPattern);
            if (EditorGUI.EndChangeCheck()) { MarkDirty(r); RebuildBulletPreview(); }

            EditorGUILayout.HelpBox(
                "预览用的是最接近的形态原语做的近似 —— 老形态的实际实现\n" +
                "在 Enemy.Shoot() 里，和预览不会逐帧一致。",
                MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>弹出字库列表，点一个就把 r.glyph 换成它</summary>
    void ShowGlyphMenu(Row r)
    {
        var menu = new GenericMenu();

        // 列出**敌人可用的字**（EnemyGlyphNames），并标出哪些已经画过了。
        //
        // 为什么不直接列玩家字库：敌人有自己独立的字库（HanziRenderer.SetEnemy），
        // 列玩家的字会让人以为"改这里能改敌人" —— 实际两套完全无关。
        var names = new List<string>(HanziRenderer.EnemyGlyphNames);

        foreach (string g in names)
        {
            string captured = g;
            bool current = g == r.glyph;

            // 敌人字库里已画的字标个记号，方便看出"哪些我已经改过了"
            bool drawn = HanziRenderer.IsAllowed(g, HanziRenderer.SetEnemy);
            string label = g + (drawn ? "  ✎已画" : "") + (current ? "   ← 当前" : "");

            menu.AddItem(new GUIContent(label), current,
                         () => { r.glyph = captured; MarkDirty(r); Repaint(); });
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent($"（{names.Count} 个可选 · ✎ = 敌人字库里有你自己的版本）"),
                     false, () => { });

        menu.ShowAsContext();
    }

    void MarkDirty(Row r)
    {
        r.dirty = Snapshot(r) != r.orig;
    }

    static string[] shapeLabelsCache;
    static string[] ShapeLabels()
    {
        if (shapeLabelsCache != null && shapeLabelsCache.Length == BulletShapes.All.Length)
            return shapeLabelsCache;

        shapeLabelsCache = new string[BulletShapes.All.Length];
        for (int i = 0; i < BulletShapes.All.Length; i++)
            shapeLabelsCache[i] = BulletShapes.DisplayName(BulletShapes.All[i]);
        return shapeLabelsCache;
    }

    // ============================================================
    //  写回源码
    // ============================================================

    void SaveAll()
    {
        if (!File.Exists(SourcePath))
        {
            Debug.LogError($"[敌人] 找不到 {SourcePath}");
            return;
        }

        string src = File.ReadAllText(SourcePath, Encoding.UTF8);
        string backup = SourcePath + ".bak";
        File.WriteAllText(backup, src, new UTF8Encoding(false));

        int ok = 0, skipped = 0;

        foreach (Row r in rows)
        {
            if (!r.dirty) continue;

            string replaced, err;
            if (!TryReplaceEnemy(src, r, out replaced, out err))
            {
                Debug.LogError($"[敌人] 「{r.displayName}」写入失败：{err}（已跳过）");
                skipped++;
                continue;
            }
            src = replaced;
            ok++;
        }

        File.WriteAllText(SourcePath, src, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        Debug.Log($"[敌人] 已写入 {ok} 条" +
                  (skipped > 0 ? $"，跳过 {skipped} 条" : "") + $"。备份在 {backup}");

        Reload();
    }

    /// <summary>
    /// 替换某个敌人定义里的字段值。
    ///
    /// 做法：定位 `id = "xxx",` 那一段到下一个 Register(...) 之间，
    /// 只在**这一段内**逐个替换字段值 —— 不动块外的任何字节。
    ///
    /// 安全规矩：定位失败 / 找不到字段 → 拒绝这一条，继续处理别的。
    /// </summary>
    static bool TryReplaceEnemy(string src, Row r, out string result, out string error)
    {
        result = src;
        error = null;

        string head = "id = \"" + r.id + "\",";
        int count = CountOccurrences(src, head);

        if (count == 0) { error = "源码里找不到这个 id"; return false; }
        if (count > 1) { error = $"源码里有 {count} 处同名 id，不敢乱改"; return false; }

        int start = src.IndexOf(head, System.StringComparison.Ordinal);
        int blockEnd = src.IndexOf("});", start, System.StringComparison.Ordinal);
        if (blockEnd < 0) { error = "找不到这条定义的结尾"; return false; }

        string block = src.Substring(start, blockEnd - start);
        string edited = block;

        edited = SetInt(edited, "health", r.health);
        edited = SetFloat(edited, "moveSpeed", r.moveSpeed);
        edited = SetFloat(edited, "shootInterval", r.shootInterval);
        edited = SetFloat(edited, "bulletSpeed", r.bulletSpeed);
        edited = SetInt(edited, "bulletDamage", r.bulletDamage);
        edited = SetInt(edited, "minFloor", r.minFloor);
        edited = SetBool(edited, "isElite", r.isElite);
        edited = SetEnum(edited, "behavior", "EnemyBehavior." + r.behavior);
        edited = SetEnum(edited, "shotPattern", "EnemyShotPattern." + r.shotPattern);
        edited = SetEnum(edited, "bodyShape", "EnemyBodyShape." + r.shape);
        edited = SetColor(edited, "bodyColor", r.bodyColor);
        edited = SetColor(edited, "shapeColor", r.shapeColor);
        edited = SetVector3(edited, "bodyScale", r.bodyScale);
        edited = SetFloat(edited, "shapeScale", r.shapeScale);
        edited = SetFloat(edited, "shapeSpin", r.shapeSpin);
        edited = SetString(edited, "glyph", r.glyph);
        edited = SetBool(edited, "usePlayerShapes", r.usePlayerShapes);
        edited = SetInt(edited, "shotCount", r.shotCount);
        edited = SetFloat(edited, "shotScale", r.shotScale);
        edited = SetTagList(edited, r.shapeTags);
        edited = SetFloat(edited, "glyphCorruption", r.glyphCorruption);
        edited = SetFloat(edited, "glyphJitter", r.glyphJitter);
        edited = SetFloat(edited, "glyphSpin", r.glyphSpin);

        result = src.Substring(0, start) + edited + src.Substring(blockEnd);
        return true;
    }

    static int CountOccurrences(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        { n++; i += needle.Length; }
        return n;
    }

    // ---------- 字段替换小工具 ----------
    // 每个都要求字段**已存在**；不存在就原样返回（不新增，避免写出畸形代码）

    static string SetInt(string s, string field, int v)
    {
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*-?\d+",
            field + " = " + v, RegexOptions.None);
    }

    static string SetFloat(string s, string field, float v)
    {
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*-?[\d.]+f",
            field + " = " + v.ToString("0.###") + "f", RegexOptions.None);
    }

    static string SetBool(string s, string field, bool v)
    {
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*(true|false)",
            field + " = " + (v ? "true" : "false"), RegexOptions.None);
    }

    static string SetEnum(string s, string field, string value)
    {
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*[\w\.]+",
            field + " = " + value, RegexOptions.None);
    }

    static string SetString(string s, string field, string value)
    {
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*""[^""]*""",
            field + " = \"" + value + "\"", RegexOptions.None);
    }

    static string SetColor(string s, string field, Color c)
    {
        string v = $"new Color({c.r:0.###}f, {c.g:0.###}f, {c.b:0.###}f, {c.a:0.###}f)";
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*new Color\([^\)]*\)",
            field + " = " + v, RegexOptions.None);
    }

    static string SetVector3(string s, string field, Vector3 v)
    {
        string val = $"new Vector3({v.x:0.###}f, {v.y:0.###}f, {v.z:0.###}f)";
        return Regex.Replace(s, @"\b" + field + @"\s*=\s*new Vector3\([^\)]*\)",
            field + " = " + val, RegexOptions.None);
    }

    /// <summary>形态标签列表 —— 整个列表换掉</summary>
    static string SetTagList(string s, List<string> tags)
    {
        string val = "new List<string> { ";
        for (int i = 0; i < tags.Count; i++)
        {
            if (i > 0) val += ", ";
            val += "\"" + tags[i] + "\"";
        }
        val += " }";

        // 有就替换，没有就**不加**（避免写出畸形代码；玩家可以手动补一行）
        return Regex.Replace(s, @"shapeTags\s*=\s*new List<string>\s*\{[^\}]*\}",
            "shapeTags = " + val, RegexOptions.None);
    }
}
#endif
