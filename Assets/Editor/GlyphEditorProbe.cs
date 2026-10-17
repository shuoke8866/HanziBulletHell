#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 实测字形编辑器用到的每个调用，找出哪一步抛异常。
///
/// 命令行：-executeMethod GlyphEditorProbe.Run
/// </summary>
public static class GlyphEditorProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字形编辑器依赖链 实测 ==========");
        sb.AppendLine();

        // 1) 字库能否构建
        sb.AppendLine("--- 1. 字库构建 ---");
        List<string> all = null;
        try
        {
            all = HanziRenderer.AllGlyphsSorted();
            sb.AppendLine($"  AllGlyphsSorted() = {all.Count} 个 ✓");
        }
        catch (System.Exception e)
        {
            sb.AppendLine($"  ✗ AllGlyphsSorted 抛异常: {e.GetType().Name}: {e.Message}");
            File.WriteAllText("F:/HanziBulletHell/_glyphed.txt", sb.ToString(), Encoding.UTF8);
            return;
        }

        // 2) 逐个查 DebugStrokes（编辑器打开时会对选中的字调用）
        sb.AppendLine();
        sb.AppendLine("--- 2. 每个字的 DebugStrokes ---");

        var bad = new List<string>();
        for (int i = 0; i < all.Count; i++)
        {
            string ch = all[i];
            try
            {
                List<HanziRenderer.StrokeInfo> si = HanziRenderer.DebugStrokes(ch, HanziRenderer.SetPlayer);
                if (si == null) { bad.Add($"{ch}(null)"); continue; }

                // 检查每条笔画的顶点数
                for (int k = 0; k < si.Count; k++)
                {
                    if (si[k].pts == null || si[k].pts.Count < 2)
                        bad.Add($"{ch}#{k}(点数={si[k].pts?.Count})");
                    if (si[k].kind == null)
                        bad.Add($"{ch}#{k}(kind=null)");
                }
            }
            catch (System.Exception e)
            {
                bad.Add($"{ch}(异常:{e.GetType().Name})");
            }
        }

        if (bad.Count == 0)
            sb.AppendLine($"  全部 {all.Count} 个字的笔画都能正常读出 ✓");
        else
        {
            sb.AppendLine($"  ✗ 有 {bad.Count} 处问题：");
            foreach (string b in bad) sb.AppendLine("      " + b);
        }

        // 3) 逐个查 CreateSpriteFor（预览会用）
        sb.AppendLine();
        sb.AppendLine("--- 3. 每个字的贴图生成 ---");

        var badTex = new List<string>();
        foreach (string ch in all)
        {
            try
            {
                Sprite sp = HanziRenderer.CreateSpriteFor(ch, 128, 32f);
                if (sp == null) badTex.Add(ch);
            }
            catch (System.Exception e)
            {
                badTex.Add($"{ch}({e.GetType().Name})");
            }
        }

        if (badTex.Count == 0)
            sb.AppendLine($"  全部 {all.Count} 个字的贴图都能生成 ✓");
        else
        {
            sb.AppendLine($"  ⚠ 有 {badTex.Count} 个字的贴图生成失败：");
            foreach (string b in badTex) sb.AppendLine("      " + b);
        }

        // 4) 新增 15 字逐个检查笔画数
        sb.AppendLine();
        sb.AppendLine("--- 4. 新增 15 字的笔画明细 ---");
        string[] targets = { "才", "求", "永", "乐", "光", "先", "尖", "尘", "我", "利", "見", "豆", "坐", "具", "胃" };

        foreach (string ch in targets)
        {
            try
            {
                List<HanziRenderer.StrokeInfo> si = HanziRenderer.DebugStrokes(ch, HanziRenderer.SetPlayer);
                if (si == null) { sb.AppendLine($"  {ch}: **读不出来**"); continue; }

                int minPts = 99, maxPts = 0;
                foreach (var s in si)
                {
                    if (s.pts == null) continue;
                    if (s.pts.Count < minPts) minPts = s.pts.Count;
                    if (s.pts.Count > maxPts) maxPts = s.pts.Count;
                }

                sb.AppendLine($"  {ch}: {si.Count} 笔，顶点数 {minPts}~{maxPts}");
            }
            catch (System.Exception e)
            {
                sb.AppendLine($"  {ch}: **异常** {e.GetType().Name}: {e.Message}");
            }
        }

        File.WriteAllText("F:/HanziBulletHell/_glyphed.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[字形编辑器]\n" + sb.ToString());
    }
}
#endif
