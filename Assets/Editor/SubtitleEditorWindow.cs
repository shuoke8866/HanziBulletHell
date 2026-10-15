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

        Debug.Log($"[字幕] 已载入 {entries.Count} 条文案");
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

        foreach (Entry e in entries) e.original = e.text;
        dirty = false;

        Debug.Log($"[字幕] 已保存：替换 {replaced} 条" +
                  (missed > 0 ? $"，{missed} 条没找到（可能已被别处改过）" : "") +
                  $"。备份在 {backup}");

        Repaint();
    }
}
#endif
