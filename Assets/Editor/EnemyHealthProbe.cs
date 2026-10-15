#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 两项实测：
///   1. 小怪在游戏里真实的血量（查数据库 + 层数倍率 + 房间覆盖）
///   2. 汉字成长路径：从「一」开始一笔一笔捡，每一步能变成什么
///
/// 命令行：-executeMethod EnemyHealthProbe.Run
/// </summary>
public static class EnemyHealthProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();

        sb.AppendLine("========== 1) 小怪实际血量 ==========");
        sb.AppendLine();
        sb.AppendLine("  怪         数据库  第1层   第2层   第3层");
        sb.AppendLine("  ------------------------------------------");

        foreach (EnemyDef def in EnemyDatabase.All)
        {
            sb.Append("  " + Pad(def.displayName, 12) + Pad(def.health.ToString(), 8));

            for (int floor = 1; floor <= 3; floor++)
            {
                float mul = 1f + (floor - 1) * 0.30f;
                int hp = Mathf.Max(1, Mathf.RoundToInt(def.health * mul));
                sb.Append(Pad(hp.ToString(), 8));
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("  层数血量倍率：第1层 x1.00  第2层 x1.30  第3层 x1.60");
        sb.AppendLine();

        // ---------- 2) 成长路径 ----------
        sb.AppendLine("========== 2) 汉字成长路径 ==========");
        sb.AppendLine();

        var probe = new GameObject("__GlyphProbe");
        var hs = probe.AddComponent<HanziSystem>();
        hs.EnsureInitialized();

        // 从**真实的开机状态**开始，别用手搓的池子 ——
        // GameData 是静态的，会带着上一次测试的残留；
        // 手搓出来的基线不对，测出来的结论也是错的（我在这上面栽过）。
        GameData.ResetAll();
        GameData.currentFloor = 1;
        GameData.inTutorial = false;
        GameData.BeginRealRun(StrokeType.Heng);
        hs.ApplyOpeningStarter(StrokeType.Heng);

        // 依次捡 竖 / 横 / 横 / 竖
        // 一路捡到能凑出「木」（横竖撇捺），再看还能换什么
        var seq = new List<StrokeType>
        {
            StrokeType.Shu, StrokeType.Pie, StrokeType.Na, StrokeType.Heng
        };

        sb.AppendLine($"  开局：当前「{hs.currentHanziName}」  池子=[{Name(hs.AllStrokes())}]");
        sb.AppendLine();

        foreach (StrokeType s in seq)
        {
            // 走真实入口：入池 → 再问能不能切
            hs.AddStrokeToPool(s);

            var avail = hs.AvailableStrokes();

            sb.AppendLine($"  捡到「{StrokeUtil.NameOf(s)}」 池子=[{Name(hs.AllStrokes())}]");
            sb.AppendLine($"      当前「{hs.currentHanziName}」占 [{Name(hs.CurrentCharacterStrokes())}]");
            sb.AppendLine($"      合成池（全部收集到的）=[{Name(hs.SynthesisStrokes())}]" +
                          $"   空闲=[{Name(avail)}]");

            var full = new List<StrokeType>(hs.CurrentCharacterStrokes());
            full.AddRange(avail);

            var matches = hs.FindAllMatchingHanzi(full);
            string names = "";
            foreach (var d in matches)
            {
                if (d.hanziName == hs.currentHanziName) continue;
                if (names.Length > 0) names += " / ";
                names += d.hanziName;
            }

            sb.AppendLine($"      成长后可拼 = [{Name(full)}]  → 可切换：{(names.Length > 0 ? names : "（无）")}");

            var formable = hs.GetAllFormable();
            string list = "";
            foreach (var o in formable)
            {
                if (list.Length > 0) list += " ";
                list += o.def.hanziName;
            }
            sb.AppendLine($"      背包列出：{(list.Length > 0 ? list : "（空）")}");

            if (matches.Count > 0)
            {
                var target = matches[0];
                if (hs.SwitchTo(target))
                    sb.AppendLine($"      ✓ 切换到「{hs.currentHanziName}」");
                else
                    sb.AppendLine($"      ✗ 切换到「{target.hanziName}」失败");
            }
            sb.AppendLine();
        }

        Object.DestroyImmediate(probe);

        File.WriteAllText("F:/HanziBulletHell/_probe.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[实测]\n" + sb.ToString());
    }

    static string Name(List<StrokeType> list)
    {
        string s = "";
        foreach (StrokeType t in list) s += StrokeUtil.NameOf(t);
        return s;
    }

    static string Pad(string s, int n)
    {
        while (s.Length < n) s += " ";
        return s;
    }
}
#endif
