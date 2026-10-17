// 用「相邻间距」而不是「坐标变异」来评估笔画分布。
//
// 上一版的算法缺陷：直接对笔画的 y 坐标算变异系数，
// 把"笔画长短不同"误判成了"间距不均"（比如「三」三横长度 0.48/0.36/0.80，
// 但间距 0.28/0.28 是完全均匀的）。
//
// 正确做法：**排序后算相邻间距**，再看这些间距是否均匀。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class GlyphSpacingAnalysis2
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 笔画间距分析 v2（相邻间距）==========");
        sb.AppendLine();

        List<string> all = HanziRenderer.AllGlyphsSorted();

        sb.AppendLine("指标说明：");
        sb.AppendLine("  横CV = 相邻横线间距的变异系数。0 = 完全均匀。");
        sb.AppendLine("  竖CV = 相邻竖线间距的变异系数。");
        sb.AppendLine("  （只统计平行且整条的笔画，折线不算）");
        sb.AppendLine();

        var rows = new List<string[]>();

        foreach (string ch in all)
        {
            List<HanziRenderer.StrokeInfo> si = HanziRenderer.DebugStrokes(ch, HanziRenderer.SetPlayer);
            if (si == null || si.Count == 0) continue;

            var hys = new List<float>();
            var vxs = new List<float>();
            float minX = 9f, maxX = -9f, minY = 9f, maxY = -9f;

            foreach (var s in si)
            {
                if (s.pts == null || s.pts.Count < 2) continue;

                foreach (var p in s.pts)
                {
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }

                if (s.kind == "H") hys.Add(s.pts[0].y);
                if (s.kind == "V") vxs.Add(s.pts[0].x);
            }

            float cvH = GapCv(hys);
            float cvV = GapCv(vxs);

            rows.Add(new[]
            {
                ch,
                hys.Count.ToString(),
                cvH < 0 ? "-" : cvH.ToString("0.00"),
                vxs.Count.ToString(),
                cvV < 0 ? "-" : cvV.ToString("0.00"),
                minX.ToString("0.00"),
                (1f - maxX).ToString("0.00"),
                minY.ToString("0.00"),
                (1f - maxY).ToString("0.00"),
            });
        }

        int badH = 0, badV = 0, tight = 0;

        foreach (string[] r in rows)
        {
            if (r[2] != "-" && float.Parse(r[2]) > 0.30f) badH++;
            if (r[4] != "-" && float.Parse(r[4]) > 0.30f) badV++;

            // 边距过窄（<0.05）算贴边；过宽（>0.30）算缩成一团
            float l = float.Parse(r[5]), rr = float.Parse(r[6]);
            float b = float.Parse(r[7]), t = float.Parse(r[8]);
            if (l < 0.05f || rr < 0.05f || b < 0.05f || t < 0.05f) tight++;
        }

        sb.AppendLine($"横间距不均（CV>0.30）: {badH} / {rows.Count}");
        sb.AppendLine($"竖间距不均（CV>0.30）: {badV} / {rows.Count}");
        sb.AppendLine($"有笔画贴边（边距<0.05）: {tight} / {rows.Count}");
        sb.AppendLine();

        // 按横CV 降序排，最差的先看
        rows.Sort((a, b) =>
        {
            float va = a[2] == "-" ? -1 : float.Parse(a[2]);
            float vb = b[2] == "-" ? -1 : float.Parse(b[2]);
            return vb.CompareTo(va);
        });

        sb.AppendLine("--- 横间距最不均的（最该检查）---");
        sb.AppendLine();
        sb.AppendLine("| 字 | 横数 | 横CV | 竖CV | 左 | 右 | 下 | 上 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < Mathf.Min(18, rows.Count); i++)
        {
            string[] r = rows[i];
            sb.AppendLine($"| {r[0]} | {r[1]} | {r[2]} | {r[4]} | {r[5]} | {r[6]} | {r[7]} | {r[8]} |");
        }

        sb.AppendLine();
        sb.AppendLine("--- 贴边严重的（边距<0.05，会顶到字面边缘）---");
        sb.AppendLine();
        sb.AppendLine("| 字 | 左 | 右 | 下 | 上 |");
        sb.AppendLine("|---|---|---|---|---|");

        int shown = 0;
        foreach (string[] r in rows)
        {
            float l = float.Parse(r[5]), rr = float.Parse(r[6]);
            float b = float.Parse(r[7]), t = float.Parse(r[8]);
            if (l < 0.05f || rr < 0.05f || b < 0.05f || t < 0.05f)
            {
                sb.AppendLine($"| {r[0]} | {r[5]} | {r[6]} | {r[7]} | {r[8]} |");
                if (++shown >= 15) break;
            }
        }

        File.WriteAllText("F:/HanziBulletHell/_spacing2.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[间距分析 v2]\n" + sb.ToString());
    }

    /// <summary>排序后算相邻间距的变异系数</summary>
    static float GapCv(List<float> v)
    {
        if (v.Count < 3) return -1f;    // 少于 3 条谈不上"间距均匀性"

        v.Sort();

        var gaps = new List<float>();
        for (int i = 0; i < v.Count - 1; i++)
            gaps.Add(v[i + 1] - v[i]);

        // 去掉过小/过大的离群间距（可能是叠图层，不是真的间隔）
        var clean = new List<float>();
        foreach (float g in gaps)
            if (g > 0.04f && g < 0.60f) clean.Add(g);

        if (clean.Count < 2) return -1f;

        float sum = 0f;
        foreach (float g in clean) sum += g;
        float mean = sum / clean.Count;
        if (mean < 1e-5f) return -1f;

        float var = 0f;
        foreach (float g in clean) var += (g - mean) * (g - mean);
        var /= clean.Count;

        return Mathf.Sqrt(var) / mean;
    }
}
