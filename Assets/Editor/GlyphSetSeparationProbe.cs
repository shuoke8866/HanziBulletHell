#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 实测：玩家字库和敌人字库**确实是两套独立的数据**。
///
/// 玩家反馈的歧义："我编辑敌人的样子，不能导致玩家的样子也变了"。
/// 所以这里直接改一遍敌人字库里的「一」，再看玩家字库里的「一」有没有被动到。
///
/// 命令行：-executeMethod GlyphSetSeparationProbe.Run
/// </summary>
public static class GlyphSetSeparationProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字库隔离实测 ==========");
        sb.AppendLine();

        // ---------- 1) 两套字库的规模 ----------
        int playerCount = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetPlayer).Count;
        int enemyCount = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetEnemy).Count;

        sb.AppendLine("--- 1) 两套字库 ---");
        sb.AppendLine($"      玩家字库：{playerCount} 个字");
        sb.AppendLine($"      敌人字库：{enemyCount} 个字（一开始是空的，等你画）");
        sb.AppendLine($"      敌人可选字表：{HanziRenderer.EnemyGlyphNames.Length} 个");
        sb.AppendLine();

        // ---------- 2) 记下玩家「一」现在的样子 ----------
        var playerBefore = HanziRenderer.DebugStrokes("一", HanziRenderer.SetPlayer);
        sb.AppendLine("--- 2) 改动前 ---");
        sb.AppendLine($"      玩家字库「一」：{Describe(playerBefore)}");
        sb.AppendLine();

        // ---------- 3) 往**敌人字库**写一个完全不同的「一」 ----------
        var enemyStrokes = new List<HanziRenderer.StrokeInfo>
        {
            new HanziRenderer.StrokeInfo
            {
                kind = "H", thickness = 0.078f,
                pts = new List<Vector2> { new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f) }
            }
        };

        bool ok = HanziRenderer.ApplyStrokes("一", enemyStrokes, HanziRenderer.SetEnemy);
        sb.AppendLine("--- 3) 往敌人字库写一个斜的「一」 ---");
        sb.AppendLine($"      写入结果：{(ok ? "成功" : "失败")}");

        var enemyAfter = HanziRenderer.DebugStrokes("一", HanziRenderer.SetEnemy);
        sb.AppendLine($"      敌人字库「一」：{Describe(enemyAfter)}");
        sb.AppendLine();

        // ---------- 4) 看玩家那个有没有被动到 ----------
        var playerAfter = HanziRenderer.DebugStrokes("一", HanziRenderer.SetPlayer);

        sb.AppendLine("--- 4) 玩家字库有没有被影响（关键） ---");
        sb.AppendLine($"      玩家字库「一」：{Describe(playerAfter)}");

        bool same = SameStrokes(playerBefore, playerAfter);
        sb.AppendLine();
        sb.AppendLine(same
            ? "      ✓ 玩家字库**没有被改动** —— 两套确实是独立的"
            : "      ✗ 玩家字库被连累了！隔离失败");
        sb.AppendLine();

        // ---------- 5) 渲染出来对比 ----------
        sb.AppendLine("--- 5) 渲染出的贴图是否不同 ---");
        Texture2D pTex = HanziRenderer.RenderGlyph("一", 32, default(HanziRenderer.GlyphStyle),
                                                   HanziRenderer.SetPlayer);
        Texture2D eTex = HanziRenderer.RenderGlyph("一", 32, default(HanziRenderer.GlyphStyle),
                                                   HanziRenderer.SetEnemy);

        sb.AppendLine($"      玩家贴图非空像素：{CountOpaque(pTex)} / {32 * 32}");
        sb.AppendLine($"      敌人贴图非空像素：{CountOpaque(eTex)} / {32 * 32}");

        // ---------- 6) 破损风格会不会误伤玩家 ----------
        sb.AppendLine();
        sb.AppendLine("--- 6) 破损风格只作用于敌人 ---");
        Texture2D plain = HanziRenderer.RenderGlyph("木", 32, default(HanziRenderer.GlyphStyle),
                                                    HanziRenderer.SetPlayer);
        Texture2D broken = HanziRenderer.RenderGlyph("木", 32,
            new HanziRenderer.GlyphStyle { corruption = 0.5f, jitter = 0.3f, spin = 15f },
            HanziRenderer.SetEnemy);

        sb.AppendLine($"      玩家「木」（完整）：{CountOpaque(plain)} 像素");
        sb.AppendLine($"      敌人「木」（残缺 0.5）：{CountOpaque(broken)} 像素");
        sb.AppendLine("      （破损是渲染时按概率丢笔画，不改动字库数据本身，");
        sb.AppendLine("        所以同一个字形可以同时有\"完整的玩家版\"和\"残缺的敌人版\"）");

        File.WriteAllText("F:/HanziBulletHell/_glyphsep.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[字库隔离]\n" + sb.ToString());
    }

    static string Describe(List<HanziRenderer.StrokeInfo> strokes)
    {
        if (strokes == null) return "(没有这个字)";
        string s = $"{strokes.Count} 笔 [";
        for (int i = 0; i < strokes.Count; i++)
        {
            if (i > 0) s += ", ";
            s += strokes[i].kind + "(";
            for (int k = 0; k < strokes[i].pts.Count; k++)
            {
                if (k > 0) s += "→";
                s += $"{strokes[i].pts[k].x:0.##},{strokes[i].pts[k].y:0.##}";
            }
            s += ")";
        }
        return s + "]";
    }

    static bool SameStrokes(List<HanziRenderer.StrokeInfo> a, List<HanziRenderer.StrokeInfo> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].pts.Count != b[i].pts.Count) return false;
            for (int k = 0; k < a[i].pts.Count; k++)
                if (Vector2.Distance(a[i].pts[k], b[i].pts[k]) > 0.0001f) return false;
        }
        return true;
    }

    static int CountOpaque(Texture2D tex)
    {
        if (tex == null) return -1;
        int n = 0;
        Color[] px = tex.GetPixels();
        foreach (Color c in px) if (c.a > 0.5f) n++;
        return n;
    }
}
#endif
