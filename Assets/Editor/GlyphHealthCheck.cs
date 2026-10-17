#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 字形体检：每个字的**笔画数**。
///
/// ⚠ 注意：这个工具拿标准汉字笔画数做参考，
/// 但本游戏**故意**用「点」表示钩和提（六笔画限制），
/// 所以很多字比标准多 1~2 笔 —— 那是设计，不是错误。
/// 真正要看的是：两个版本的**同一字**笔画数是否变了。
///
/// 用「汉字标准笔画数」做基准（常见字的表），
/// 对不上就说明定义被污染了（吃到了别的字的笔画）。
///
/// 命令行：-executeMethod GlyphHealthCheck.Run
/// </summary>
public static class GlyphHealthCheck
{
    // 标准笔画数（常用字，用于校验）
    static readonly Dictionary<string, int> Std = new Dictionary<string, int>
    {
        {"一",1},{"丨",1},{"丿",1},{"乀",1},{"丶",1},{"乛",1},
        {"二",2},{"十",2},{"丁",2},{"厂",2},{"人",2},{"八",2},{"入",2},{"卜",2},{"了",2},{"刀",2},
        {"三",3},{"干",3},{"土",3},{"工",3},{"士",3},{"大",3},{"丈",3},{"川",3},{"上",3},{"下",3},
        {"口",3},{"山",3},{"小",3},{"才",3},{"兀",3},{"刃",3},
        {"王",4},{"丰",4},{"木",4},{"天",4},{"夫",4},{"太",4},{"犬",4},{"日",4},{"中",4},
        {"井",4},{"五",4},{"不",4},{"水",4},{"火",4},{"少",4},{"手",4},{"分",4},
        {"玉",5},{"主",5},{"生",5},{"由",5},{"甲",5},{"申",5},{"田",5},{"白",5},{"目",5},
        {"古",5},{"右",5},{"石",5},{"本",5},{"末",5},{"未",5},{"术",5},{"甘",5},{"左",5},{"正",5},
        {"百",6},{"圭",6},{"早",6},{"曲",6},{"舌",6},{"再",6},{"在",6},{"全",6},{"年",6},
        {"朱",6},{"米",6},{"吕",6},{"回",6},{"交",6},{"光",6},{"先",6},{"有",6},{"尖",6},{"尘",6},{"乐",5},
        {"里",7},{"豆",7},{"甫",7},{"坐",7},{"杏",7},{"呆",7},{"束",7},{"更",7},{"我",7},{"利",7},
        {"兵",7},{"见",4},{"見",7},
        {"直",8},{"林",8},{"昌",8},{"京",8},{"其",8},{"采",8},{"官",8},{"奉",8},{"具",8},
        {"音",9},{"是",9},{"品",9},{"相",9},{"查",9},{"胃",9},
        {"真",10},{"皇",9},
    };

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字形体检（笔画数）==========");
        sb.AppendLine();

        var all = HanziRenderer.AllGlyphsSorted(HanziRenderer.SetPlayer);

        var bad = new List<string>();
        var unknown = new List<string>();
        var ok = 0;

        foreach (string ch in all)
        {
            var si = HanziRenderer.DebugStrokes(ch, HanziRenderer.SetPlayer);
            if (si == null) { bad.Add($"{ch}（读不出）"); continue; }

            int actual = si.Count;

            if (!Std.ContainsKey(ch)) { unknown.Add($"{ch}({actual}笔)"); continue; }

            int expect = Std[ch];

            if (actual != expect)
            {
                bad.Add($"{ch}: {actual} 笔（应为 {expect}）与标准笔画数不同（可能是设计如此）");
            }
            else ok++;
        }

        sb.AppendLine($"--- 笔画数正确：{ok} 个 ---");
        sb.AppendLine();
        sb.AppendLine($"--- ✗ 笔画数不对：{bad.Count} 个 ---");
        foreach (string b in bad) sb.AppendLine("  " + b);
        sb.AppendLine();
        sb.AppendLine($"--- 没有标准值可对比：{unknown.Count} 个 ---");
        sb.AppendLine("  " + string.Join(" ", unknown.ToArray()));
        sb.AppendLine();

        sb.AppendLine("--- 全部字的实际笔画数 ---");
        foreach (string ch in all)
        {
            var si = HanziRenderer.DebugStrokes(ch, HanziRenderer.SetPlayer);
            int n = si != null ? si.Count : -1;
            string mark = "";
            if (Std.ContainsKey(ch) && n != Std[ch]) mark = "  ✗";
            sb.AppendLine($"  {ch}: {n} 笔{mark}");
        }

        File.WriteAllText("F:/HanziBulletHell/_health.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[字形体检]\n" + sb.ToString());
    }
}
#endif
