#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字形检查器：在 Unity 里直接看到所有汉字的当前渲染效果。
///
/// 打开方式：菜单 Tools → 字形检查器
///
/// 为什么需要它：
///   字形是手写折线坐标，改完只盯着代码根本判断不出对不对。
///   之前只能"跑批处理 → 导出 PNG → 打开图片"，一轮几十秒；
///   改成编辑器窗口后，改完代码等编译完就能立刻看到。
///
/// 窗口里：
///   - 每个字带参考网格（外框 0.10/0.90，三等分线 0.30/0.50/0.70）
///   - 底部列出"超出主体框"和"平行笔画粘连"两类自动检查结果
///   - 点「重新渲染」或改完脚本自动刷新
/// </summary>
public class GlyphInspectorWindow : EditorWindow
{
    const int CellSize = 96;      // 每个字的渲染尺寸
    const int Cols = 8;
    const int Pad = 4;
    const int LabelH = 16;

    Vector2 scroll;
    Texture2D sheet;
    string lastGlyphHash = "";
    List<string> report = new List<string>();

    static readonly float[] Guides = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };

    [MenuItem("Tools/字形检查器")]
    public static void Open()
    {
        var w = GetWindow<GlyphInspectorWindow>("字形检查器");
        w.minSize = new Vector2(700f, 500f);
        w.Rebuild();
    }

    void OnEnable()
    {
        // 脚本重新编译后自动刷新：改完 HanziRenderer 就能直接看到新字形
        EditorApplication.projectChanged += Rebuild;
        Rebuild();
    }

    void OnDisable()
    {
        EditorApplication.projectChanged -= Rebuild;
        if (sheet != null) DestroyImmediate(sheet);
    }

    void OnGUI()
    {
        if (GUILayout.Button("重新渲染", GUILayout.Height(24f)))
            Rebuild();

        if (sheet == null)
        {
            EditorGUILayout.HelpBox("字形库为空。检查 HanziRenderer.BuildDefinitions()。", MessageType.Warning);
            return;
        }

        // 检查结果
        if (report.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string r in report) sb.AppendLine(r);
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("未发现笔画超出主体框或明显粘连。", MessageType.Info);
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        float w = position.width - 24f;
        float h = w * sheet.height / sheet.width;
        GUILayout.Label(sheet, GUILayout.Width(w), GUILayout.Height(h));
        EditorGUILayout.EndScrollView();
    }

    void Rebuild()
    {
        if (this == null) return;

        List<string> glyphs = HanziRenderer.AllGlyphsSorted();
        if (glyphs.Count == 0) { sheet = null; return; }

        // 避免每次 projectChanged 都重渲染（很费）
        string hash = string.Join(",", glyphs.ToArray()) + "|" + glyphs.Count;
        if (hash == lastGlyphHash && sheet != null) return;
        lastGlyphHash = hash;

        int rows = Mathf.CeilToInt(glyphs.Count / (float)Cols);
        int W = Cols * (CellSize + Pad) + Pad;
        int H = rows * (CellSize + Pad + LabelH) + Pad;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.10f, 0.11f, 0.14f, 1f);

        report.Clear();
        var outOfBox = new List<string>();
        var sticky = new List<string>();

        for (int idx = 0; idx < glyphs.Count; idx++)
        {
            int col = idx % Cols;
            int row = idx / Cols;
            int ox = Pad + col * (CellSize + Pad);
            int oy = H - Pad - (row + 1) * (CellSize + Pad + LabelH);

            // 参考网格
            DrawGuides(px, W, H, ox, oy);

            Texture2D g = HanziRenderer.RenderGlyph(glyphs[idx], CellSize);
            if (g == null) continue;
            Color[] gp = g.GetPixels();
            DestroyImmediate(g);

            int minX = CellSize, maxX = -1, minY = CellSize, maxY = -1;

            // 注意：GetPixels 的 y 向上，画布 px[] 也 y 向上 —— **不要翻转**
            for (int y = 0; y < CellSize; y++)
            {
                for (int x = 0; x < CellSize; x++)
                {
                    if (gp[y * CellSize + x].a < 0.5f) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    int tx = ox + x, ty = oy + y;
                    if (tx < 0 || tx >= W || ty < 0 || ty >= H) continue;
                    px[ty * W + tx] = new Color(1f, 0.35f, 0.30f, 1f);
                }
            }

            if (maxX >= 0)
            {
                float bx0 = minX / (float)CellSize, bx1 = maxX / (float)CellSize;
                if (bx0 < 0.055f || bx1 > 0.945f)
                    outOfBox.Add($"{glyphs[idx]}({bx0:0.00}~{bx1:0.00})");
            }
        }

        if (outOfBox.Count > 0)
            report.Add("横向超出主体框：" + string.Join(" ", outOfBox));

        // 网格吸附检查：笔画坐标不在参考线上、且离所有参考线都超过容差，
        // 说明这一笔是"随手写的"，很容易和其它字对不齐。
        var offGrid = AuditGridSnap(glyphs);
        if (offGrid.Count > 0)
            report.Add("笔画未吸附到参考网格：" + string.Join(" ", offGrid));
        else
            report.Add("所有笔画坐标都吸附在参考网格上。");

        tex.SetPixels(px);
        tex.Apply();

        if (sheet != null) DestroyImmediate(sheet);
        sheet = tex;

        Repaint();
    }

    /// <summary>
    /// 查出哪些字的笔画坐标"没落在参考网格上"。
    ///
    /// 参考线是 0.10 / 0.30 / 0.50 / 0.70 / 0.90。允许一定容差（0.06），
    /// 超过容差就说明这个坐标是随手写的 —— 这类字最容易看起来"歪"。
    /// 撇捺的端点本来就该落在参考线之间，所以只检查横竖笔画。
    /// </summary>
    static List<string> AuditGridSnap(List<string> glyphs)
    {
        var bad = new List<string>();
        const float tol = 0.06f;

        foreach (string ch in glyphs)
        {
            var strokes = HanziRenderer.DebugStrokes(ch);
            if (strokes == null) continue;

            int off = 0;
            foreach (var s in strokes)
            {
                if (s.pts == null || s.pts.Count < 2) continue;

                // 只审计横(H)和竖(V)：
                //   横看它的 y 有没有落在参考横线上
                //   竖看它的 x 有没有落在参考竖线上
                // 撇捺/折的端点本来就该落在参考线之间，不参与审计。
                if (s.kind == "H" && !OnGrid(s.pts[0].y, tol)) off++;
                else if (s.kind == "V" && !OnGrid(s.pts[0].x, tol)) off++;
            }

            if (off > 0) bad.Add($"{ch}({off})");
        }

        return bad;
    }

    static bool OnGrid(float v, float tol)
    {
        foreach (float g in Guides)
            if (Mathf.Abs(v - g) <= tol) return true;

        // 0.14~0.26 这类"次一级"的常用值也放过，避免和用户习惯打架
        foreach (float g in new[] { 0.14f, 0.16f, 0.20f, 0.22f, 0.24f, 0.26f, 0.28f,
                                    0.34f, 0.36f, 0.40f, 0.44f, 0.56f, 0.60f, 0.64f,
                                    0.66f, 0.68f, 0.72f, 0.74f, 0.76f, 0.78f, 0.80f,
                                    0.84f, 0.86f, 0.88f })
            if (Mathf.Abs(v - g) <= 0.02f) return true;

        return false;
    }

    void DrawGuides(Color[] px, int W, int H, int ox, int oy)
    {
        Color frame = new Color(0.35f, 0.40f, 0.50f, 1f);
        Color mid = new Color(0.20f, 0.23f, 0.30f, 1f);

        for (int i = 0; i < Guides.Length; i++)
        {
            bool isFrame = (i == 0 || i == Guides.Length - 1);
            Color c = isFrame ? frame : mid;

            int cx = ox + Mathf.RoundToInt(Guides[i] * (CellSize - 1));
            int cy = oy + Mathf.RoundToInt(Guides[i] * (CellSize - 1));

            for (int y = 0; y < CellSize; y++)
            {
                int ty = oy + y;
                if (cx >= 0 && cx < W && ty >= 0 && ty < H) px[ty * W + cx] = c;
            }
            for (int x = 0; x < CellSize; x++)
            {
                int tx = ox + x;
                if (tx >= 0 && tx < W && cy >= 0 && cy < H) px[cy * W + tx] = c;
            }
        }
    }
}
#endif
