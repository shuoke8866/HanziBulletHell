#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 汉字弹幕编辑器。
///
/// ## 做什么
///
///   左边：40 个汉字列表（点选）
///   中间：实时弹幕预览（可播放/暂停）
///   右边：形态标签 / 弹数 / 速度 / 尺寸，以及推导出来的伤害与 DPS
///
/// 调完点「写入源码」，直接改 HanziSystem.cs 里的 AddHanzi(...) 调用。
///
/// ## 为什么写回用"整块替换 + 唯一性校验"
///
/// 这个项目已经因为"往源码里追加定义"出过两次事故
/// （字形编辑器的 Save 被反复追加，47 个变成 54 个，画面出现重影）。
/// 所以这里的规矩是：
///   · 先在源码里**定位**这个字的 AddHanzi 整块
///   · 定位失败、或匹配到多于一处 → **拒绝写入**并报错，绝不硬写
///   · 写入前自动备份 .bak
///
/// 菜单：Tools/弹幕编辑器
/// </summary>
public class BulletPatternEditorWindow : EditorWindow
{
    const string SourcePath = "Assets/Scripts/HanziSystem.cs";

    // ---------- 数据 ----------
    class Row
    {
        public string name;
        public List<string> tags = new List<string>();
        public int bulletCount;
        public int savedBulletCount;
        public float bulletSpeed;
        public int strokeCount;

        // 编辑中的副本
        public List<string> editTags = new List<string>();
        public int editCount;
        public float editSpeed;
        public float editScale = 1f;

        public bool Dirty
        {
            get
            {
                if (editCount != bulletCount) return true;
                if (!Mathf.Approximately(editSpeed, bulletSpeed)) return true;
                if (editTags.Count != tags.Count) return true;
                for (int i = 0; i < tags.Count; i++)
                    if (editTags[i] != tags[i]) return true;
                return false;
            }
        }

        public string TagString { get { return string.Join(",", tags.ToArray()); } }
        public string EditTagString { get { return string.Join(",", editTags.ToArray()); } }
    }

    readonly List<Row> rows = new List<Row>();
    int selected = -1;
    Vector2 listScroll;
    Vector2 editScroll;
    string filter = "";

    BulletPreview preview;
    double lastTick;
    bool playing = true;

    [MenuItem("Tools/弹幕编辑器")]
    public static void Open()
    {
        var w = GetWindow<BulletPatternEditorWindow>("弹幕编辑器");
        w.minSize = new Vector2(860f, 560f);
        w.Reload();
    }

    void OnDestroy()
    {
        if (preview != null) preview.Dispose();
    }

    // ============================================================
    //  载入
    // ============================================================

    void Reload()
    {
        rows.Clear();
        selected = -1;

        var probe = new GameObject("__BulletEditorProbe");
        var hs = probe.AddComponent<HanziSystem>();
        hs.EnsureInitialized();

        foreach (HanziDef def in hs.hanziDatabase)
        {
            var r = new Row
            {
                name = def.hanziName,
                bulletCount = def.bulletCount,
                savedBulletCount = def.savedBulletCount,
                bulletSpeed = def.bulletSpeed,
                strokeCount = def.strokes != null ? def.strokes.Count : 1
            };
            if (def.attackTags != null) r.tags.AddRange(def.attackTags);

            r.editTags.AddRange(r.tags);
            r.editCount = r.bulletCount;
            r.editSpeed = r.bulletSpeed;

            rows.Add(r);
        }

        Object.DestroyImmediate(probe);

        if (preview == null) preview = new BulletPreview();

        if (rows.Count > 0) Select(0);

        Debug.Log($"[弹幕] 载入 {rows.Count} 个汉字");
    }

    void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, rows.Count - 1);
        RebuildPreview();
        Repaint();
    }

    Row Current
    {
        get { return selected >= 0 && selected < rows.Count ? rows[selected] : null; }
    }

    // ============================================================
    //  预览
    // ============================================================

    void RebuildPreview()
    {
        Row r = Current;
        if (r == null || preview == null) return;

        // 用字形真实的笔画序列来决定每发子弹的颜色/形状
        var strokes = StrokesOf(r.name);
        preview.Setup(r.editTags, r.editCount, r.editSpeed, r.editScale, strokes);
        preview.Clear();
        preview.WarmUp(18);      // 先快进一会儿，让画面里立刻有东西
    }

    static List<StrokeType> StrokesOf(string hanzi)
    {
        var probe = new GameObject("__StrokeLookup");
        var hs = probe.AddComponent<HanziSystem>();
        hs.EnsureInitialized();
        HanziDef def = hs.FindByName(hanzi);
        var result = def != null && def.strokes != null
            ? new List<StrokeType>(def.strokes)
            : new List<StrokeType>();
        Object.DestroyImmediate(probe);
        return result;
    }

    void Update()
    {
        if (!playing || preview == null) return;

        // 按真实时间推进（编辑器窗口的 Update 频率跟着 Repaint 走）
        double now = EditorApplication.timeSinceStartup;
        if (now - lastTick < 1.0 / 60.0) return;
        lastTick = now;

        preview.Step();
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

        GUILayout.Space(6f);
        GUILayout.Label("搜索", EditorStyles.miniLabel, GUILayout.Width(34f));
        filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField, GUILayout.Width(140f));

        GUILayout.FlexibleSpace();

        int dirty = 0;
        foreach (Row r in rows) if (r.Dirty) dirty++;

        GUI.enabled = dirty > 0;
        if (GUILayout.Button($"写入源码（{dirty} 处改动）",
                             EditorStyles.toolbarButton, GUILayout.Width(180f)))
            SaveAll();
        GUI.enabled = true;

        if (GUILayout.Button("全部撤销", EditorStyles.toolbarButton, GUILayout.Width(80f)))
        {
            foreach (Row r in rows)
            {
                r.editTags.Clear();
                r.editTags.AddRange(r.tags);
                r.editCount = r.bulletCount;
                r.editSpeed = r.bulletSpeed;
                r.editScale = 1f;
            }
            RebuildPreview();
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(190f));
        EditorGUILayout.LabelField("汉字", EditorStyles.boldLabel);

        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));

        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            if (!Matches(r)) continue;

            string label = r.name;
            if (r.Dirty) label += " *";

            var style = i == selected ? EditorStyles.boldLabel : EditorStyles.label;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(label, style, GUILayout.Width(48f)))
                Select(i);

            EditorGUILayout.LabelField(
                $"{r.editCount}发 · {r.strokeCount}画" +
                (r.editTags.Count > 0 ? " · " + r.EditTagString : ""),
                EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawPreviewPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(340f));
        EditorGUILayout.LabelField("弹幕预览", EditorStyles.boldLabel);

        if (preview != null && preview.Texture != null)
        {
            Rect rect = GUILayoutUtility.GetRect(320f, 320f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(rect, preview.Texture, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.BeginHorizontal();
        playing = GUILayout.Toggle(playing, playing ? "▶ 播放中" : "⏸ 已暂停", "Button",
                                   GUILayout.Width(90f));
        if (GUILayout.Button("重放", GUILayout.Width(60f))) RebuildPreview();
        EditorGUILayout.EndHorizontal();

        Row r = Current;
        if (r != null)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("推导数值", EditorStyles.boldLabel);

            int std = GameBalance.BulletCountFor(r.strokeCount);
            int dmg = GameBalance.DamageFor(r.strokeCount, std, r.editCount);
            float interval = GameBalance.FireIntervalFor(r.strokeCount);
            float dps = r.editCount * dmg / interval;

            EditorGUILayout.LabelField($"标准弹数（该笔画档）", std.ToString());
            EditorGUILayout.LabelField($"单发伤害", dmg.ToString());
            EditorGUILayout.LabelField($"射击间隔", $"{interval:0.###} 秒");
            EditorGUILayout.LabelField($"理论 DPS（全中）", $"{dps:0.#}");
        }

        EditorGUILayout.EndVertical();
    }

    void DrawEditPane()
    {
        Row r = Current;
        if (r == null)
        {
            EditorGUILayout.LabelField("（左边选一个汉字）");
            return;
        }

        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);

        EditorGUILayout.LabelField($"「{r.name}」的弹幕", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{r.strokeCount} 画", EditorStyles.miniLabel);
        EditorGUILayout.Space(6f);

        // ---- 弹数 ----
        EditorGUI.BeginChangeCheck();
        r.editCount = EditorGUILayout.IntSlider(
            new GUIContent("弹数", "这一轮打几发。**这是权威总数**，形态标签只决定它们怎么摆。"),
            r.editCount, 1, 24);
        if (EditorGUI.EndChangeCheck()) RebuildPreview();

        // ---- 速度 ----
        EditorGUI.BeginChangeCheck();
        r.editSpeed = EditorGUILayout.Slider(
            new GUIContent("弹速", "子弹飞行速度。这是手感参数，不影响强度。"),
            r.editSpeed, 3f, 20f);
        if (EditorGUI.EndChangeCheck()) RebuildPreview();

        EditorGUILayout.Space(8f);

        // ---- 形态标签 ----
        EditorGUILayout.LabelField("形态标签", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("顺序即优先级：第一个拿最多子弹", EditorStyles.miniLabel);
        EditorGUILayout.Space(4f);

        for (int i = 0; i < r.editTags.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            string cur = r.editTags[i];
            int idx = System.Array.IndexOf(BulletShapes.All, cur);

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(idx < 0 ? 0 : idx, ShapeLabels(), GUILayout.Width(190f));
            if (EditorGUI.EndChangeCheck())
            {
                r.editTags[i] = BulletShapes.All[Mathf.Clamp(newIdx, 0, BulletShapes.All.Length - 1)];
                RebuildPreview();
            }

            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                r.editTags.RemoveAt(i);
                RebuildPreview();
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ 加形态", GUILayout.Width(90f)) &&
            r.editTags.Count < 4)
        {
            r.editTags.Add("straight");
            RebuildPreview();
        }

        if (GUILayout.Button("穿透", GUILayout.Width(60f)))
        {
            if (!r.editTags.Contains("pierce")) r.editTags.Add("pierce");
            else r.editTags.Remove("pierce");
            RebuildPreview();
        }

        EditorGUILayout.LabelField(r.editTags.Contains("pierce") ? "（已加穿透）" : "（无穿透）",
                                   EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10f);

        // ---- 单发伤害预览 ----
        int std2 = GameBalance.BulletCountFor(r.strokeCount);
        int dmg2 = GameBalance.DamageFor(r.strokeCount, std2, r.editCount);

        EditorGUILayout.HelpBox(
            $"弹数 {r.editCount} → 单发伤害 {dmg2}\n" +
            $"（强度由笔画数决定，弹数只改形状。总输出 = 标准弹数 {std2} × " +
            $"{GameBalance.DamageFor(r.strokeCount, std2, std2)}）",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
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

    bool Matches(Row r)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return r.name.Contains(filter) || r.EditTagString.ToLowerInvariant()
                                            .Contains(filter.ToLowerInvariant());
    }

    // ============================================================
    //  写回源码
    // ============================================================

    void SaveAll()
    {
        if (!File.Exists(SourcePath))
        {
            Debug.LogError($"[弹幕] 找不到 {SourcePath}");
            return;
        }

        string src = File.ReadAllText(SourcePath, Encoding.UTF8);
        string backup = SourcePath + ".bak";
        File.WriteAllText(backup, src, new UTF8Encoding(false));

        int ok = 0, skipped = 0;

        foreach (Row r in rows)
        {
            if (!r.Dirty) continue;

            string replaced;
            string err;
            if (!TryReplaceHanziBlock(src, r, out replaced, out err))
            {
                Debug.LogError($"[弹幕] 「{r.name}」写入失败：{err}（这一条已跳过）");
                skipped++;
                continue;
            }

            src = replaced;
            ok++;
        }

        File.WriteAllText(SourcePath, src, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        Debug.Log($"[弹幕] 已写入 {ok} 条" +
                  (skipped > 0 ? $"，跳过 {skipped} 条（见上面的错误）" : "") +
                  $"。备份在 {backup}");

        Reload();
    }

    /// <summary>
    /// 把某个汉字的 AddHanzi 整块替换成新参数。
    ///
    /// **安全规矩**（这个项目在"往源码写东西"上出过两次事故，所以很保守）：
    ///   1. 必须能唯一定位到 `AddHanzi("字",` 这一处；0 处或多处都拒绝写入
    ///   2. 必须能匹配到完整的 `AddHanzi(...);` 块；匹配不到就拒绝
    ///   3. 只替换这一个块，别的地方一个字节都不动
    /// </summary>
    static bool TryReplaceHanziBlock(string src, Row r,
                                     out string result, out string error)
    {
        result = src;
        error = null;

        string head = "AddHanzi(\"" + r.name + "\"";
        int count = CountOccurrences(src, head);

        if (count == 0) { error = "源码里找不到这个字的定义"; return false; }
        if (count > 1) { error = $"源码里有 {count} 处同名定义，不敢乱改"; return false; }

        int start = src.IndexOf(head, System.StringComparison.Ordinal);

        // 从 start 往后找这个调用结束的分号（AddHanzi 的参数里不含分号）
        int end = src.IndexOf(';', start);
        if (end < 0) { error = "找不到这条定义的结尾分号"; return false; }

        string oldBlock = src.Substring(start, end - start + 1);

        var sb = new StringBuilder();
        sb.Append("AddHanzi(\"").Append(r.name).Append("\",\n");
        sb.Append("            new List<StrokeType> ").Append(StrokeListLiteral(src, start)).Append(",\n");

        var shapes = new List<string>();
        foreach (string t in r.editTags) if (t != "pierce") shapes.Add(t);
        if (shapes.Count == 0) shapes.Add("straight");

        sb.Append("            new List<string> { ");
        for (int i = 0; i < shapes.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('"').Append(shapes[i]).Append('"');
        }
        if (r.editTags.Contains("pierce"))
        {
            if (shapes.Count > 0) sb.Append(", ");
            sb.Append("\"pierce\"");
        }
        sb.Append(" },\n");

        sb.Append("            ").Append(r.editCount).Append(", ")
          .Append(r.editSpeed.ToString("0.#")).Append("f, \"")
          .Append(DescOf(oldBlock)).Append("\");");

        result = src.Substring(0, start) + sb.ToString() + src.Substring(end + 1);
        return true;
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }
        return n;
    }

    /// <summary>把原来那条定义里的笔画列表原样抠出来（不动它）</summary>
    static string StrokeListLiteral(string src, int from)
    {
        int a = src.IndexOf("new List<StrokeType>", from, System.StringComparison.Ordinal);
        if (a < 0) return "{ StrokeType.Heng }";

        int open = src.IndexOf('{', a);
        int close = src.IndexOf('}', open);
        if (open < 0 || close < 0) return "{ StrokeType.Heng }";

        return src.Substring(open, close - open + 1);
    }

    /// <summary>抠出原来的说明文字，替换时保留</summary>
    static string DescOf(string block)
    {
        var m = Regex.Match(block, "\"([^\"]*)\"\\s*\\)\\s*;\\s*$");
        return m.Success ? m.Groups[1].Value : "自定义弹幕";
    }
}
#endif
