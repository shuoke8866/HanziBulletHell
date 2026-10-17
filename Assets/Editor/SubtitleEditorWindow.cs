#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字幕编辑器：图形化地改所有玩家可见的文案。
///
/// ## 它能做什么
///
///   · 按情境分组列出全部字幕（操作反馈 / BOSS 台词 / 提示条 / 面板 / 教程）
///   · 每条都能直接编辑
///   · **实时预览**：面板底部按当前输入渲染出"实际会显示成什么样"
///   · 搜索过滤
///   · 一键写回 GameText.cs（带备份）
///
/// ## 它不能做什么（重要）
///
/// **游戏运行时改不了。** C# 的字符串是编译进程序集的，
/// 想在打包后实时改文案得改架构（外部 JSON + 运行时读取），
/// 对现在这个规模不值得 —— 而且 WebGL 下改文件也没法热重载。
///
/// 所以工作流是：在这里改 → 保存 → Unity 自动重编译 → Play 看效果。
/// 整个循环几秒钟，够快了。
///
/// 菜单：Tools/字幕编辑器
/// </summary>
public class SubtitleEditorWindow : EditorWindow
{
    const string SourcePath = "Assets/Scripts/GameText.cs";

    class Entry
    {
        public string group;        // 类名，如 Toast / Boss / Hint
        public string key;          // 方法名或字段名
        public string text;         // 当前文案
        public string original;     // 载入时的原文案（用来判断有没有改过）
        public bool isArray;        // 教程那种 string[] 的条目
        public int arrayIndex;

        /// <summary>这条字幕属于哪个房间（只在教程步骤里有意义，其余为 null）</summary>
        public string room;

        /// <summary>源码里这一行房间名的原文（保存时用它定位替换）</summary>
        public string roomOriginal;
    }

    readonly List<Entry> entries = new List<Entry>();
    Vector2 scroll;
    string filter = "";
    bool dirty;

    [MenuItem("Tools/字幕编辑器")]
    public static void Open()
    {
        var w = GetWindow<SubtitleEditorWindow>("字幕编辑器");
        w.minSize = new Vector2(720f, 520f);
        w.Reload();
    }

    // ============================================================
    //  载入
    // ============================================================

    void Reload()
    {
        entries.Clear();
        dirty = false;

        if (!File.Exists(SourcePath))
        {
            Debug.LogError($"[字幕] 找不到 {SourcePath}");
            return;
        }

        string src = File.ReadAllText(SourcePath, Encoding.UTF8);

        // 逐行扫：遇到 "public static class X" 记组名，
        // 遇到 "public static string 名字(...)" 或 "public static string 名字" 记一条，
        // 双引号里的内容就是文案。
        string group = "(顶层)";
        string currentKey = null;
        int arrayDepth = 0;
        int arrayIdx = 0;

        foreach (string raw in src.Split('\n'))
        {
            string line = raw.Trim();

            var mg = Regex.Match(line, @"public static class (\w+)");
            if (mg.Success) { group = mg.Groups[1].Value; continue; }

            // 方法或字段名
            var mk = Regex.Match(line, @"public static (?:string|string\[\]) (\w+)\s*[\(\{]");
            if (mk.Success)
            {
                currentKey = mk.Groups[1].Value;
                arrayIdx = 0;
                continue;
            }

            // 文案字面量
            foreach (Match ms in Regex.Matches(raw, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string lit = ms.Groups[1].Value;

                // 跳过纯占位/转义、以及明显不是文案的
                if (lit.Length == 0) continue;
                if (currentKey == null) continue;

                bool hasCjk = Regex.IsMatch(lit, @"[\u4e00-\u9fff]");
                bool hasFormat = lit.Contains("{");
                if (!hasCjk && !hasFormat) continue;

                entries.Add(new Entry
                {
                    group = group,
                    key = currentKey,
                    text = lit,
                    original = lit,
                    isArray = arrayDepth > 0,
                    arrayIndex = arrayIdx++
                });
            }
        }

        // 解析教程步骤的房间归属（new CoachStep("房间名", ...) 这种写法）
        ParseRooms(src);

        Debug.Log($"[字幕] 已载入 {entries.Count} 条文案");
    }

    // ============================================================
    //  房间归属
    // ============================================================

    /// <summary>
    /// 解析教程步骤里每条字幕属于哪个房间。
    ///
    /// 认两种写法：
    ///     new CoachStep("Room2_Collect", "文案")       ← 推荐，按房间播
    ///     new CoachStep(CoachTrigger.Xxx, "文案")       ← 旧写法，没有房间
    ///
    /// 做法：找出所有 `new CoachStep(` 的位置，看它后面第一个字符串字面量
    /// 是不是以 Room 开头；是就把「该步骤后面到下一次 new CoachStep 之间」
    /// 的全部文案条目都标上这个房间。
    /// </summary>
    void ParseRooms(string src)
    {
        // 找所有步骤起点
        var stepStarts = new List<int>();
        int at = 0;
        while ((at = src.IndexOf("new CoachStep(", at, System.StringComparison.Ordinal)) >= 0)
        {
            stepStarts.Add(at);
            at += 4;
        }

        if (stepStarts.Count == 0) return;

        int marked = 0;

        for (int i = 0; i < stepStarts.Count; i++)
        {
            int from = stepStarts[i];
            int to = (i + 1 < stepStarts.Count) ? stepStarts[i + 1] : src.Length;

            // 这一步骤块内的第一个字面量
            var lm = System.Text.RegularExpressions.Regex.Match(
                src.Substring(from, to - from), "\"((?:[^\"\\\\]|\\\\.)*)\"");

            if (!lm.Success) continue;

            string first = lm.Groups[1].Value;
            if (!first.StartsWith("Room")) continue;      // 不是按房间播的步骤

            // 把这一块里所有文案条目标上房间
            foreach (Entry e in entries)
            {
                if (e.group != "CoachStep" && e.group != "(顶层)")
                {
                    // 教程步骤在 GameText 顶层（不在某个 static class 里），
                    // 所以 group 一般是 (顶层)。这里不做强约束。
                }

                if (e.text == first) { e.room = first; e.roomOriginal = first; marked++; }
            }
        }

        Debug.Log($"[字幕] 已识别 {marked} 条带房间的字幕");
    }

    /// <summary>
    /// 收集项目里所有房间名，给下拉框用。
    ///
    /// 从**场景文件**里扫（Room 对象名），而不是硬编码 ——
    /// 以后你改房间名、加房间，下拉框会自动跟着变。
    /// </summary>
    static List<string> CollectRoomNames()
    {
        var names = new List<string>();

        string[] scenes = Directory.GetFiles("Assets/Scenes", "*.unity");

        foreach (string sc in scenes)
        {
            string[] lines;
            try { lines = File.ReadAllLines(sc); }
            catch { continue; }

            foreach (string line in lines)
            {
                string s = line.Trim();
                if (!s.StartsWith("m_Name: Room")) continue;

                string nm = s.Substring("m_Name: ".Length).Trim();
                if (nm.Length == 0) continue;
                if (names.Contains(nm)) continue;

                names.Add(nm);
            }
        }

        names.Sort();
        return names;
    }

    /// <summary>房间名缓存（避免每帧扫场景文件）</summary>
    List<string> roomNames;

    List<string> RoomNames
    {
        get
        {
            if (roomNames == null) roomNames = CollectRoomNames();
            return roomNames;
        }
    }

    // ============================================================
    //  绘制
    // ============================================================

    void OnGUI()
    {
        DrawToolbar();

        if (entries.Count == 0)
        {
            EditorGUILayout.HelpBox("没有载入到文案。点上面的「重新载入」试试。", MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        string lastGroup = null;

        foreach (Entry e in entries)
        {
            if (!Matches(e)) continue;

            if (e.group != lastGroup)
            {
                lastGroup = e.group;
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("【" + GroupTitle(e.group) + "】", EditorStyles.boldLabel);
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(e.key, GUILayout.Width(150f));

            // 教程步骤：显示房间下拉框
            DrawRoomPicker(e);

            EditorGUI.BeginChangeCheck();
            string v = EditorGUILayout.TextField(e.text);
            if (EditorGUI.EndChangeCheck())
            {
                e.text = v;
                dirty = true;
            }

            if (e.text != e.original)
            {
                if (GUILayout.Button("撤销", GUILayout.Width(52f))) e.text = e.original;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        DrawPreview();
    }

    /// <summary>
    /// 房间下拉框。只有「按房间播」的教程步骤才会显示。
    ///
    /// 玩家要的就是这个：**选择这条字幕在哪个房间输出**。
    /// </summary>
    void DrawRoomPicker(Entry e)
    {
        bool isRoomStep = e.roomOriginal != null;

        // 不是按房间播的条目（Toast / Boss / Panel 等）→ 不给房间框，但留同样的宽度
        if (!isRoomStep)
        {
            GUILayout.Space(170f);
            return;
        }

        List<string> opts = RoomNames;
        int cur = opts.IndexOf(e.room);

        // 当前房间名不在列表里（比如房间被改名了）→ 放在最后一项，避免丢数据
        bool orphan = cur < 0 && !string.IsNullOrEmpty(e.room);
        if (orphan) opts = new List<string>(opts) { e.room };

        int shown = orphan ? opts.Count - 1 : Mathf.Max(0, cur);

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(shown, opts.ToArray(), GUILayout.Width(170f));
        if (EditorGUI.EndChangeCheck())
        {
            e.room = opts[picked];
            dirty = true;
        }

        if (orphan)
            EditorGUILayout.LabelField("⚠", GUILayout.Width(20f));
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("重新载入", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Reload();

        GUILayout.Space(8f);
        GUILayout.Label("搜索", EditorStyles.miniLabel, GUILayout.Width(34f));
        filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField, GUILayout.Width(180f));

        GUILayout.FlexibleSpace();

        GUI.enabled = dirty;
        if (GUILayout.Button(dirty ? "保存到 GameText.cs *" : "保存到 GameText.cs",
                             EditorStyles.toolbarButton, GUILayout.Width(170f)))
            Save();
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>底部预览：把改动过的条目按"游戏里会显示成什么样"渲染一遍</summary>
    void DrawPreview()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("预览（改动过的条目）", EditorStyles.boldLabel);

        int shown = 0;
        foreach (Entry e in entries)
        {
            if (e.text == e.original) continue;
            if (shown++ >= 6) { EditorGUILayout.LabelField("…还有更多改动"); break; }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(e.group + "." + e.key, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(e.original + "   →   " + e.text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        if (shown == 0)
            EditorGUILayout.LabelField("（还没改任何东西）", EditorStyles.miniLabel);
    }

    bool Matches(Entry e)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        string f = filter.ToLowerInvariant();
        return e.key.ToLowerInvariant().Contains(f)
            || e.text.ToLowerInvariant().Contains(f)
            || e.group.ToLowerInvariant().Contains(f);
    }

    static string GroupTitle(string g)
    {
        switch (g)
        {
            case "Toast": return "操作反馈（拾取 / 换字 / 房间）";
            case "Boss": return "BOSS 台词";
            case "Hint": return "常驻提示条";
            case "Panel": return "面板标题与说明";
            case "Tutorial": return "新手教程提示";
            case "Debug": return "诊断提示";
            default: return g;
        }
    }

    // ============================================================
    //  保存
    // ============================================================

    void Save()
    {
        if (!File.Exists(SourcePath))
        {
            Debug.LogError($"[字幕] 找不到 {SourcePath}");
            return;
        }

        string src = File.ReadAllText(SourcePath, Encoding.UTF8);
        string backup = SourcePath + ".bak";
        File.WriteAllText(backup, src, new UTF8Encoding(false));

        int replaced = 0;
        int missed = 0;
        int named = 0;

        foreach (Entry e in entries)
        {
            if (e.text == e.original) continue;

            // 用"原文案"做定位替换。原文案在文件里是唯一的（同一句不会写两遍），
            // 所以直接替换一次即可。
            string needle = "\"" + e.original + "\"";
            int idx = src.IndexOf(needle, System.StringComparison.Ordinal);

            if (idx < 0) { missed++; continue; }

            src = src.Substring(0, idx) + "\"" + e.text + "\"" + src.Substring(idx + needle.Length);
            replaced++;
        }

        File.WriteAllText(SourcePath, src, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        // 写回房间名
        foreach (Entry e in entries)
        {
            if (e.roomOriginal == null || e.room == e.roomOriginal) continue;

            string needle = "new CoachStep(\"" + e.roomOriginal + "\"";
            int idx = src.IndexOf(needle, System.StringComparison.Ordinal);
            if (idx < 0) { missed++; continue; }

            string repl = "new CoachStep(\"" + e.room + "\"";
            src = src.Substring(0, idx) + repl + src.Substring(idx + needle.Length);
            named++;
        }

        File.WriteAllText(SourcePath, src, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        foreach (Entry e in entries) e.original = e.text;
        foreach (Entry e in entries) e.roomOriginal = e.room;
        dirty = false;

        Debug.Log($"[字幕] 已保存：替换 {replaced} 条" +
                  (missed > 0 ? $"，{missed} 条没找到（可能已被别处改过）" : "") +
                  $"。备份在 {backup}");

        Repaint();
    }
}
#endif
