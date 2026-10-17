#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 汉字字形拖动编辑器（支持任意点数的折线笔画）。
///
/// 打开：菜单 Tools → 字形拖动编辑器
///
/// 左栏选字，中间是放大后的字形，**用鼠标拖动顶点**即可调整。
///
/// 操作：
///   拖实心圆点        移动该顶点
///   拖半透明小方点     在该位置插入一个顶点（用来做额外的拐折）
///   右键顶点          删除该顶点（至少要留 2 个）
///   拖笔画中段        整条笔画平移
///   空白处拖动        平移画布
///   滚轮              缩放
///   Ctrl + 拖动       临时关闭吸附
///   [ ]               上/下一个字
///
/// 「加点」按钮也会在最长的那一段中间插一个点，适合做"竖+横+竖"这类多次拐折。
///
/// 保存：
///   「应用（看效果）」  立即在 Unity 场景里生效，不写文件
///   「写入源码」       把坐标写回 HanziRenderer.cs 并触发重新编译
///   「复制代码」       复制到剪贴板自行粘贴
/// </summary>
public class HanziGlyphEditor : EditorWindow
{
    // ---------------- 参考网格 ----------------
    static readonly float[] Grid = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };
    const float SnapStep = 0.02f;     // 吸附步进
    const float MinGap = 0.12f;       // 平行笔画最小间距（笔宽 0.078 的 1.5 倍）
    const float HandleRadius = 6f;
    const float InsertRadius = 4f;

    // ---------------- 状态 ----------------
    List<string> glyphs = new List<string>();

    /// <summary>异常只报一次，避免每帧刷屏</summary>
    bool guiErrorLogged;

    /// <summary>当前编辑哪套字库：player = 玩家，enemy = 敌人</summary>
    string glyphSet = HanziRenderer.SetPlayer;

    string SourceFileFor(string set)
    {
        return set == HanziRenderer.SetEnemy
            ? "Assets/Scripts/EnemyGlyphs.cs"
            : "Assets/Scripts/HanziRenderer.cs";
    }
    int selected;
    Vector2 glyphScroll;

    List<HanziRenderer.StrokeInfo> strokes = new List<HanziRenderer.StrokeInfo>();
    int selectedStroke = -1;

    // 拖动状态
    int dragStroke = -1;
    int dragPoint = -1;        // >=0 拖顶点；-1 没拖点；-2 拖整条；-3 拖画布
    bool dragging;
    Vector2 dragLast;

    float zoom = 460f;
    Vector2 pan;

    bool snap = true;
    bool showGrid = true;
    string status = "";

    [MenuItem("Tools/字形拖动编辑器")]
    public static void Open()
    {
        var w = GetWindow<HanziGlyphEditor>("字形拖动编辑器");
        w.minSize = new Vector2(900f, 660f);
        w.ReloadGlyphList();
    }

    void OnEnable() { ReloadGlyphList(); }

    /// <summary>
    /// 从别的窗口跳过来时，直接选中指定的字。
    ///
    /// 敌人定义器里点"打开字形编辑器"会调它 —— 让人不用自己在下拉里翻。
    /// 找不到这个字就留在原位（不然会跳到列表第一项，反而让人困惑）。
    /// </summary>
    public void SelectGlyphByName(string ch)
    {
        if (string.IsNullOrEmpty(ch)) return;

        ReloadGlyphList();

        int idx = glyphs.IndexOf(ch);
        if (idx < 0)
        {
            Debug.LogWarning($"字形编辑器：字库里没有「{ch}」，先在下面这个列表里选一个再改，" +
                             "或者用「新建字形」加一个。");
            return;
        }

        selected = idx;
        LoadGlyph();
        Repaint();
    }

    void ReloadGlyphList()
    {
        // 敌人字库是"标称字表"驱动的 —— 它一开始是空的，
        // 但编辑器要能列出"可以画哪些字"，所以用 EnemyGlyphNames 而不是"已画的字"
        try
        {
            glyphs = glyphSet == HanziRenderer.SetEnemy
                ? new List<string>(HanziRenderer.EnemyGlyphNames)
                : HanziRenderer.AllGlyphsSorted();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[字形编辑器] 读取字形库失败：\n" + e);
            glyphs = new List<string>();
            return;
        }

        if (glyphs == null || glyphs.Count == 0)
        {
            selected = 0;
            strokes = new List<HanziRenderer.StrokeInfo>();
            selectedStroke = -1;
            return;
        }

        // **一定要夹取** —— 切换字库（玩家/敌人）后字数会变，selected 可能越界。
        // 越界会让后面 glyphs[selected] 抛 IndexOutOfRange，
        // 而异常会跳过 GUI 的 End 调用 → Unity 报
        // "Invalid GUILayout state ... Verify that all layout Begin/End calls match"。
        selected = Mathf.Clamp(selected, 0, glyphs.Count - 1);
        LoadGlyph();
    }

    /// <summary>
    /// 当前选中的字。**越界时自动夹取，绝不抛异常。**
    ///
    /// 所有原本写 `glyphs[selected]` 的地方都改用它 ——
    /// 那是最容易在"切换字库 / 增删字之后"炸掉的一处，
    /// 一旦炸掉就会跳过 GUI 的 End 调用，导致布局失衡报错。
    /// </summary>
    string CurrentChar
    {
        get
        {
            if (glyphs == null || glyphs.Count == 0) return "";
            int i = Mathf.Clamp(selected, 0, glyphs.Count - 1);
            return glyphs[i];
        }
    }

    void LoadGlyph()
    {
        if (glyphs.Count == 0) return;
        strokes = HanziRenderer.DebugStrokes(CurrentChar, glyphSet);
        if (strokes == null) strokes = new List<HanziRenderer.StrokeInfo>();
        selectedStroke = strokes.Count > 0 ? 0 : -1;
        dragStroke = -1; dragPoint = -1;
    }

    // ============================================================
    //  界面
    // ============================================================

    void OnGUI()
    {
        if (glyphs == null || glyphs.Count == 0)
        {
            EditorGUILayout.HelpBox("字形库为空。检查 HanziRenderer.BuildDefinitions()。", MessageType.Error);
            return;
        }

        // ============================================================
        //  try/finally 保证 BeginHorizontal 一定被 End
        // ============================================================
        //
        // 踩过的坑：Unity 报
        //     GUI Error: Invalid GUILayout state ...
        //     Verify that all layout Begin/End calls match
        //
        // 静态看代码所有 Begin/End 都是配对的，**问题在于运行时抛异常**：
        // 切换字库（玩家/敌人）时会 ReloadGlyphList()，glyphs 被重建，
        // 如果 selected 越界，后面 DrawCanvas 里 CurrentChar 就抛
        // IndexOutOfRange —— 异常直接跳过了 EndHorizontal。
        //
        // 现在即使中间抛异常，也会先 End 再抛，布局不会残废。
        EditorGUILayout.BeginHorizontal();
        try
        {
            DrawLeftPanel();
            DrawCanvas();
        }
        catch (System.Exception e)
        {
            // 打印一次就好，别每帧刷屏
            if (!guiErrorLogged)
            {
                guiErrorLogged = true;
                Debug.LogError("[字形编辑器] 绘制时抛异常（布局已兜底，不会再报 GUILayout state）：\n" + e);
            }
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }
    }

    void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(320f));

        // ---- 字库切换 ----
        //
        // 玩家和敌人是**两套独立的字库**。这里切换编辑哪一套 ——
        // 改敌人的字绝不会影响玩家操控的汉字。
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("字库", EditorStyles.boldLabel, GUILayout.Width(34f));

        bool wasPlayer = glyphSet == HanziRenderer.SetPlayer;
        if (GUILayout.Toggle(wasPlayer, "  玩家  ", "Button", GUILayout.Width(80f)) != wasPlayer
            && !wasPlayer)
        {
            glyphSet = HanziRenderer.SetPlayer;
            ReloadGlyphList();
        }

        bool wasEnemy = glyphSet == HanziRenderer.SetEnemy;
        if (GUILayout.Toggle(wasEnemy, "  敌人  ", "Button", GUILayout.Width(80f)) != wasEnemy
            && !wasEnemy)
        {
            glyphSet = HanziRenderer.SetEnemy;
            selected = 0;
            ReloadGlyphList();
        }

        GUILayout.Label(glyphSet == HanziRenderer.SetEnemy
            ? "→ 写入 Assets/Scripts/EnemyGlyphs.cs"
            : "→ 写入 Assets/Scripts/HanziRenderer.cs", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        if (glyphSet == HanziRenderer.SetEnemy)
        {
            EditorGUILayout.HelpBox(
                "正在编辑**敌人专用字库**。\n" +
                "这里改的字只影响敌人，玩家操控的汉字不受任何影响。\n" +
                "敌人字库一开始是空的 —— 敌人会先用玩家字库显示，你画一个就生效一个。",
                MessageType.Info);
        }

        EditorGUILayout.LabelField($"字形拖动编辑器（{selected + 1}/{glyphs.Count}）", EditorStyles.boldLabel);

        // --- 选字 ---
        // 注意：Begin/EndHorizontal 必须严格配对。
        // 之前这里在循环里按 %12 开合行，当字数正好是 12 的倍数时
        // 会多出一个已经 End 过的 Begin —— Unity 就报
        // "Invalid GUILayout state ... Verify that all layout Begin/End calls match"。
        // 现在改成"先算好行数，每行一个 Begin/End"，结构上不可能失衡。
        glyphScroll = EditorGUILayout.BeginScrollView(glyphScroll, GUILayout.Height(110f));

        const int perRow = 12;
        int rowCount = Mathf.CeilToInt(glyphs.Count / (float)perRow);
        for (int row = 0; row < rowCount; row++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = row * perRow; i < Mathf.Min((row + 1) * perRow, glyphs.Count); i++)
            {
                if (GUILayout.Button(glyphs[i], EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    selected = i; LoadGlyph(); GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(4f);
        DrawChecks();
        GUILayout.Space(4f);

        // --- 笔画列表 ---
        EditorGUILayout.LabelField($"笔画（{strokes.Count}）", EditorStyles.boldLabel);

        // ---- 新增笔画 ----
        //
        // 敌人字库一开始是空的，没有这个就完全没法从零画一个字（只能改已有的）。
        // 六个预设按钮对应汉字的六种基本笔画，坐标按标准字面比例给，
        // 加进去之后再拖点微调即可 —— 比让人从空白开始拖快得多。
        EditorGUILayout.LabelField("新增一笔", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("横", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("H");
        if (GUILayout.Button("竖", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("V");
        if (GUILayout.Button("撇", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("Pie");
        if (GUILayout.Button("捺", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("Na");
        if (GUILayout.Button("点", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("Dian");
        if (GUILayout.Button("折", EditorStyles.miniButton, GUILayout.Width(34f)))
            AddPresetStroke("Zhe");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("自由斜线", EditorStyles.miniButton, GUILayout.Width(70f)))
            AddPresetStroke("Free");
        if (GUILayout.Button("清空全部", EditorStyles.miniButton, GUILayout.Width(70f)))
        {
            strokes.Clear();
            selectedStroke = -1;
            status = "已清空所有笔画 —— 可以重新画。";
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            string label = $"{i}: {s.kind}  {s.Count} 点";

            EditorGUILayout.BeginHorizontal();
            bool sel = (i == selectedStroke);
            bool now = EditorGUILayout.Toggle(sel, GUILayout.Width(16f));
            if (now != sel) selectedStroke = i;

            if (GUILayout.Button(label, sel ? EditorStyles.boldLabel : EditorStyles.label))
                selectedStroke = i;

            if (GUILayout.Button("＋点", EditorStyles.miniButton, GUILayout.Width(40f)))
            {
                InsertPointOnLongestSegment(i);
                selectedStroke = i;
            }

            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                strokes.RemoveAt(i);
                selectedStroke = Mathf.Clamp(i - 1, -1, strokes.Count - 1);
                status = $"已删除笔画 {i}。";
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        if (strokes.Count == 0)
            EditorGUILayout.LabelField("（还没有笔画 —— 用上面的按钮加一笔）",
                                       EditorStyles.miniLabel);

        GUILayout.Space(6f);
        DrawPointEditor();
        GUILayout.Space(6f);
        DrawControls();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 加一笔预设笔画。
    ///
    /// 坐标按标准汉字字面比例给（0~1，y 向上，(0,0) 在左下角）：
    ///   竖线通常落在 x = 0.10 / 0.30 / 0.50 / 0.70 / 0.90
    ///   横线通常落在 y = 0.10 / 0.30 / 0.50 / 0.70 / 0.90
    /// 平行笔画间距建议 ≥ 0.12（笔宽 0.078，太近会糊在一起）。
    ///
    /// 加进来的位置是**字面正中**，避免落在边缘看不清；
    /// 之后拖动圆点移到想要的位置即可。
    /// </summary>
    void AddPresetStroke(string kind)
    {
        var si = new HanziRenderer.StrokeInfo
        {
            thickness = 0.078f,
            pts = new List<Vector2>()
        };

        switch (kind)
        {
            case "H":     // 横：中间一条横
                si.kind = "H";
                si.pts.Add(new Vector2(0.15f, 0.50f));
                si.pts.Add(new Vector2(0.85f, 0.50f));
                break;

            case "V":     // 竖：中间一条竖
                si.kind = "V";
                si.pts.Add(new Vector2(0.50f, 0.15f));
                si.pts.Add(new Vector2(0.50f, 0.85f));
                break;

            case "Pie":   // 撇：右上 → 左下
                si.kind = "D";
                si.pts.Add(new Vector2(0.72f, 0.82f));
                si.pts.Add(new Vector2(0.28f, 0.18f));
                break;

            case "Na":    // 捺：左上 → 右下
                si.kind = "D";
                si.pts.Add(new Vector2(0.28f, 0.82f));
                si.pts.Add(new Vector2(0.72f, 0.18f));
                break;

            case "Dian":  // 点：短小、细一点
                si.kind = "DS";
                si.thickness = 0.058f;
                si.pts.Add(new Vector2(0.42f, 0.62f));
                si.pts.Add(new Vector2(0.52f, 0.50f));
                break;

            case "Zhe":   // 折：三点折线（拐角会自动接上）
                si.kind = "Z";
                si.pts.Add(new Vector2(0.22f, 0.70f));
                si.pts.Add(new Vector2(0.72f, 0.70f));
                si.pts.Add(new Vector2(0.72f, 0.26f));
                break;

            default:      // 自由斜线：随便给一条，拖成想要的角度
                si.kind = "D";
                si.pts.Add(new Vector2(0.30f, 0.70f));
                si.pts.Add(new Vector2(0.70f, 0.30f));
                break;
        }

        strokes.Add(si);
        selectedStroke = strokes.Count - 1;
        status = $"已添加一笔（{kind}）—— 拖圆点调整位置，或在下面对话框里精确改坐标。";
    }

    /// <summary>选中笔画的顶点数值编辑（精确输入 + 增删点）</summary>
    void DrawPointEditor()
    {
        if (selectedStroke < 0 || selectedStroke >= strokes.Count) return;

        var s = strokes[selectedStroke];
        EditorGUILayout.LabelField($"顶点（笔画 {selectedStroke} · {s.kind}）", EditorStyles.boldLabel);

        // 插点/删点必须**等循环结束后**再做。
        // 在遍历中直接 Insert/RemoveAt 会让下一次 Repaint 的布局对不上，
        // 也会让 Unity 报 "Invalid GUILayout state"。
        int insertAfter = -1;
        int removeAt = -1;

        for (int i = 0; i < s.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"{i}", GUILayout.Width(18f));

            float nx = EditorGUILayout.FloatField(s.pts[i].x, GUILayout.Width(70f));
            float ny = EditorGUILayout.FloatField(s.pts[i].y, GUILayout.Width(70f));

            // 比较容差要够大。
            // 之前用 1e-5，但 FloatField 显示/回读会引入更大的浮点往返误差，
            // 于是"只是把窗口重绘一遍"也会被判定成"用户改了值"，
            // 结果把坐标一点点改坏（「人」的两笔顶点被改散就是这个原因）。
            if (Mathf.Abs(nx - s.pts[i].x) > 0.004f || Mathf.Abs(ny - s.pts[i].y) > 0.004f)
                s.pts[i] = new Vector2(Mathf.Clamp01(nx), Mathf.Clamp01(ny));

            if (i < s.Count - 1)
            {
                if (GUILayout.Button("插", EditorStyles.miniButton, GUILayout.Width(24f)))
                    insertAfter = i;
            }
            else
            {
                GUILayout.Space(24f);
            }

            if (s.Count > 2)
            {
                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                    removeAt = i;
            }
            else
            {
                GUILayout.Space(24f);
            }

            EditorGUILayout.EndHorizontal();
        }

        if (insertAfter >= 0)
        {
            Vector2 mid = (s.pts[insertAfter] + s.pts[insertAfter + 1]) * 0.5f;
            s.pts.Insert(insertAfter + 1, mid);
            status = $"已在顶点 {insertAfter} 与 {insertAfter + 1} 之间插入新顶点。";
        }

        if (removeAt >= 0)
        {
            s.pts.RemoveAt(removeAt);
            status = $"已删除顶点 {removeAt}。";
        }

        strokes[selectedStroke] = s;
    }

    void DrawChecks()
    {
        if (selectedStroke < 0 || selectedStroke >= strokes.Count) return;
        var s = strokes[selectedStroke];

        var warns = new List<string>();

        // 选中笔画的相邻顶点间距
        for (int i = 0; i < s.Count - 1; i++)
        {
            float d = Vector2.Distance(s.pts[i], s.pts[i + 1]);
            if (d < 0.06f)
                warns.Add($"顶点 {i}→{i + 1} 距离过近（{d:0.00}），会挤出小疙瘩");
        }

        // 同方向的平行笔画间距
        var hys = new List<float>();
        var vxs = new List<float>();
        foreach (var st in strokes)
        {
            if (st.Count < 2) continue;
            if (st.kind == "H") hys.Add(st.pts[0].y);
            if (st.kind == "V") vxs.Add(st.pts[0].x);
        }
        CheckGaps(hys, "横", warns);
        CheckGaps(vxs, "竖", warns);

        if (warns.Count == 0)
            EditorGUILayout.HelpBox("检查通过。", MessageType.Info);
        else
            EditorGUILayout.HelpBox(string.Join("\n", warns.ToArray()), MessageType.Warning);
    }

    /// <summary>
    /// 写入前的安全检查。
    ///
    /// 之前"写入源码"有个严重 bug：新代码插在 Save 前面、旧内容留下，
    /// 反复点击会把定义越堆越多（Save 从 47 涨到 54），字形出现"重影"。
    /// 修好之后仍然加这道校验 —— 一旦发现某个字有多个 Save 就拒绝写入，
    /// 避免把已经损坏的文件继续改坏。
    /// </summary>
    bool TryLocateSingleSave(string text, string ch, out int saveStart)
    {
        saveStart = -1;

        var ms = System.Text.RegularExpressions.Regex.Matches(
            text, "Save\\(\"" + System.Text.RegularExpressions.Regex.Escape(ch) + "\"\\)");

        if (ms.Count == 0)
        {
            // 敌人字库里还没画过的字 —— 源码里当然没有 Save，这时应该**追加**一条定义。
            //
            // 原来这里只提示"请用复制代码手动粘贴"，等于新字永远写不进去。
            // 敌人字库一开始是空的，所以这个分支是**主路径**，不是异常情况。
            if (glyphSet == HanziRenderer.SetEnemy)
                return TryAppendNewEnemyGlyph(text, ch, out saveStart);

            status = $"源码里找不到 Save(\"{ch}\")，请用「复制代码」手动粘贴。";
            return false;
        }

        if (ms.Count > 1)
        {
            status = $"⚠️ 源码里 Save(\"{ch}\") 出现了 {ms.Count} 次 —— 文件已有重复定义。" +
                     "请先用「撤销改动」或让我用 git 恢复 HanziRenderer.cs，不要继续写入。";
            return false;
        }

        saveStart = ms[0].Index;
        return true;
    }

    /// <summary>
    /// 在 EnemyGlyphs.cs 的"画字区"末尾追加一条新定义。
    ///
    /// 插入点靠那两个中文注释标记：
    ///     // （先空着 —— 你在字形编辑器里画，画一个生效一个）
    ///     sets[buildingSet] = d;
    /// 在标记行之前插入，保证新代码落在 BuildEnemyDefinitions() 里面、
    /// 而且在 sets[...] = d 之前（不然刚加的不会被收进字库）。
    /// </summary>
    bool TryAppendNewEnemyGlyph(string text, string ch, out int saveStart)
    {
        saveStart = -1;
        const string marker = "sets[buildingSet] = d;";

        int markerAt = text.LastIndexOf(marker, System.StringComparison.Ordinal);
        if (markerAt < 0)
        {
            status = "EnemyGlyphs.cs 里找不到插入标记（sets[buildingSet] = d;），" +
                     "请用「复制代码」手动粘贴。";
            return false;
        }

        string code = BuildCode() + $"\n        Save(\"{ch}\");\n";

        // 写盘前自检 —— 这是最后一道防线。
        //
        // 教训：上一版为了"让新字能写进去"就无条件追加，
        // 结果把一段语法非法的代码（点数和构造函数不匹配）写进了 EnemyGlyphs.cs，
        // 整个项目编译不过。
        // 文本编辑器的底线是"**写出去的代码必须能编译**"，
        // 而不是"我能不能找到插入点"。
        string problem;
        if (!LooksLikeValidCode(code, out problem))
        {
            status = $"生成的代码自检不过，**拒绝写入**：{problem}\n" +
                     "请点「复制代码」自己检查后粘贴。";
            Debug.LogError($"[字形编辑器] 拒绝写入「{ch}」：{problem}\n{code}");
            return false;
        }

        // 逐行加缩进。
        //
        // 上一版直接把拼接串插进去，导致第一行之后的所有行都顶到第 0 列
        // （C# 不介意缩进，但代码会变得很难读，而且下一次的"整块定位"
        //   是按缩进找构造器行的，缩进乱了就定位不到）。
        var indented = new StringBuilder();
        foreach (string line in code.Split('\n'))
            indented.Append(line.Length == 0 ? "\n" : "        " + line + "\n");

        int lineStart = text.LastIndexOf('\n', markerAt) + 1;
        text = text.Substring(0, lineStart) + indented + text.Substring(lineStart);

        string full = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName,
            SourceFileFor(glyphSet));

        // 备份（和别的写盘操作保持一致）
        System.IO.File.WriteAllText(full + ".bak", 
            System.IO.File.ReadAllText(full, Encoding.UTF8), new UTF8Encoding(false));

        System.IO.File.WriteAllText(full, text, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        status = $"「{ch}」是敌人字库里的新字，已**追加**到 {SourceFileFor(glyphSet)}。" +
                 "（备份在同名 .bak）";
        return false;   // 内容已经自己写完了，外层不用再替换
    }

    /// <summary>
    /// 粗检生成的代码是不是合法的构造器调用。
    ///
    /// 检查的是**最容易出错、也最容易静默写坏文件**的那一点：
    /// 点数和构造器名是否匹配。
    ///   H/V/D/DS/Z → 固定点数
    ///   P3/P4/P5   → 分别要求 3/4/5 个点
    ///   AddPoly    → 任意点数
    /// 这个错误 C# 编译时会报，但**报的时候文件已经被写坏了** ——
    /// 所以必须在写之前拦住。
    /// </summary>
    static bool LooksLikeValidCode(string code, out string problem)
    {
        problem = null;

        if (string.IsNullOrEmpty(code)) { problem = "生成的代码是空的"; return false; }

        foreach (string raw in code.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (!(line.StartsWith("H(") || line.StartsWith("V(") || line.StartsWith("D(") ||
                  line.StartsWith("DS(") || line.StartsWith("Z(") || line.StartsWith("P") ||
                  line.StartsWith("AddPoly(") || line.StartsWith("Save(")))
            {
                problem = $"出现不认识的语句：{line}";
                return false;
            }

            // P3/P4/P5 的点数必须对得上
            if (line.StartsWith("P") && line.Length > 2 && char.IsDigit(line[1]))
            {
                int want = line[1] - '0';
                int commas = 0;
                int open = line.IndexOf('{');
                int close = line.IndexOf('}');
                if (open >= 0 && close > open)
                {
                    foreach (char c in line.Substring(open, close - open))
                        if (c == ',') commas++;
                    // 每个点写成 "x, y"，所以逗号数 = 点数 * 2 - 1
                    int got = (commas + 1) / 2;
                    if (got != want)
                    {
                        problem = $"P{want} 需要 {want} 个点，但生成了 {got} 个 —— " +
                                  "点数和构造函数不匹配，会编译不过：" + line;
                        return false;
                    }
                }
            }
        }

        return true;
    }

    static void CheckGaps(List<float> vals, string name, List<string> warns)
    {
        if (vals.Count < 2) return;
        vals.Sort();
        for (int i = 0; i < vals.Count - 1; i++)
        {
            float gap = vals[i + 1] - vals[i];
            if (gap > 0.0001f && gap < MinGap)
                warns.Add($"{name}间距过小 {gap:0.00}（{vals[i]:0.00} → {vals[i + 1]:0.00}）");
        }
    }

    void DrawControls()
    {
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("拖圆点=移顶点", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("拖小方点=在此插顶点", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("右键圆点=删顶点", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("拖中段=整条平移  空白拖=平移画布", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("滚轮=缩放  Ctrl=关吸附", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Alt+拖=解除「竖必垂直」约束（可拖出角度）", EditorStyles.miniLabel);

        snap = EditorGUILayout.ToggleLeft("吸附到 0.02 网格", snap);
        showGrid = EditorGUILayout.ToggleLeft("显示参考网格", showGrid);

        GUILayout.Space(2f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("[ 上一个")) { selected = (selected - 1 + glyphs.Count) % glyphs.Count; LoadGlyph(); }
        if (GUILayout.Button("下一个 ]")) { selected = (selected + 1) % glyphs.Count; LoadGlyph(); }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("保存", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("应用（看效果）")) ApplyToRuntime();
        if (GUILayout.Button("放弃改动")) LoadGlyph();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("重新载入（从源码）")) ReloadFromSource();
        if (GUILayout.Button("复制代码")) CopyCode();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("写入源码")) WriteToSource();

        if (!string.IsNullOrEmpty(status))
            EditorGUILayout.HelpBox(status, MessageType.None);
    }

    // ============================================================
    //  画布
    // ============================================================

    Rect canvasRect;
    Vector2 canvasOrigin;
    bool originReady;

    void DrawCanvas()
    {
        EditorGUILayout.BeginVertical();

        canvasRect = GUILayoutUtility.GetRect(zoom + 40f, zoom + 40f,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(canvasRect, GUIContent.none, EditorStyles.helpBox);

        canvasOrigin = new Vector2(
            canvasRect.x + (canvasRect.width - zoom) * 0.5f + pan.x,
            canvasRect.y + (canvasRect.height - zoom) * 0.5f + pan.y);
        originReady = true;

        if (showGrid) DrawGrid();
        DrawStrokes();
        DrawInsertHints();
        HandleInput();

        GUILayout.Space(2f);
        var s = (selectedStroke >= 0 && selectedStroke < strokes.Count) ? strokes[selectedStroke] : default;
        string info = selectedStroke >= 0 && selectedStroke < strokes.Count
            ? $"选中笔画 {selectedStroke}（{s.kind}，{s.Count} 点）"
            : "未选中笔画";
        EditorGUILayout.LabelField($"当前字：{CurrentChar}    {info}", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    Vector2 ToScreen(float gx, float gy)
    {
        return new Vector2(
            canvasOrigin.x + gx * zoom,
            canvasOrigin.y + (1f - gy) * zoom);
    }

    Vector2 ToScreen(Vector2 g) { return ToScreen(g.x, g.y); }

    Vector2 ToGlyph(Vector2 screen)
    {
        return new Vector2(
            (screen.x - canvasOrigin.x) / zoom,
            1f - (screen.y - canvasOrigin.y) / zoom);
    }

    void DrawGrid()
    {
        Handles.BeginGUI();

        for (int i = 0; i < Grid.Length; i++)
        {
            bool frame = (i == 0 || i == Grid.Length - 1);
            Handles.color = frame ? new Color(0.45f, 0.55f, 0.7f, 0.85f)
                                  : new Color(0.35f, 0.4f, 0.5f, 0.45f);

            Vector2 a = ToScreen(Grid[i], 0f), b = ToScreen(Grid[i], 1f);
            Handles.DrawLine(new Vector3(a.x, a.y), new Vector3(b.x, b.y));

            Vector2 c = ToScreen(0f, Grid[i]), d = ToScreen(1f, Grid[i]);
            Handles.DrawLine(new Vector3(c.x, c.y), new Vector3(d.x, d.y));
        }

        Vector2 p0 = ToScreen(0.10f, 0.90f), p1 = ToScreen(0.90f, 0.10f);
        Handles.DrawSolidRectangleWithOutline(
            new Rect(p0.x, p0.y, p1.x - p0.x, p1.y - p0.y),
            new Color(1f, 1f, 1f, 0.03f), new Color(0.45f, 0.55f, 0.7f, 0.6f));

        Handles.EndGUI();
    }

    void DrawStrokes()
    {
        Handles.BeginGUI();

        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            if (s.Count < 2) continue;

            bool sel = (i == selectedStroke);
            Handles.color = sel ? new Color(1f, 0.75f, 0.2f) : new Color(0.95f, 0.45f, 0.4f);

            float w = Mathf.Max(3f, s.thickness * zoom * 0.85f);

            var pts3 = new Vector3[s.Count];
            for (int k = 0; k < s.Count; k++)
            {
                Vector2 p = ToScreen(s.pts[k]);
                pts3[k] = new Vector3(p.x, p.y, 0f);
            }
            Handles.DrawAAPolyLine(w, pts3);
        }

        // 顶点手柄
        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            bool sel = (i == selectedStroke);
            Handles.color = sel ? new Color(1f, 0.9f, 0.3f) : new Color(0.9f, 0.6f, 0.6f, 0.8f);

            for (int k = 0; k < s.Count; k++)
            {
                Vector2 p = ToScreen(s.pts[k]);
                Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward,
                    sel ? HandleRadius : HandleRadius - 1.5f);
            }
        }

        Handles.EndGUI();
    }

    /// <summary>在每段中点画一个"插入提示"小方点</summary>
    void DrawInsertHints()
    {
        if (selectedStroke < 0 || selectedStroke >= strokes.Count) return;
        var s = strokes[selectedStroke];
        if (s.Count < 2) return;

        Handles.BeginGUI();
        Handles.color = new Color(0.5f, 0.9f, 1f, 0.55f);

        for (int i = 0; i < s.Count - 1; i++)
        {
            Vector2 mid = (s.pts[i] + s.pts[i + 1]) * 0.5f;
            Vector2 p = ToScreen(mid);
            float r = InsertRadius;
            Handles.DrawSolidRectangleWithOutline(
                new Rect(p.x - r, p.y - r, r * 2f, r * 2f),
                new Color(0.5f, 0.9f, 1f, 0.5f), new Color(0.5f, 0.9f, 1f, 0.9f));
        }

        Handles.EndGUI();
    }

    // ============================================================
    //  交互
    // ============================================================

    void HandleInput()
    {
        Event e = Event.current;
        if (e == null || !originReady) return;

        if (e.type == EventType.ScrollWheel && canvasRect.Contains(e.mousePosition))
        {
            zoom = Mathf.Clamp(zoom - e.delta.y * 20f, 160f, 1400f);
            e.Use(); Repaint(); return;
        }

        // --- 命中测试 ---
        int hitStroke = -1, hitPoint = -1;
        float bestD = 14f;

        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            for (int k = 0; k < s.Count; k++)
            {
                float d = Vector2.Distance(ToScreen(s.pts[k]), e.mousePosition);
                if (d < bestD) { bestD = d; hitStroke = i; hitPoint = k; }
            }
        }

        // 插入点提示（只对选中的笔画）
        int insStroke = -1, insAfter = -1;
        if (selectedStroke >= 0 && selectedStroke < strokes.Count)
        {
            var s = strokes[selectedStroke];
            float bd = InsertRadius * 2.2f;
            for (int i = 0; i < s.Count - 1; i++)
            {
                Vector2 mid = (s.pts[i] + s.pts[i + 1]) * 0.5f;
                float d = Vector2.Distance(ToScreen(mid), e.mousePosition);
                if (d < bd) { bd = d; insStroke = selectedStroke; insAfter = i; }
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (hitPoint >= 0)
            {
                selectedStroke = hitStroke;
                dragStroke = hitStroke; dragPoint = hitPoint;
                dragging = true;
                e.Use(); Repaint(); return;
            }

            if (insStroke >= 0)
            {
                var s = strokes[insStroke];
                Vector2 mid = (s.pts[insAfter] + s.pts[insAfter + 1]) * 0.5f;
                s.pts.Insert(insAfter + 1, mid);
                strokes[insStroke] = s;
                selectedStroke = insStroke;
                dragStroke = insStroke; dragPoint = insAfter + 1;
                dragging = true;
                status = "已插入顶点，直接拖动它即可折出拐角。";
                e.Use(); Repaint(); return;
            }

            int seg = StrokeUnderMouse(e.mousePosition);
            if (seg >= 0)
            {
                selectedStroke = seg;
                dragStroke = seg; dragPoint = -2;
                dragging = true;
                dragLast = ToGlyph(e.mousePosition);
                e.Use(); Repaint(); return;
            }

            dragStroke = -1; dragPoint = -3; dragging = true;
            e.Use(); return;
        }

        // 右键删点
        if (e.type == EventType.MouseDown && e.button == 1 && hitPoint >= 0)
        {
            var s = strokes[hitStroke];
            if (s.Count > 2)
            {
                s.pts.RemoveAt(hitPoint);
                strokes[hitStroke] = s;
                selectedStroke = hitStroke;
                status = $"已删除顶点 {hitPoint}。";
            }
            else
            {
                status = "一条笔画至少要保留 2 个顶点。";
            }
            e.Use(); Repaint(); return;
        }

        if (e.type == EventType.MouseDrag && dragging)
        {
            if (dragPoint == -3)
            {
                pan += e.delta;
                e.Use(); Repaint(); return;
            }

            Vector2 g = ToGlyph(e.mousePosition);
            bool noSnap = e.control || e.command;

            if (dragPoint >= 0 && dragStroke >= 0 && dragStroke < strokes.Count)
            {
                MovePoint(dragStroke, dragPoint, g, noSnap);
                e.Use(); Repaint(); return;
            }

            if (dragPoint == -2 && dragStroke >= 0 && dragStroke < strokes.Count)
            {
                MoveWholeStroke(dragStroke, g, noSnap);
                e.Use(); Repaint(); return;
            }
        }

        if (e.type == EventType.MouseUp)
        {
            if (dragging) { dragging = false; dragStroke = -1; dragPoint = -1; }
            Repaint(); return;
        }

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.LeftBracket) { selected = (selected - 1 + glyphs.Count) % glyphs.Count; LoadGlyph(); e.Use(); Repaint(); }
            if (e.keyCode == KeyCode.RightBracket) { selected = (selected + 1) % glyphs.Count; LoadGlyph(); e.Use(); Repaint(); }
        }
    }

    int StrokeUnderMouse(Vector2 mouse)
    {
        float best = 12f;
        int found = -1;

        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            for (int k = 0; k < s.Count - 1; k++)
            {
                float d = DistToSegment(mouse, ToScreen(s.pts[k]), ToScreen(s.pts[k + 1]));
                if (d < best) { best = d; found = i; }
            }
        }
        return found;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    float Snap(float v, bool noSnap)
    {
        if (!snap || noSnap) return Mathf.Clamp01(v);
        return Mathf.Clamp01(Mathf.Round(v / SnapStep) * SnapStep);
    }

    /// <summary>
    /// 移动顶点。
    ///
    /// 约束规则：
    ///   - 横(H) / 竖(V)：**所有**顶点共享同一个 y（或 x），
    ///     所以拖任何一点都只改另一个轴 —— 不会被拖歪。
    ///   - 折(Z) / 斜(D,DS)：顶点完全自由。
    ///   - **按住 Alt 拖动 = 临时解除 H/V 约束**，
    ///     可以把竖拖斜、把横拖斜（比如「丁」的竖钩就是要斜一点）。
    ///     一旦真的拖出了角度，这条笔画会自动转成自由折线，
    ///     否则你松手后它又会被约束拉回垂直。
    /// </summary>
    void MovePoint(int si, int pi, Vector2 g, bool noSnap)
    {
        var s = strokes[si];
        if (pi < 0 || pi >= s.Count) return;

        float nx = Snap(g.x, noSnap);
        float ny = Snap(g.y, noSnap);

        // Alt = 解除"竖必须垂直/横必须水平"的约束，允许拖出角度
        bool freeAngle = Event.current != null && Event.current.alt;

        if (freeAngle && (s.kind == "H" || s.kind == "V"))
        {
            // 先自由改这个顶点
            s.pts[pi] = new Vector2(nx, ny);

            // 再检查是否真的出现角度；有角度就转成自由折线，
            // 否则后面的约束会把它拉回水平/垂直
            if (HasAngle(s))
            {
                s.kind = "Z";
                status = "已解除方向约束：这条笔画现在是自由折线，可以任意拖角度。";
            }

            strokes[si] = s;
            return;
        }

        switch (s.kind)
        {
            case "H":
                // 只改 x；y 同步到所有顶点（保持水平）
                s.pts[pi] = new Vector2(nx, s.pts[0].y);
                break;

            case "V":
                s.pts[pi] = new Vector2(s.pts[0].x, ny);
                break;

            default:
                s.pts[pi] = new Vector2(nx, ny);
                break;
        }

        strokes[si] = s;
    }

    /// <summary>这条笔画的顶点是不是已经不在同一条水平/垂直线上</summary>
    static bool HasAngle(HanziRenderer.StrokeInfo s)
    {
        if (s.Count < 2) return false;

        if (s.kind == "H")
        {
            for (int i = 1; i < s.Count; i++)
                if (Mathf.Abs(s.pts[i].y - s.pts[0].y) > 0.005f) return true;
            return false;
        }

        if (s.kind == "V")
        {
            for (int i = 1; i < s.Count; i++)
                if (Mathf.Abs(s.pts[i].x - s.pts[0].x) > 0.005f) return true;
            return false;
        }

        return true;   // 已经是自由折线
    }

    void MoveWholeStroke(int si, Vector2 g, bool noSnap)
    {
        var s = strokes[si];
        Vector2 delta = g - dragLast;
        dragLast = g;

        for (int i = 0; i < s.Count; i++)
            s.pts[i] += delta;

        s = ClampStroke(s);
        strokes[si] = s;
    }

    static HanziRenderer.StrokeInfo ClampStroke(HanziRenderer.StrokeInfo s)
    {
        float minx = 2f, maxx = -1f, miny = 2f, maxy = -1f;
        for (int i = 0; i < s.Count; i++)
        {
            minx = Mathf.Min(minx, s.pts[i].x); maxx = Mathf.Max(maxx, s.pts[i].x);
            miny = Mathf.Min(miny, s.pts[i].y); maxy = Mathf.Max(maxy, s.pts[i].y);
        }

        float dx = 0f, dy = 0f;
        if (minx < 0f) dx = -minx;
        if (maxx > 1f) dx = 1f - maxx;
        if (miny < 0f) dy = -miny;
        if (maxy > 1f) dy = 1f - maxy;

        if (Mathf.Abs(dx) > 1e-6f || Mathf.Abs(dy) > 1e-6f)
            for (int i = 0; i < s.Count; i++)
                s.pts[i] += new Vector2(dx, dy);

        return s;
    }

    void InsertPointOnLongestSegment(int si)
    {
        var s = strokes[si];
        if (s.Count < 2) return;

        int best = 0; float bestLen = -1f;
        for (int i = 0; i < s.Count - 1; i++)
        {
            float d = Vector2.Distance(s.pts[i], s.pts[i + 1]);
            if (d > bestLen) { bestLen = d; best = i; }
        }

        Vector2 mid = (s.pts[best] + s.pts[best + 1]) * 0.5f;
        s.pts.Insert(best + 1, mid);
        strokes[si] = s;
        status = $"已在最长的一段（{best}→{best + 1}）中间插入顶点，拖动它即可折出拐角。";
    }

    // ============================================================
    //  输出
    // ============================================================

    /// <summary>
    /// 生成写回源码的代码。
    ///
    /// 2 点的横/竖用紧凑的 H/V 写法；其余按点数用 P3/P4/P5。
    /// 超过 5 点的情况（拖太多点）会提示手动处理 —— 正常做字不会用到。
    /// </summary>
    string BuildCode()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"        // {CurrentChar}");

        foreach (var s in strokes)
        {
            if (s.Count < 2) continue;

            if (s.Count == 2 && s.kind == "H")
            {
                sb.AppendLine($"        H(L, {s.pts[0].x:0.00}f, {s.pts[1].x:0.00}f, {s.pts[0].y:0.00}f);");
            }
            else if (s.Count == 2 && s.kind == "V")
            {
                sb.AppendLine($"        V(L, {s.pts[0].x:0.00}f, {s.pts[0].y:0.00}f, {s.pts[1].y:0.00}f);");
            }
            else if (s.Count == 2)
            {
                string fn = (s.kind == "DS") ? "DS" : "D";
                sb.AppendLine($"        {fn}(L, {s.pts[0].x:0.00}f, {s.pts[0].y:0.00}f, " +
                              $"{s.pts[1].x:0.00}f, {s.pts[1].y:0.00}f);");
            }
            else if (s.Count <= 5)
            {
                var parts = new StringBuilder();
                for (int i = 0; i < s.Count; i++)
                {
                    if (i > 0) parts.Append(", ");
                    parts.Append($"{s.pts[i].x:0.00}f, {s.pts[i].y:0.00}f");
                }
                sb.AppendLine($"        P{s.Count}(L, {s.thickness:0.000}f, {parts});");
            }
            else
            {
                sb.AppendLine($"        // 注意：这条笔画有 {s.Count} 个点，超过了 P5。" +
                              "请手动用 AddPoly(...) 写，或删掉几个点。");
                var parts = new StringBuilder();
                for (int i = 0; i < s.Count; i++)
                {
                    if (i > 0) parts.Append(", ");
                    parts.Append($"new Vector2({s.pts[i].x:0.00}f, {s.pts[i].y:0.00}f)");
                }
                sb.AppendLine($"        AddPoly(L, {s.thickness:0.000}f, new List<Vector2> {{ {parts} }});");
            }
        }

        sb.Append($"        Save(\"{CurrentChar}\");");
        return sb.ToString();
    }

    void CopyCode()
    {
        EditorGUIUtility.systemCopyBuffer = BuildCode();
        status = $"「{CurrentChar}」的代码已复制。注意：含 3 点以上的笔画会用 P(...) 写法，" +
                 "需要 HanziRenderer 里有对应的 P 构造器（已提供）。";
    }

    void ApplyToRuntime()
    {
        if (HanziRenderer.ApplyStrokes(CurrentChar, strokes, glyphSet))
        {
            foreach (var r in Object.FindObjectsOfType<HanziRenderer>())
                r.SetCharacter(r.character);

            status = $"「{CurrentChar}」已在运行时生效（场景里的字会立刻变）。";
            Repaint();
        }
    }

    /// <summary>
    /// 丢弃内存里的改动，从源码的 BuildDefinitions() 重新读一遍。
    ///
    /// 用途：拖乱了 / 不小心加了重复笔画（"重影"）之后，
    /// 点一下就能退回文件里的版本，不用去动 git。
    /// </summary>
    void ReloadFromSource()
    {
        HanziRenderer.RebuildAll();
        LoadGlyph();

        foreach (var r in Object.FindObjectsOfType<HanziRenderer>())
            r.SetCharacter(r.character);

        status = "已从源码重新载入全部字形，内存里的改动已丢弃。";
        Repaint();
    }

    void WriteToSource()
    {
        string ch = CurrentChar;
        string full = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName,
            SourceFileFor(glyphSet));

        if (!System.IO.File.Exists(full))
        {
            status = "找不到 HanziRenderer.cs，无法写入。请用「复制代码」。";
            return;
        }

        // 文件统一用 \n，避免 \r\n 混进来让行号算错
        string text = System.IO.File.ReadAllText(full, Encoding.UTF8).Replace("\r\n", "\n");

        // ---- 1) 找到这个字所属的"定义块" ----
        //
        // 一条完整定义 = 若干构造器行 + 结尾的 Save("X")，前面可能还有注释行。
        // 必须**整块替换**，否则新旧内容会并存。
        //
        // 之前就是这里写错了：只替换到 Save 那一行的行首，新代码插在 Save **前面**，
        // 旧的 Save 与后续内容被留在原地 —— 反复点「写入源码」就越堆越多，
        // 最后 HanziRenderer.cs 里出现重复的 Save 和构造器，字形全乱。
        int saveStart;
        if (!TryLocateSingleSave(text, ch, out saveStart)) return;
        int saveLineEnd = text.IndexOf('\n', saveStart);
        if (saveLineEnd < 0) saveLineEnd = text.Length;

        // 往前吃构造器行与本字自己的注释行
        //
        // 判据：这一行的第一个非空白字符是字母、且是已知的构造器名。
        // 但注释行也要吃 —— 只吃**紧贴构造器上方、且不含其它字名的**那种，
        // 保证不会误删上一个字的代码。
        int blockStart = text.LastIndexOf('\n', saveStart) + 1;
        int guard = 0;

        while (blockStart > 0 && guard++ < 200)
        {
            int prevEnd = blockStart - 1;                        // 指向 \n
            if (prevEnd <= 0) break;
            int prevStart = text.LastIndexOf('\n', prevEnd - 1) + 1;
            string prevLine = text.Substring(prevStart, prevEnd - prevStart).Trim();

            if (IsCtorLine(prevLine))
            {
                blockStart = prevStart;
                continue;
            }

            // 本字自己的注释（形如 "// 人" 或 "// 人：..."）：以 // 开头、且包含本字
            if (prevLine.StartsWith("//") && prevLine.Contains(ch) &&
                !prevLine.Contains("Save(") && prevLine.Length < 40)
            {
                blockStart = prevStart;
                continue;
            }

            break;
        }

        string newBlock = BuildCode() + "\n";
        text = text.Substring(0, blockStart) + newBlock + text.Substring(saveLineEnd + 1);

        System.IO.File.WriteAllText(full, text, new UTF8Encoding(false));

        HanziRenderer.RebuildAll();
        AssetDatabase.Refresh();

        // 写完重新载入，让窗口里的数据和文件一致 ——
        // 否则下次再点「写入源码」又会拿着旧数据去覆盖。
        LoadGlyph();

        status = $"已写入 {ch}。Unity 会重新编译（窗口可能被重置，改动已存盘）。";
    }

    /// <summary>这一行是不是字形构造器调用（H/V/D/DS/Z/P3/P4/P5/AddPoly）</summary>
    static bool IsCtorLine(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed)) return false;

        string[] names = { "H(L,", "V(L,", "D(L,", "DS(L,", "Z(L,",
                           "P3(L,", "P4(L,", "P5(L,", "AddPoly(L," };
        foreach (string n in names)
            if (trimmed.StartsWith(n)) return true;

        return false;
    }
}
#endif
