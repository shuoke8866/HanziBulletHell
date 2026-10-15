#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 按新的玩家有效 DPS 重新给小怪配血量，并**直接写进 EnemyDatabase**。
///
/// ## 为什么必须重配
///
/// 汉字强度刚做过归一化，玩家 DPS 的量级整个变了：
///
///   旧模型：1 画 0.27 → 5 画 1.11（弹数手写 + 整除摊薄）
///   新模型：1 画 4.7  → 9 画 46.2（弹数归一 + 单发反比，已计命中率）
///
/// 小怪血量还是照旧 DPS 配的，所以现在一切都死得太快。
///
/// ## 有效 DPS 怎么算的
///
/// 不能直接用"弹数 × 单发 / 间隔"——那假设每一发都命中同一个敌人，
/// 但扇形/环形弹幕打单体时大部分会飞走。所以按形状给了命中率：
///
///   1发直线 70%   2发 55%   3~4发扇形 35%   5~6发环形 25%   8~10发 20%
///
/// 得到：1画 4.7 / 2画 7.5 / 3画 14.5 / 4画 24.6 / 5画 21.4
///       6画 25.4 / 7画 29.6 / 8画 40.8 / 9画 46.2
///
/// ## 目标击杀秒数
///
///   冲刺怪 1.0（血薄但快，靠速度施压）
///   基础杂兵 1.3
///   中等 1.8    肉盾/炮台 2.2
///   弹幕墙怪 3.0  精英 4.0
///
/// 命令行：-executeMethod EnemyHealthBalancer.Run
/// </summary>
public static class EnemyHealthBalancer
{
    /// <summary>各弹数的命中率（打单体时的典型值）</summary>
    static float HitRate(int bullets)
    {
        if (bullets <= 1) return 0.70f;
        if (bullets == 2) return 0.55f;
        if (bullets <= 4) return 0.35f;
        if (bullets <= 6) return 0.25f;
        return 0.20f;
    }

    /// <summary>某个笔画数的字，玩家的**有效** DPS</summary>
    static float EffectiveDps(int strokes)
    {
        int std = GameBalance.BulletCountFor(strokes);
        int dmg = GameBalance.DamageFor(strokes, std, std);
        float interval = GameBalance.FireIntervalFor(strokes);
        float raw = std * dmg / interval;
        return raw * HitRate(std);
    }

    /// <summary>每种怪的目标击杀秒数</summary>
    static readonly Dictionary<string, float> Ttk = new Dictionary<string, float>
    {
        { "yi",         1.3f },
        { "shu",        1.3f },
        { "kou",        2.2f },
        { "ren",        1.0f },
        { "shi",        2.2f },
        { "bing",       1.8f },
        { "mu",         1.8f },
        { "tian",       3.0f },
        { "elite_wang", 4.0f },
        { "elite_huo",  3.5f },
        { "elite_shui", 3.5f }
    };

    /// <summary>每种怪出场的那一层（和定义里的 minFloor 保持一致）</summary>
    static readonly Dictionary<string, int> MinFloor = new Dictionary<string, int>
    {
        { "yi", 1 }, { "shu", 1 }, { "kou", 1 },
        { "ren", 2 }, { "shi", 2 }, { "bing", 2 },
        { "mu", 3 }, { "tian", 3 },
        { "elite_wang", 3 }, { "elite_huo", 3 }, { "elite_shui", 3 }
    };

    /// <summary>出场那一层，玩家大概是什么水平（笔画数）</summary>
    static int TypicalStrokesForFloor(int floor)
    {
        if (floor <= 1) return 2;
        if (floor == 2) return 3;
        return 5;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 小怪血量重配 ==========");
        sb.AppendLine();

        sb.AppendLine("玩家有效 DPS（已计命中率）：");
        for (int s = 1; s <= 9; s++)
            sb.AppendLine($"  {s} 画 → {EffectiveDps(s):0.0}");
        sb.AppendLine();

        sb.AppendLine("  怪           旧血   新血   目标秒   依据(该层玩家DPS)");
        sb.AppendLine("  ----------------------------------------------------------");

        var newHealth = new Dictionary<string, int>();

        foreach (var kv in Ttk)
        {
            string id = kv.Key;
            int floor = MinFloor[id];
            int strokes = TypicalStrokesForFloor(floor);
            float dps = EffectiveDps(strokes);
            int hp = Mathf.Max(2, Mathf.RoundToInt(kv.Value * dps));

            newHealth[id] = hp;

            int old = OldHealth(id);
            sb.AppendLine($"  {Pad(id, 12)} {Pad(old.ToString(), 6)} {Pad(hp.ToString(), 6)} " +
                          $"{Pad(kv.Value.ToString("0.#"), 8)} {strokes}画 {dps:0.0} DPS");
        }

        sb.AppendLine();

        int changed = WriteToSource(newHealth, sb);
        sb.AppendLine($"写入 EnemyDatabase：改动 {changed} 处");

        Debug.Log("[配血] " + sb.ToString());
    }

    static int OldHealth(string id)
    {
        var m = new Dictionary<string, int>
        {
            { "yi", 7 }, { "shu", 8 }, { "kou", 18 }, { "ren", 5 }, { "shi", 15 },
            { "bing", 10 }, { "mu", 14 }, { "tian", 24 },
            { "elite_wang", 60 }, { "elite_huo", 45 }, { "elite_shui", 40 }
        };
        return m.ContainsKey(id) ? m[id] : 0;
    }

    static string Pad(string s, int n)
    {
        while (s.Length < n) s += " ";
        return s;
    }

    /// <summary>把血量写回 EnemyDatabase.cs 的 health = N</summary>
    static int WriteToSource(Dictionary<string, int> health, StringBuilder sb)
    {
        const string path = "F:/HanziBulletHell/Assets/Scripts/EnemyDatabase.cs";
        if (!System.IO.File.Exists(path))
        {
            sb.AppendLine("  ❌ 找不到 " + path);
            return 0;
        }

        string src = System.IO.File.ReadAllText(path);
        int changed = 0;

        foreach (var kv in health)
        {
            // 定位这个怪的 id 行，然后在它之后找第一个 health = N
            string needle = "id = \"" + kv.Key + "\",";
            int idx = src.IndexOf(needle);
            if (idx < 0) { sb.AppendLine($"  ⚠ 找不到定义 {kv.Key}"); continue; }

            var rx = new System.Text.RegularExpressions.Regex(@"health = (\d+)");
            var m = rx.Match(src, idx);
            if (!m.Success) { sb.AppendLine($"  ⚠ {kv.Key} 没有 health 字段"); continue; }

            // 只改这一个匹配
            string before = src.Substring(0, m.Index);
            string after = src.Substring(m.Index + m.Length);
            src = before + "health = " + kv.Value + after;
            changed++;
        }

        System.IO.File.WriteAllText(path, src, new System.Text.UTF8Encoding(false));
        return changed;
    }
}
#endif
