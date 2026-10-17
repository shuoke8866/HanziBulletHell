using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字幕 ↔ 房间开门 协同工具。
///
/// ## 这个工具解决什么
///
/// 玩家要的：「规定在说完那句话后开哪个房间的门」。
///
/// 原来教程和门是两套互不相干的东西：
///   · 字幕按房间播（GameText.教学步骤）
///   · 门由 TutorialCoach 按"房间说完了"放开
/// 两者靠**猜**关联，所以你改一句文案、挪一条步骤，门就可能不对。
///
/// 这个工具把它们**显式绑在一张表上**：
///
///     第 N 条字幕  →  延时 X 秒  →  打开【某扇门】
///
/// 左边列出所有字幕（带房间和首句，方便认），右边选门。
/// 改完点保存，直接写回 `GameText.门规则表()`。
///
/// 菜单：Tools/字幕 × 房间开门 协同工具
/// </summary>
public class SubtitleDoorTool : EditorWindow
{
    const string TextPath = "Assets/Scripts/GameText.cs";

    // ---------- 规则 ----------
    class Row
    {
        public int subtitleIndex;
        public string doorName;
        public float delay;
    }

    readonly List<Row> rows = new List<Row>();

    // ---------- 缓存 ----------
    string[]   subtitleLabels;     // "1. [Room0_Doubt] ……"
    string[]   doorNames;          // 场景里所有 Door 对象名
    List<string> roomNames = new List<string>();

    Vector2 scroll;
    bool dirty;
    string message = "";

    [MenuItem("Tools/字幕 × 房间开门 协同工具")]
    public static void Open()
    {
        var w = GetWindow<SubtitleDoorTool>("字幕 × 门 协同");
        w.minSize = new Vector2(780f, 560f);
        w.Reload();
    }

    // ============================================================
    //  载入
    // ============================================================

    void Reload()
    {
        message = "";
        dirty = false;
        rows.Clear();

        LoadSubtitles();
        LoadDoors();
        LoadRules();
    }

    /// <summary>读教学步骤，生成给玩家看的标签</summary>
    void LoadSubtitles()
    {
        GameText.CoachStep[] steps = GameText.教学步骤();

        subtitleLabels = new string[steps.Length];

        for (int i = 0; i < steps.Length; i++)
        {
            string room = string.IsNullOrEmpty(steps[i].room) ? "?" : steps[i].room;
            string first = (steps[i].lines != null && steps[i].lines.Length > 0)
                ? steps[i].lines[0] : "";
            if (first.Length > 26) first = first.Substring(0, 26) + "…";

            subtitleLabels[i] = $"{i + 1}. [{room}] {first}";
        }
    }

    /// <summary>从场景文件里扫出所有门名</summary>
    void LoadDoors()
    {
        var list = new List<string>();

        string[] scenes = Directory.GetFiles("Assets/Scenes", "*.unity");
        foreach (string sc in scenes)
        {
            string[] lines;
            try { lines = File.ReadAllLines(sc); }
            catch { continue; }

            foreach (string line in lines)
            {
                string s = line.Trim();
                if (!s.StartsWith("m_Name: Door")) continue;

                string nm = s.Substring("m_Name: ".Length).Trim();
                if (nm.Length > 0 && !list.Contains(nm)) list.Add(nm);
            }
        }

        list.Sort();
        doorNames = list.ToArray();
    }

    /// <summary>从 GameText.门规则表() 读取现有规则</summary>
    void LoadRules()
    {
        if (!File.Exists(TextPath)) return;

        string src = File.ReadAllText(TextPath, Encoding.UTF8);

        // 定位 门规则表() 的方法体
        int mi = src.IndexOf("public static 门规则[] 门规则表()");
        if (mi < 0) { message = "源码里没有 门规则表()，先跑一次生成脚本"; return; }

        int brace = src.IndexOf('{', mi);
        int depth = 0, end = brace;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
        }

        string body = src.Substring(brace, end - brace + 1);

        // 解析 new 门规则(序号, "门名", 延迟)
        var re = new Regex(@"new\s*门规则\s*\(\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*([\d.]+)f?\s*\)");
        foreach (Match m in re.Matches(body))
        {
            rows.Add(new Row
            {
                subtitleIndex = int.Parse(m.Groups[1].Value),
                doorName = m.Groups[2].Value,
                delay = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        message = $"已载入 {rows.Count} 条规则";
    }

    // ============================================================
    //  绘制
    // ============================================================

    void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.Space(4f);

        EditorGUILayout.HelpBox(
            "左边是教程字幕（按顺序），右边选「说完这条之后开哪扇门」。\n" +
            "延迟单位是秒 —— 1.0 秒比较自然（话落音后留一点余韵）。\n" +
            "改完点「保存到代码」，运行时会按这张表开门。",
            MessageType.Info);

        EditorGUILayout.Space(4f);

        // 表头
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("字幕（说完这条之后…）", EditorStyles.toolbarButton, GUILayout.Width(400f));
        EditorGUILayout.LabelField("开这扇门", EditorStyles.toolbarButton, GUILayout.Width(200f));
        EditorGUILayout.LabelField("延迟(秒)", EditorStyles.toolbarButton, GUILayout.Width(70f));
        GUILayout.Space(60f);
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];

            EditorGUILayout.BeginHorizontal();

            // ---- 字幕下拉 ----
            int curIdx = SubtitleIndexOf(r.subtitleIndex);
            int picked = EditorGUILayout.Popup(curIdx, subtitleLabels, GUILayout.Width(400f));
            if (picked != curIdx)
            {
                r.subtitleIndex = picked;
                dirty = true;
            }

            // ---- 门下拉 ----
            int curDoor = Array.IndexOf(doorNames, r.doorName);
            bool orphanDoor = curDoor < 0;
            if (orphanDoor)
            {
                // 保留原本的门名，让它显示在末尾，避免静默丢数据
                var tmp = new List<string>(doorNames) { r.doorName };
                int p2 = EditorGUILayout.Popup(tmp.Count - 1, tmp.ToArray(), GUILayout.Width(200f));
                if (p2 != tmp.Count - 1) { r.doorName = tmp[p2]; dirty = true; }
                EditorGUILayout.LabelField("⚠", GUILayout.Width(18f));
            }
            else
            {
                int p2 = EditorGUILayout.Popup(curDoor, doorNames, GUILayout.Width(200f));
                if (p2 != curDoor) { r.doorName = doorNames[p2]; dirty = true; }
            }

            // ---- 延迟 ----
            EditorGUI.BeginChangeCheck();
            float d = EditorGUILayout.FloatField(r.delay, GUILayout.Width(70f));
            if (EditorGUI.EndChangeCheck())
            {
                r.delay = Mathf.Max(0f, d);
                dirty = true;
            }

            // ---- 删除 ----
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                rows.RemoveAt(i);
                dirty = true;
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 添加规则", GUILayout.Height(26f)))
        {
            rows.Add(new Row { subtitleIndex = 0, doorName = doorNames.Length > 0 ? doorNames[0] : "", delay = 1f });
            dirty = true;
        }

        GUI.enabled = dirty;
        if (GUILayout.Button("保存到代码", GUILayout.Height(26f)))
            Save();
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(message))
            EditorGUILayout.LabelField(message, EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>把"字幕序号"映射到下拉框下标（本来就是一回事，但防越界）</summary>
    int SubtitleIndexOf(int idx)
    {
        if (subtitleLabels == null || subtitleLabels.Length == 0) return 0;
        return Mathf.Clamp(idx, 0, subtitleLabels.Length - 1);
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("重新载入", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Reload();

        if (GUILayout.Button("打开字幕编辑器", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            SubtitleEditorWindow.Open();

        GUILayout.FlexibleSpace();

        EditorGUILayout.LabelField(
            $"字幕 {subtitleLabels?.Length ?? 0} 条 · 门 {doorNames?.Length ?? 0} 扇 · 规则 {rows.Count} 条",
            EditorStyles.miniLabel, GUILayout.Width(260f));

        EditorGUILayout.EndHorizontal();
    }

    // ============================================================
    //  保存
    // ============================================================

    void Save()
    {
        if (!File.Exists(TextPath)) { message = "找不到 GameText.cs"; return; }

        string src = File.ReadAllText(TextPath, Encoding.UTF8);

        int mi = src.IndexOf("public static 门规则[] 门规则表()");
        if (mi < 0) { message = "源码里没有 门规则表()"; return; }

        int brace = src.IndexOf('{', mi);
        int depth = 0, end = brace;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
        }

        // 组新的方法体
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("        return new[]\n");
        sb.Append("        {\n");
        sb.Append("            // 字幕序号, 门名, 延迟（由「字幕 × 房间开门 协同工具」生成）\n");

        // 按字幕序号排序，读起来舒服
        rows.Sort((a, b) => a.subtitleIndex.CompareTo(b.subtitleIndex));

        foreach (Row r in rows)
        {
            sb.Append($"            new 门规则({r.subtitleIndex}, \"{r.doorName}\", ");
            sb.Append(r.delay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("f),\n");
        }

        sb.Append("        };\n");
        sb.Append("    }");

        src = src.Substring(0, brace) + sb.ToString() + src.Substring(end + 1);

        File.WriteAllText(TextPath, src, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        dirty = false;
        message = $"已保存 {rows.Count} 条规则";
        Debug.Log($"[协同工具] 已写入 {rows.Count} 条「字幕 → 门」规则");
    }
}
