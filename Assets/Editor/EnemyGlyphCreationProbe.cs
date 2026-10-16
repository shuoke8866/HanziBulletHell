#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 实测"从零画一个敌人字形"这条链路。
///
/// 玩家反馈"敌人编辑器应该多一个增加笔画的选项，不然都是空的" ——
/// 这个工具验证：空字库 → 加笔画 → 能渲染 → 玩家字库不受影响。
///
/// 命令行：-executeMethod EnemyGlyphCreationProbe.Run
/// </summary>
public static class EnemyGlyphCreationProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 敌人字形创建链路实测 ==========");
        sb.AppendLine();

        // ---------- 1) 起点：敌人字库是空的 ----------
        int before = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetEnemy).Count;
        sb.AppendLine("--- 1) 起点 ---");
        sb.AppendLine($"      敌人字库已有 {before} 个字");
        sb.AppendLine($"      敌人可选字表 {HanziRenderer.EnemyGlyphNames.Length} 个");
        sb.AppendLine();

        // 挑一个词表里有、但还没画的字
        string target = null;
        foreach (string g in HanziRenderer.EnemyGlyphNames)
        {
            if (!HanziRenderer.IsAllowed(g, HanziRenderer.SetEnemy)) { target = g; break; }
        }

        if (target == null)
        {
            sb.AppendLine("      敌人字库里所有字都画过了，跳过创建测试");
            Done(sb);
            return;
        }

        sb.AppendLine($"--- 2) 挑一个没画过的字：「{target}」---");
        sb.AppendLine($"      IsAllowed(enemy) = {HanziRenderer.IsAllowed(target, HanziRenderer.SetEnemy)}");
        sb.AppendLine($"      DebugStrokes(enemy) = " +
                      $"{(HanziRenderer.DebugStrokes(target, HanziRenderer.SetEnemy) == null ? "null（还不存在）" : "有数据")}");
        sb.AppendLine();

        // ---------- 3) 模拟"加笔画"：横 + 竖 = 十 ----------
        sb.AppendLine("--- 3) 加两笔（横 + 竖），模拟编辑器的新增笔画按钮 ---");

        var strokes = new List<HanziRenderer.StrokeInfo>
        {
            new HanziRenderer.StrokeInfo
            {
                kind = "H", thickness = 0.078f,
                pts = new List<Vector2> { new Vector2(0.15f, 0.50f), new Vector2(0.85f, 0.50f) }
            },
            new HanziRenderer.StrokeInfo
            {
                kind = "V", thickness = 0.078f,
                pts = new List<Vector2> { new Vector2(0.50f, 0.15f), new Vector2(0.50f, 0.85f) }
            }
        };

        bool ok = HanziRenderer.ApplyStrokes(target, strokes, HanziRenderer.SetEnemy);
        sb.AppendLine($"      ApplyStrokes 结果 = {(ok ? "成功" : "**失败**")}");

        int after = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetEnemy).Count;
        sb.AppendLine($"      敌人字库现在有 {after} 个字（之前 {before}）");
        sb.AppendLine();

        // ---------- 4) 渲染出来看看到底有没有内容 ----------
        sb.AppendLine("--- 4) 渲染验证 ---");
        Texture2D tex = HanziRenderer.RenderGlyph(target, 64, default(HanziRenderer.GlyphStyle),
                                                  HanziRenderer.SetEnemy);
        int opaque = CountOpaque(tex);
        sb.AppendLine($"      敌人库「{target}」渲染出的非空像素 = {opaque} / {64 * 64}");
        sb.AppendLine(opaque > 0
            ? "      ✓ 画出来了"
            : "      ✗ **渲染出来是空的** —— 加笔画没生效");
        sb.AppendLine();

        // ---------- 5) 玩家字库有没有被影响 ----------
        sb.AppendLine("--- 5) 玩家字库有没有被影响（关键）---");

        var playerStrokes = HanziRenderer.DebugStrokes(target, HanziRenderer.SetPlayer);
        sb.AppendLine($"      玩家库「{target}」= " +
                      $"{(playerStrokes == null ? "不存在" : playerStrokes.Count + " 笔")}");

        Texture2D ptex = HanziRenderer.RenderGlyph(target, 64, default(HanziRenderer.GlyphStyle),
                                                   HanziRenderer.SetPlayer);
        sb.AppendLine($"      玩家库「{target}」渲染像素 = {CountOpaque(ptex)}");
        sb.AppendLine("      （玩家库该是多少还是多少 —— 加敌人字形不会动它）");
        sb.AppendLine();

        // ---------- 6) 再试 RebuildAll，看敌人库会不会被清掉 ----------
        sb.AppendLine("--- 6) RebuildAll() 之后敌人库还在不在 ---");
        sb.AppendLine("      （这一条是回归测试：原来 RebuildAll 只重建玩家库，");
        sb.AppendLine("        导致字形编辑器里'丢弃改动重载'之后敌人的编辑看起来没保存）");

        HanziRenderer.RebuildAll();

        int afterRebuild = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetEnemy).Count;
        sb.AppendLine($"      RebuildAll 之后敌人字库有 {afterRebuild} 个字");

        // 注意：RebuildAll 会从**源码**重建，而源码里还没有这个字
        //（ApplyStrokes 只改内存，要点"写入源码"才落盘）。
        // 所以这里期望的是"回到源码状态"，也就是 before。
        sb.AppendLine(afterRebuild == before
            ? $"      ✓ 回到源码状态（{before} 个字）—— 符合预期，" +
              "内存里的临时改动被丢弃，源码里的没丢"
            : $"      ! 数量 = {afterRebuild}（期望 {before}）");

        sb.AppendLine();
        sb.AppendLine("      说明：ApplyStrokes 只改**内存**。要让改动留下来，");
        sb.AppendLine("            必须在编辑器里点「写入源码」把它落到 EnemyGlyphs.cs。");

        Done(sb);
    }

    static int CountOpaque(Texture2D tex)
    {
        if (tex == null) return -1;
        int n = 0;
        foreach (Color c in tex.GetPixels()) if (c.a > 0.5f) n++;
        return n;
    }

    static void Done(StringBuilder sb)
    {
        File.WriteAllText("F:/HanziBulletHell/_enemyglyph.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[敌人字形实测]\n" + sb.ToString());
    }
}
#endif
