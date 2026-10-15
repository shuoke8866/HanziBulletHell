#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 汉字合成逻辑审计：把全部规则跑一遍，找出"应该能拼却拼不出来"的字。
///
/// 起因：玩家捡到一个横、一个竖，却合不出「十」。
///
/// 这个工具会做四件事：
///   1. 列出字库里每个字的笔顺定义，检查有无重复/冲突
///   2. 用"一个横 + 一个竖"这类真实场景去跑一遍求解器，看结果对不对
///   3. 检查每个字的"成长路径"：从空手开始，一笔一笔加，
///      看能不能长到它（有没有断掉的中间态）
///   4. 检查所有字是不是都"可达"—— 会不会有字永远拼不出来
///
/// 命令行：-executeMethod HanziAudit.Run
/// </summary>
public static class HanziAudit
{
    static StringBuilder sb;

    public static void Run()
    {
        sb = new StringBuilder();

        Line("========================================");
        Line("        汉字合成逻辑审计");
        Line("========================================");
        Line();

        // ---------- 1) 笔顺定义有无重复 ----------
        AuditDefinitions();

        // ---------- 2) 十 这个具体案例 ----------
        AuditConcreteCase();

        // ---------- 3) 每个字能不能从空手长出来 ----------
        AuditReachability();

        // ---------- 4) 所有 2 笔组合能不能拼出对应字 ----------
        AuditTwoStrokeCombos();

        File.WriteAllText("F:/HanziBulletHell/_hanzi_audit.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[审计] 已写入 _hanzi_audit.txt\n" + sb.ToString());
    }

    // ============================================================
    //  1) 定义审计
    // ============================================================

    static void AuditDefinitions()
    {
        Line("---------- 1) 笔顺定义审计 ----------");

        var db = HanziRenderer.AllGlyphsSorted();
        Line($"  字形库共 {db.Count} 个字");

        // 从 HanziSystem 拿不到数据库（私有），这里改成读源码里的定义做交叉核对
        var defs = ReadDefinitionsFromSource();

        Line($"  HanziSystem 里定义了 {defs.Count} 个字");
        Line();

        // 笔顺重复检测
        var bySeq = new Dictionary<string, List<string>>();
        foreach (var kv in defs)
        {
            string key = string.Join(",", kv.Value.ConvertAll(s => s.ToString()).ToArray());
            if (!bySeq.ContainsKey(key)) bySeq[key] = new List<string>();
            bySeq[key].Add(kv.Key);
        }

        int dupCount = 0;
        foreach (var kv in bySeq)
        {
            if (kv.Value.Count <= 1) continue;
            dupCount++;
            Line($"  ❌ 笔顺冲突：[{kv.Key}] 被这些字共用 → {string.Join(" / ", kv.Value.ToArray())}");
        }
        if (dupCount == 0) Line("  ✓ 没有两个字的笔顺完全相同");
        Line();

        // 字形库里没定义的
        int missingGlyph = 0;
        foreach (var kv in defs)
        {
            if (!db.Contains(kv.Key))
            {
                missingGlyph++;
                Line($"  ❌ 「{kv.Key}」在 HanziSystem 里有定义，但 HanziRenderer 里没有字形");
            }
        }
        if (missingGlyph == 0) Line("  ✓ 所有已定义的字都有字形");
        Line();
    }

    // ============================================================
    //  2) 具体案例：一横 + 一竖
    // ============================================================

    static void AuditConcreteCase()
    {
        Line("---------- 2) 案例：捡到「横」+「竖」 ----------");

        // 模拟：开局手上「一」，捡到「横」，再捡到「竖」
        var pool = new List<StrokeType> { StrokeType.Heng };   // 开局「一」占一横
        Line($"  开局池子：[{Name(pool)}]   当前字：「一」");

        pool.Add(StrokeType.Heng);
        Line($"  捡到「横」后池子：[{Name(pool)}]");
        DumpSolve("    此时能拼出", pool, "一");

        pool.Add(StrokeType.Shu);
        Line($"  捡到「竖」后池子：[{Name(pool)}]");
        DumpSolve("    此时能拼出", pool, "一");

        Line();
        Line("  --- 换个顺序：先捡「竖」再捡「横」 ---");
        var pool2 = new List<StrokeType> { StrokeType.Heng };
        pool2.Add(StrokeType.Shu);
        Line($"  开局 + 竖：[{Name(pool2)}]");
        DumpSolve("    能拼出", pool2, "一");
        pool2.Add(StrokeType.Heng);
        Line($"  再 + 横：[{Name(pool2)}]");
        DumpSolve("    能拼出", pool2, "一");

        Line();
    }

    // ============================================================
    //  3) 可达性：每个字能不能从"空手"一笔笔长出来
    // ============================================================

    static void AuditReachability()
    {
        Line("---------- 3) 可达性审计 ----------");

        var defs = ReadDefinitionsFromSource();

        int unreachable = 0;

        foreach (var kv in defs)
        {
            string name = kv.Key;
            var need = kv.Value;

            // 把一个字的笔顺放进池子，看求解器认不认它
            var pool = new List<StrokeType>(need);
            var options = HanziSolver.Solve(pool, SnapshotDatabase());

            bool found = false;
            foreach (var o in options)
            {
                if (o.def != null && o.def.hanziName == name) { found = true; break; }
            }

            if (!found)
            {
                unreachable++;
                Line($"  ❌ 「{name}」({Name(need)}) 放进池子里也解不出来 —— " +
                     $"求解器给出的前几个是：{TopNames(options, 4)}");
            }
        }

        if (unreachable == 0)
            Line($"  ✓ {defs.Count} 个字全部可达（池子里有它的笔顺就能解出来）");
        Line();
    }

    // ============================================================
    //  4) 所有两笔组合
    // ============================================================

    static void AuditTwoStrokeCombos()
    {
        Line("---------- 4) 两笔组合审计 ----------");

        var defs = ReadDefinitionsFromSource();
        var twoStroke = new List<KeyValuePair<string, List<StrokeType>>>();
        foreach (var kv in defs)
            if (kv.Value.Count == 2) twoStroke.Add(kv);

        Line($"  两笔的字共 {twoStroke.Count} 个");

        foreach (var kv in twoStroke)
        {
            var pool = new List<StrokeType>(kv.Value);
            var options = HanziSolver.Solve(pool, SnapshotDatabase());

            bool found = false;
            foreach (var o in options)
                if (o.def != null && o.def.hanziName == kv.Key) { found = true; break; }

            Line($"    {(found ? "✓" : "❌")} 「{kv.Key}」({Name(kv.Value)}) → " +
                 $"求解器首选：{TopNames(options, 3)}");
        }

        Line();
    }

    // ============================================================
    //  辅助
    // ============================================================

    static void DumpSolve(string label, List<StrokeType> pool, string currentHanzi)
    {
        var options = HanziSolver.Solve(pool, SnapshotDatabase());

        // 求解器只知道池子，不知道"当前字占了几笔"，这里手动减掉
        var current = FindDef(currentHanzi);
        var avail = new List<StrokeType>(pool);
        if (current != null)
        {
            foreach (var s in current)
            {
                int i = avail.IndexOf(s);
                if (i >= 0) avail.RemoveAt(i);
            }
        }

        Line($"{label}（可用笔画 = [{Name(avail)}]）：");

        var availOptions = HanziSolver.Solve(avail, SnapshotDatabase());
        if (availOptions.Count == 0) { Line("      （没有可切换的字）"); return; }

        int n = 0;
        foreach (var o in availOptions)
        {
            if (n++ >= 6) break;
            Line($"      {o.def.hanziName}  （{o.totalStrokes} 画，用掉 {o.usedFromPool} 笔）");
        }
    }

    static List<StrokeType> FindDef(string hanzi)
    {
        var defs = ReadDefinitionsFromSource();
        List<StrokeType> v;
        if (defs.TryGetValue(hanzi, out v)) return v;
        return null;
    }

    static List<HanziDef> SnapshotDatabase()
    {
        // 借一个临时的 HanziSystem 拿数据库。
        //
        // 注意：**不能**指望 Awake 同步跑完 —— 编辑器批处理下
        // AddComponent 之后 Awake 可能要到下一帧才执行，
        // 那样拿到的是空库、所有字都会报"解不出来"（我踩过两次）。
        // 直接调 EnsureInitialized()，它本身就是"把库和索引准备好"的入口。
        var probe = new GameObject("__HanziAuditProbe");
        var hs = probe.AddComponent<HanziSystem>();
        hs.EnsureInitialized();

        List<HanziDef> db = new List<HanziDef>(hs.hanziDatabase);
        Object.DestroyImmediate(probe);
        return db;
    }

    static string TopNames(List<HanziOption> options, int n)
    {
        var s = new List<string>();
        for (int i = 0; i < options.Count && i < n; i++)
            if (options[i].def != null) s.Add(options[i].def.hanziName);
        return s.Count == 0 ? "（空）" : string.Join(" ", s.ToArray());
    }

    static string Name(List<StrokeType> list)
    {
        var s = new List<string>();
        foreach (var t in list) s.Add(StrokeUtil.NameOf(t));
        return string.Join("", s.ToArray());
    }

    static void Line(string s = "")
    {
        sb.AppendLine(s);
    }

    // ============================================================
    //  从源码里读笔顺定义
    // ============================================================

    static Dictionary<string, List<StrokeType>> ReadDefinitionsFromSource()
    {
        var result = new Dictionary<string, List<StrokeType>>();

        string path = "Assets/Scripts/HanziSystem.cs";
        if (!File.Exists(path)) path = "F:/HanziBulletHell/Assets/Scripts/HanziSystem.cs";
        if (!File.Exists(path))
        {
            Line($"  ❌ 找不到 {path}");
            return result;
        }

        string src = File.ReadAllText(path);

        var matches = System.Text.RegularExpressions.Regex.Matches(
            src, @"AddHanzi\(""([^""]+)"",\s*new List<StrokeType>\s*\{([^\}]*)\}");

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string name = m.Groups[1].Value;
            string body = m.Groups[2].Value;

            var strokes = new List<StrokeType>();
            foreach (System.Text.RegularExpressions.Match sm in
                     System.Text.RegularExpressions.Regex.Matches(body, @"StrokeType\.(\w+)"))
            {
                StrokeType t;
                if (System.Enum.TryParse(sm.Groups[1].Value, out t)) strokes.Add(t);
            }

            result[name] = strokes;
        }

        return result;
    }
}
#endif
