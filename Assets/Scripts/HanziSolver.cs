using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个候选汉字的评估结果。
///
/// 用于"最优解"排序：先看它用到了多少个「池中出现最多的那种笔画」，
/// 再看它能消耗掉池里多少笔画，最后看总笔画数（越复杂越强）。
/// </summary>
public class HanziOption
{
    public HanziDef def;

    /// <summary>该字用到了几个「池中出现最多的那种笔画」</summary>
    public int keyStrokeCount;

    /// <summary>该字能从池子里消耗掉的总笔画数</summary>
    public int usedFromPool;

    /// <summary>该字的总笔画数</summary>
    public int totalStrokes;

    public string Name { get { return def != null ? def.hanziName : "?"; } }
}

/// <summary>
/// 汉字"最优解"求解器。
///
/// 取代了原来的"笔顺逐步成长"规则（要求目标 = 当前 +1 笔、且当前笔顺是目标的
/// 有序子序列）。旧规则有两个致命问题：
///   1. 起始字恒为「一」，而 一 的笔顺不是 丨/丿/丶 的子序列，
///      于是这 3 个字以及只能从它们长出来的 人/水/火/刀 共 7 个字永远不可达；
///   2. 完全忽略玩家手上已有的其他笔画，导致"捡了什么"对结果影响很小。
///
/// 新规则只看"手上有什么笔画"：
///   1. 找出池子里出现次数最多的笔画类型（并列时都算）
///   2. 候选字 = 能由池子里的一部分笔画拼出、且用该「关键笔画」最多的字
///   3. 同分时按"能消耗池子里的笔画总数" → "总笔画数" 排序
///   4. 如果没有任何字用到关键笔画，则回退为"池子里能拼出的所有字"里最复杂的
///
/// 关键设计：笔画类型顺序（横竖撇捺点折）与字典遍历顺序都不影响结果 ——
/// 排序在关键笔画数并列时一律用「消耗池子笔画数」打破平局，结果确定且顺序无关。
/// </summary>
public static class HanziSolver
{
    /// <summary>统计池子里每种笔画的出现次数</summary>
    public static Dictionary<StrokeType, int> CountTypes(List<StrokeType> pool)
    {
        var counts = new Dictionary<StrokeType, int>();
        if (pool == null) return counts;

        foreach (StrokeType s in pool)
        {
            int c;
            counts.TryGetValue(s, out c);
            counts[s] = c + 1;
        }
        return counts;
    }

    /// <summary>
    /// 池子里出现次数最多的那种笔画，以及它的次数。
    /// 括号里带上"哪些笔画并列最多"，供 UI 提示用。
    /// </summary>
    public static int MaxFrequency(List<StrokeType> pool, out List<StrokeType> keyTypes)
    {
        keyTypes = new List<StrokeType>();
        Dictionary<StrokeType, int> counts = CountTypes(pool);

        int best = 0;
        foreach (var kv in counts)
            if (kv.Value > best) best = kv.Value;

        if (best > 0)
        {
            foreach (var kv in counts)
                if (kv.Value == best) keyTypes.Add(kv.Key);
        }

        return best;
    }

    /// <summary>
    /// 目标字的笔画能不能从池子里凑出来（多重集包含），
    /// 并给出用到了几个关键笔画、消耗了池子里的几个笔画。
    /// </summary>
    static bool TryForm(HanziDef def, List<StrokeType> pool,
                        List<StrokeType> keyTypes,
                        out int keyCount, out int usedFromPool)
    {
        keyCount = 0;
        usedFromPool = 0;

        if (def == null || def.strokes == null || def.strokes.Count == 0) return false;

        // 池子的可消耗副本
        var remaining = new List<StrokeType>(pool);

        foreach (StrokeType s in def.strokes)
        {
            int idx = remaining.IndexOf(s);
            if (idx < 0) return false;      // 池子里没有这一笔，凑不出来
            remaining.RemoveAt(idx);
            usedFromPool++;

            if (keyTypes != null && keyTypes.Contains(s)) keyCount++;
        }

        return true;
    }

    /// <summary>
    /// 求解当前进度下可切换的全部候选汉字，**最优解排在第一个**。
    ///
    /// 返回空列表表示池子里的笔画凑不出任何字。
    /// </summary>
    public static List<HanziOption> Solve(List<StrokeType> pool,
                                          IReadOnlyList<HanziDef> database)
    {
        var result = new List<HanziOption>();
        if (pool == null || pool.Count == 0 || database == null) return result;

        List<StrokeType> keyTypes;
        int maxFreq = MaxFrequency(pool, out keyTypes);

        // 先收集"能用关键笔画"的候选
        var byKey = new List<HanziOption>();
        // 兜底：完全用不上关键笔画、但能从池子里拼出来的字
        var fallback = new List<HanziOption>();

        foreach (HanziDef def in database)
        {
            int keyCount, used;
            if (!TryForm(def, pool, keyTypes, out keyCount, out used)) continue;

            var opt = new HanziOption
            {
                def = def,
                keyStrokeCount = keyCount,
                usedFromPool = used,
                totalStrokes = def.strokes.Count
            };

            if (keyCount > 0) byKey.Add(opt);
            else fallback.Add(opt);
        }

        List<HanziOption> list = byKey.Count > 0 ? byKey : fallback;

        // 排序：关键笔画数 ↓ → 剩余未用关键笔画数 ↑ → 消耗池子笔画数 ↓ → 总笔画数 ↓ → 名字
        //
        // 第二项的用意：池子里常常有"用不完"的笔画（比如 4 个横），
        // 这时应当优先选那个把笔画吃得更干净的字 —— 否则会出现
        // "手上 4 横 1 竖，最优解却是只吃 3 横的 王，白白剩 1 横"。
        // 排序键与字典遍历顺序、笔画类型顺序都无关，结果确定。
        list.Sort((a, b) =>
        {
            if (a.keyStrokeCount != b.keyStrokeCount)
                return b.keyStrokeCount.CompareTo(a.keyStrokeCount);

            int aLeft = maxFreq - a.keyStrokeCount;
            int bLeft = maxFreq - b.keyStrokeCount;
            if (aLeft != bLeft) return aLeft.CompareTo(bLeft);   // 剩得少优先

            if (a.usedFromPool != b.usedFromPool)
                return b.usedFromPool.CompareTo(a.usedFromPool);
            if (a.totalStrokes != b.totalStrokes)
                return b.totalStrokes.CompareTo(a.totalStrokes);
            return string.CompareOrdinal(a.Name, b.Name);
        });

        return list;
    }

    /// <summary>
    /// 把候选集里"与最优解同复杂度"的字都挑出来 ——
    /// 也就是关键笔画数、消耗笔画数、总笔画数三项都相同的那些字。
    ///
    /// 需求里的"同笔画数的并列字也算最优解"指的就是这一组。
    /// </summary>
    public static List<HanziOption> TiedWithBest(List<HanziOption> ranked)
    {
        var tied = new List<HanziOption>();
        if (ranked == null || ranked.Count == 0) return tied;

        HanziOption best = ranked[0];
        foreach (HanziOption o in ranked)
        {
            if (o.keyStrokeCount != best.keyStrokeCount) break;
            if (o.usedFromPool != best.usedFromPool) break;
            if (o.totalStrokes != best.totalStrokes) break;
            tied.Add(o);
        }
        return tied;
    }
}
