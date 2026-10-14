#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字形预览工具：把所有汉字渲染成一张对比大图，导出 PNG。
///
/// 用途：字形是手写坐标，改完之后只盯着代码根本判断不出笔画有没有对齐。
/// 这个工具把每个字都画出来、并叠上参考网格，一眼就能看出：
///   - 笔画有没有落在中线上（歪不歪）
///   - 上下结构的两部分宽度是否一致（比如 里 的 日 对 土）
///   - 部件是否贴在一起或重叠（比如 皇 的 白 和 王）
///
/// 菜单：Tools/字形预览（导出 PNG）
/// 命令行：-executeMethod GlyphPreview.Export
/// </summary>
public static class GlyphPreview
{
    const int CellSize = 128;      // 每格像素
    const int Cols = 8;            // 每行几个字
    const int Pad = 6;             // 格间距
    const int LabelHeight = 18;    // 底部留白放"参考网格说明"

    // 参考网格（与字形坐标系的 0~1 对应）
    //   外框：0.10 ~ 0.90（汉字主体应落在这个范围内）
    //   三等分线：0.30 / 0.50 / 0.70（结构中线）
    static readonly float[] Guides = { 0.10f, 0.30f, 0.50f, 0.70f, 0.90f };

    const string OutPath = "F:/HanziBulletHell/_glyph_preview.png";

    [MenuItem("Tools/字形预览（导出 PNG）")]
    public static void Export()
    {
        ExportSet(null);
    }

    /// <summary>只导出指定的几个字，方便放大逐个检查</summary>
    public static void ExportFocus()
    {
        ExportSet(new List<string> { "里", "皇", "上", "下", "工", "丰", "中", "土", "圭", "日", "白", "由" }, 256);
    }

    /// <summary>把报告有问题的几个字放大，逐像素检查</summary>
    public static void ExportBig()
    {
        ExportSet(new List<string> { "里", "圭" }, 320);
    }

    /// <summary>
    /// 按游戏实际分辨率（64px 纹理）渲染，检查缺陷在真实尺寸下是否可见。
    /// 之前一直在 256/480px 下放大看，容易把 1~2 像素的瑕疵放大成"很明显的问题"。
    /// </summary>
    public static void ExportGameSize()
    {
        ExportGameSize(new List<string> { "里", "皇", "田", "日", "白", "由" });
    }

    /// <summary>
    /// 按游戏实际纹理尺寸渲染，再用最近邻放大若干倍铺到画布上。
    ///
    /// 这样看到的**像素结构**与游戏里完全一致（不做任何插值），
    /// 只是整体放大了方便肉眼看。之前直接在大尺寸下渲染，
    /// 会把 1~2 像素的瑕疵放大成"很明显的问题"，判断容易失真。
    /// </summary>
    public static void ExportGameSize(List<string> glyphs)
    {
        const int texSize = 64;    // 与 HanziRenderer.textureSize 保持一致
        const int zoom = 5;        // 最近邻放大倍数
        int cell = texSize * zoom;

        int cols = Mathf.Min(3, glyphs.Count);
        int rows = Mathf.CeilToInt(glyphs.Count / (float)cols);
        int W = cols * (cell + Pad) + Pad;
        int H = rows * (cell + Pad + LabelHeight) + Pad;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.10f, 0.11f, 0.14f, 1f);

        for (int idx = 0; idx < glyphs.Count; idx++)
        {
            int col = idx % cols;
            int row = idx / cols;

            int ox = Pad + col * (cell + Pad);
            int oy = H - Pad - (row + 1) * (cell + Pad + LabelHeight);

            // 按游戏尺寸渲染
            Texture2D g = HanziRenderer.RenderGlyph(glyphs[idx], texSize);
            if (g == null) continue;
            Color[] gp = g.GetPixels();
            Object.DestroyImmediate(g);

            // 参考网格（按 texSize 坐标画，再随放大倍数铺开）
            DrawGuidesZoomed(px, W, H, ox, oy, texSize, zoom);

            // 最近邻放大贴上去
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    if (gp[y * texSize + x].a < 0.5f) continue;

                    // 同样不翻转（GetPixels 与画布都是 y 向上）
                    int bx = ox + x * zoom;
                    int by = oy + y * zoom;

                    for (int dy = 0; dy < zoom; dy++)
                    {
                        for (int dx = 0; dx < zoom; dx++)
                        {
                            int tx = bx + dx, ty = by + dy;
                            if (tx < 0 || tx >= W || ty < 0 || ty >= H) continue;
                            px[ty * W + tx] = new Color(1f, 0.35f, 0.30f, 1f);
                        }
                    }
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(OutPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        Debug.Log($"[字形预览] 按游戏尺寸({texSize}px)渲染 {glyphs.Count} 个字 → {OutPath}  画布 {W}x{H}");
    }

    /// <summary>画参考网格（按纹理坐标，再乘以放大倍数）</summary>
    static void DrawGuidesZoomed(Color[] px, int W, int H, int ox, int oy, int texSize, int zoom)
    {
        Color frame = new Color(0.35f, 0.40f, 0.50f, 1f);
        Color mid = new Color(0.20f, 0.23f, 0.30f, 1f);

        int cell = texSize * zoom;

        for (int gi = 0; gi < Guides.Length; gi++)
        {
            float v = Guides[gi];
            bool isFrame = (gi == 0 || gi == Guides.Length - 1);
            Color c = isFrame ? frame : mid;

            int cx = ox + Mathf.RoundToInt(v * (texSize - 1)) * zoom;
            int cy = oy + Mathf.RoundToInt(v * (texSize - 1)) * zoom;

            for (int y = 0; y < cell; y++)
            {
                int ty = oy + y;
                if (cx >= 0 && cx < W && ty >= 0 && ty < H) px[ty * W + cx] = c;
            }
            for (int x = 0; x < cell; x++)
            {
                int tx = ox + x;
                if (tx >= 0 && tx < W && cy >= 0 && cy < H) px[cy * W + tx] = c;
            }
        }
    }

    /// <summary>
    /// 坐标系验证：渲染结构已知的简单字。
    /// 「二」上短下长 —— 如果渲染出来是上长下短，说明 y 方向反了。
    /// 「上」竖在上、长横在最下 —— 用来确认上下顺序。
    /// </summary>
    public static void ExportProbe()
    {
        ExportSet(new List<string> { "二", "上", "三", "土" }, 256);
    }

    static void ExportSet(List<string> focus)
    {
        ExportSet(focus, 256);
    }

    static void ExportSet(List<string> focus, int fixedCell)
    {
        List<string> glyphs = focus ?? HanziRenderer.AllGlyphsSorted();
        if (glyphs.Count == 0)
        {
            Debug.LogError("[字形预览] 字形库是空的");
            return;
        }

        int cols = focus != null ? Mathf.Min(2, glyphs.Count) : Cols;
        int cell = focus != null ? fixedCell : CellSize;

        int rows = Mathf.CeilToInt(glyphs.Count / (float)cols);
        int W = cols * (cell + Pad) + Pad;
        int H = rows * (cell + Pad + LabelHeight) + Pad;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];

        // 背景
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.10f, 0.11f, 0.14f, 1f);

        for (int idx = 0; idx < glyphs.Count; idx++)
        {
            int col = idx % cols;
            int row = idx / cols;

            int ox = Pad + col * (cell + Pad);
            // 从上往下排
            int oy = H - Pad - (row + 1) * (cell + Pad + LabelHeight);

            Texture2D glyph = HanziRenderer.RenderGlyph(glyphs[idx], cell);
            if (glyph == null) continue;

            // 先画参考网格（在字下面）
            DrawGuides(px, W, H, ox, oy, cell);

            // 再叠字形像素（亮红）
            Color[] gp = glyph.GetPixels();
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    if (gp[y * cell + x].a < 0.5f) continue;

                    // 不要翻转！
                    // Texture2D.GetPixels() 的 y 向上（(0,0) 在左下），
                    // 画布 px[] 也是 y 向上，EncodeToPNG 同样按 y 向上写出。
                    // 三处约定一致，逐像素拷贝即可。
                    // （之前这里多翻了一次，导致预览图上下颠倒，
                    //   前几轮关于字形的判断全是基于镜像图做的，教训。）
                    int tx = ox + x;
                    int ty = oy + y;
                    if (tx < 0 || tx >= W || ty < 0 || ty >= H) continue;

                    px[ty * W + tx] = new Color(1f, 0.35f, 0.30f, 1f);
                }
            }

            Object.DestroyImmediate(glyph);
        }

        tex.SetPixels(px);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();
        File.WriteAllBytes(OutPath, png);
        Object.DestroyImmediate(tex);

        Debug.Log($"[字形预览] 已导出 {glyphs.Count} 个字 → {OutPath}  尺寸 {W}x{H}");

        if (focus == null)
        {
            ReportOutOfBox(glyphs);
            ReportStrokeOverlap(glyphs);
        }
    }

    /// <summary>
    /// 检查同一个字内部、平行的笔画有没有糊在一起。
    ///
    /// 只在**字的中心区域**取样：边缘处撇捺本来就会汇聚、横竖本来就会相交，
    /// 那些位置测出来的"小缝隙"是正常的，不能算粘连。
    /// 中心区域里如果两条平行笔画之间的缝隙接近 0，才是真的糊了。
    /// </summary>
    static void ReportStrokeOverlap(List<string> glyphs)
    {
        var bad = new List<string>();

        foreach (string ch in glyphs)
        {
            float gap = MinParallelGap(ch);
            // 笔宽约 CellSize*0.085≈11px。水平笔画在中心区域还挤在
            // 5px 以内，基本就是两笔黏成一条了。
            if (gap >= 0f && gap < 5f)
                bad.Add($"{ch}({gap:0.0}px)");
        }

        if (bad.Count > 0)
            Debug.LogWarning($"[字形预览] 中心区域有笔画糊在一起（括号是缝隙像素）：{string.Join(" ", bad)}");
        else
            Debug.Log("[字形预览] 未发现笔画粘连 ✓");
    }

    /// <summary>
    /// 返回字形中心区域内"相邻平行笔画之间最小的空隙"（像素）。
    /// 返回 -1 表示没有可测量的平行笔画（比如只有一笔、或中心区域是空的）。
    /// </summary>
    static float MinParallelGap(string ch)
    {
        Texture2D t = HanziRenderer.RenderGlyph(ch, CellSize);
        if (t == null) return -1f;

        Color[] p = t.GetPixels();
        Object.DestroyImmediate(t);

        float best = -1f;

        // 中心区域：40% ~ 60%，避开所有边缘汇聚点
        int lo = Mathf.RoundToInt(CellSize * 0.40f);
        int hi = Mathf.RoundToInt(CellSize * 0.60f);

        // 竖向取样 → 测上下相邻的横向笔画
        for (int x = lo; x <= hi; x += 2)
        {
            float g = MinGapInColumn(p, x);
            if (g >= 0f && (best < 0f || g < best)) best = g;
        }

        // 横向取样 → 测左右相邻的竖向笔画
        for (int y = lo; y <= hi; y += 2)
        {
            float g = MinGapInRow(p, y);
            if (g >= 0f && (best < 0f || g < best)) best = g;
        }

        return best;
    }

    /// <summary>某一列里，相邻两个实心段之间的最小空隙</summary>
    static float MinGapInColumn(Color[] p, int x)
    {
        float best = -1f;
        int gapStart = -1;

        for (int y = 0; y < CellSize; y++)
        {
            bool on = p[y * CellSize + x].a > 0.5f;

            if (!on && gapStart < 0) gapStart = y;
            else if (on && gapStart >= 0)
            {
                // 只有在实心段之间才算"内部缝隙"
                if (gapStart > 0 && HasSolid(p, x, y))
                {
                    int len = y - gapStart;
                    if (best < 0f || len < best) best = len;
                }
                gapStart = -1;
            }
        }
        return best;
    }

    /// <summary>某一行里，相邻两个实心段之间的最小空隙</summary>
    static float MinGapInRow(Color[] p, int y)
    {
        float best = -1f;
        int gapStart = -1;

        for (int x = 0; x < CellSize; x++)
        {
            bool on = p[y * CellSize + x].a > 0.5f;

            if (!on && gapStart < 0) gapStart = x;
            else if (on && gapStart >= 0)
            {
                if (gapStart > 0 && HasSolid(p, x, y))
                {
                    int len = x - gapStart;
                    if (best < 0f || len < best) best = len;
                }
                gapStart = -1;
            }
        }
        return best;
    }

    static bool HasSolid(Color[] p, int x, int y)
    {
        return x > 0 && y > 0 && x < CellSize && y < CellSize
            && p[y * CellSize + x].a > 0.5f;
    }

    /// <summary>画参考网格：外框亮一点，三等分线暗一点</summary>
    static void DrawGuides(Color[] px, int W, int H, int ox, int oy, int size)
    {
        Color frame = new Color(0.35f, 0.40f, 0.50f, 1f);
        Color mid = new Color(0.20f, 0.23f, 0.30f, 1f);

        for (int g = 0; g < Guides.Length; g++)
        {
            float v = Guides[g];
            bool isFrame = (g == 0 || g == Guides.Length - 1);
            Color c = isFrame ? frame : mid;

            int cx = ox + Mathf.RoundToInt(v * (size - 1));
            int cy = oy + Mathf.RoundToInt(v * (size - 1));

            // 竖线（贴图 y 与字形 y 反向，但竖线位置与方向无关）
            for (int y = 0; y < size; y++)
            {
                int ty = oy + y;
                if (cx >= 0 && cx < W && ty >= 0 && ty < H)
                    px[ty * W + cx] = c;
            }
            // 横线
            for (int x = 0; x < size; x++)
            {
                int tx = ox + x;
                if (tx >= 0 && tx < W && cy >= 0 && cy < H)
                    px[cy * W + tx] = c;
            }
        }
    }

    /// <summary>
    /// 报告"笔画端点落在主体框之外"的字。
    /// 汉字主体习惯落在 0.10~0.90，个别笔画略出头是正常的（比如 主 的点），
    /// 但大面积出格往往意味着比例失控。
    /// </summary>
    static void ReportOutOfBox(List<string> glyphs)
    {
        var bad = new List<string>();

        foreach (string ch in glyphs)
        {
            int segs = HanziRenderer.SegmentCount(ch);
            if (segs <= 0) continue;

            // 用渲染结果反推包围盒：找出所有不透明像素的范围
            Texture2D t = HanziRenderer.RenderGlyph(ch, CellSize);
            if (t == null) continue;

            Color[] p = t.GetPixels();
            int minX = CellSize, maxX = -1, minY = CellSize, maxY = -1;

            for (int y = 0; y < CellSize; y++)
            {
                for (int x = 0; x < CellSize; x++)
                {
                    if (p[y * CellSize + x].a < 0.5f) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            Object.DestroyImmediate(t);

            if (maxX < 0) { bad.Add($"{ch}(空)"); continue; }

            float bx0 = minX / (float)CellSize, bx1 = maxX / (float)CellSize;
            float by0 = minY / (float)CellSize, by1 = maxY / (float)CellSize;

            // 主体框 0.06 ~ 0.94 之外就算出格（留一点余量给本来就该出头的笔画）
            bool outX = bx0 < 0.055f || bx1 > 0.945f;
            bool outY = by0 < 0.055f || by1 > 0.945f;

            if (outX || outY)
                bad.Add($"{ch}[x {bx0:0.00}-{bx1:0.00} y {by0:0.00}-{by1:0.00}]");
        }

        if (bad.Count > 0)
            Debug.LogWarning($"[字形预览] 有 {bad.Count} 个字超出主体框：{string.Join(" ", bad)}");
        else
            Debug.Log("[字形预览] 所有字都在主体框内 ✓");
    }
}
#endif
