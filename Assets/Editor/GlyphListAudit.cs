#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 实测字形编辑器能读到的字表，并排查"新字不显示"的原因。
///
/// 命令行：-executeMethod GlyphListAudit.Run
/// </summary>
public static class GlyphListAudit
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字形列表审计 ==========");
        sb.AppendLine();

        // 1) AllGlyphsSorted 返回什么
        var player = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetPlayer);
        sb.AppendLine($"--- 玩家字库（AllGlyphsSorted）= {player.Count} 个 ---");
        sb.AppendLine("  " + string.Join(" ", player.ToArray()));
        sb.AppendLine();

        // 2) 检查重复
        var seen = new HashSet<string>();
        var dups = new List<string>();
        foreach (string c in player) if (!seen.Add(c)) dups.Add(c);

        if (dups.Count > 0)
            sb.AppendLine("  ⚠ 重复的字: " + string.Join(" ", dups.ToArray()));
        else
            sb.AppendLine("  ✓ 无重复");
        sb.AppendLine();

        // 3) 新增的 9 个在不在
        string[] targets = { "夫", "有", "分", "刃", "犬", "井", "古", "右", "左" };
        sb.AppendLine("--- 新增 9 字是否在列表中 ---");
        foreach (string c in targets)
            sb.AppendLine($"  {c}: {(player.Contains(c) ? "✓ 在" : "✗ **不在**")}");
        sb.AppendLine();

        // 4) 用户说被删掉的两个
        sb.AppendLine("--- 豆 / 士 是否在列表中 ---");
        foreach (string c in new[] { "豆", "士" })
            sb.AppendLine($"  {c}: {(player.Contains(c) ? "✓ 在" : "✗ **不在**")}");
        sb.AppendLine();

        // 5) DebugStrokes 能否读出这 9 个
        sb.AppendLine("--- 新增 9 字的笔画 ---");
        foreach (string c in targets)
        {
            var si = HanziRenderer.DebugStrokes(c, HanziRenderer.SetPlayer);
            if (si == null) { sb.AppendLine($"  {c}: **读不出来**"); continue; }
            sb.AppendLine($"  {c}: {si.Count} 笔");
        }
        sb.AppendLine();

        // 6) 敌人字库
        var enemy = HanziRenderer.EnemyGlyphNames;
        sb.AppendLine($"--- 敌人字库（标称表）= {enemy.Length} 个 ---");
        sb.AppendLine("  " + string.Join(" ", enemy));
        sb.AppendLine();

        // 7) 关键：缓存何时被清
        sb.AppendLine("--- 缓存状态 ---");
        sb.AppendLine("  HanziRenderer 的字库字典 `sets` 是静态缓存，");
        sb.AppendLine("  **源码改了之后必须调 ClearAllCaches() 或重启编辑器**，");
        sb.AppendLine("  否则编辑器窗口读到的还是旧列表。");
        sb.AppendLine();

        // 强制清一次缓存再看
        HanziRenderer.ClearAllCaches();
        var after = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetPlayer);
        sb.AppendLine($"  清理缓存后重新读 = {after.Count} 个");

        var added = new List<string>();
        var lost = new List<string>();
        foreach (string c in after) if (!player.Contains(c)) added.Add(c);
        foreach (string c in player) if (!after.Contains(c)) lost.Add(c);

        if (added.Count == 0 && lost.Count == 0)
            sb.AppendLine("  → 和清理前完全一样（说明缓存不是问题）");
        else
        {
            if (added.Count > 0) sb.AppendLine("  → 清理后新增: " + string.Join(" ", added.ToArray()));
            if (lost.Count > 0) sb.AppendLine("  → 清理后消失: " + string.Join(" ", lost.ToArray()));
        }

        File.WriteAllText("F:/HanziBulletHell/_glyphaudit.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[字形审计]\n" + sb.ToString());
    }
}
#endif
