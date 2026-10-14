using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HanziDef
{
    public string hanziName;             // 汉字名称
    public List<StrokeType> strokes;     // 笔画组合
    public List<string> attackTags;      // 攻击特征标签（继承体现）
    public int bulletCount;              // 弹幕数量
    public float bulletSpeed;            // 弹幕速度
    public int damage;                   // 单发伤害
    public float fireRate;               // 射击间隔（秒）
    public float bulletScale;            // 弹幕大小倍率
    public string description;           // 描述
}

public class HanziSystem : MonoBehaviour
{
    [Header("当前状态")]
    public List<StrokeType> currentStrokes = new List<StrokeType>();
    public string currentHanziName = "一";

    [Header("弹幕预制体")]
    public GameObject bulletPrefab;

    [Header("汉字数据库")]
    public List<HanziDef> hanziDatabase = new List<HanziDef>();

    private HanziDef currentHanziDef;

    void Awake()
    {
        BuildDatabase();
        BuildIndex();

        // 如果 GameData 中有保存的数据，恢复它
        if (GameData.currentStrokes != null && GameData.currentStrokes.Count > 0)
        {
            currentStrokes = new List<StrokeType>(GameData.currentStrokes);
            GameLog.Info("从 GameData 恢复笔画，共 " + currentStrokes.Count + " 个");
        }
        else
        {
            currentStrokes.Clear();
            currentStrokes.Add(StrokeType.Heng);
            // 就地写入，保持 GameData 里的 List 引用不变（其他系统可能持有引用）
            SaveStrokesToGameData();
        }

        UpdateCurrentHanzi();

        // 进场时同步字形：切层／死亡重开后画面上的汉字也要正确
        SyncCharacterGlyph();
    }

    // 统一出口：就地更新 GameData.currentStrokes，避免替换整个 List 导致引用失效
    void SaveStrokesToGameData()
    {
        if (GameData.currentStrokes == null)
            GameData.currentStrokes = new List<StrokeType>();

        GameData.currentStrokes.Clear();
        GameData.currentStrokes.AddRange(currentStrokes);
    }

    // 死亡重开：把汉字重置回"一"，并清空仓库
    public void ResetToDefaultHanzi()
    {
        EnsureInitialized();

        currentStrokes.Clear();
        currentStrokes.Add(StrokeType.Heng);
        currentHanziDef = null;
        SaveStrokesToGameData();

        // 永久池与已合成列表一并清空，否则重开会带着上一局积累的笔画
        Pool.Clear();
        Pool.Add(StrokeType.Heng);

        if (GameData.formedHanzi == null) GameData.formedHanzi = new List<string>();
        GameData.formedHanzi.Clear();
        GameData.formedHanzi.Add("一");

        StrokeInventory inv = GetComponent<StrokeInventory>();
        if (inv != null && inv.strokes != null)
        {
            inv.strokes.Clear();
            inv.SaveToGameData();
        }

        UpdateCurrentHanzi();
        SyncCharacterGlyph();

        GameLog.Info("汉字已重置为「一」");
    }

    void BuildDatabase()
    {
        hanziDatabase = new List<HanziDef>();

        // ============================================================
        // 六种笔画：横 竖 撇 捺 点 折
        //
        // 规则一（匹配）：按**笔顺**严格比较，每个字的笔画序列唯一。
        // 规则二（成长）：当前字的笔顺必须是目标字的**有序子序列**，
        //                 所以"王(一丨一) + 点 = 主(丶一丨一)"成立。
        // 规则三（数值）：单发伤害 = 笔画数，**不随弹数叠加**；
        //                 强度只靠弹数成长，避免"弹数 × 伤害"双重爆炸。
        //
        // 成长路径（加一笔向外长）：
        //   一 → 十/丁      二 → 三/工/干/土/王
        //   口 → 日/中/田    大 → 太
        //   王 → 主/玉/生    曰/日 → 白/田
        // ============================================================

        // ---------- 1 画 ----------
        AddHanzi("一",
            new List<StrokeType> { StrokeType.Heng },
            new List<string> { "straight" },
            1, 12f, "1发直线");

        AddHanzi("丨",
            new List<StrokeType> { StrokeType.Shu },
            new List<string> { "straight" },
            1, 12f, "1发重弹");

        AddHanzi("丿",
            new List<StrokeType> { StrokeType.Pie },
            new List<string> { "vshape" },
            1, 12f, "1发斜射");

        AddHanzi("丶",
            new List<StrokeType> { StrokeType.Dian },
            new List<string> { "straight" },
            1, 12f, "1发点射");

        // ---------- 2 画 ----------
        AddHanzi("二",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng },
            new List<string> { "parallel" },
            2, 11f, "2发平行");

        AddHanzi("十",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu },
            new List<string> { "cross" },
            4, 10f, "4发十字");

        AddHanzi("人",
            new List<StrokeType> { StrokeType.Pie, StrokeType.Na },
            new List<string> { "vshape" },
            2, 11f, "V形双向");

        AddHanzi("丁",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu },
            new List<string> { "cross" },
            2, 11f, "2发重弹");

        // ---------- 3 画 ----------
        AddHanzi("三",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Heng },
            new List<string> { "parallel" },
            3, 11f, "3发平行");

        AddHanzi("大",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Pie, StrokeType.Na },
            new List<string> { "scatter" },
            5, 10f, "5发扇形");

        AddHanzi("工",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "parallel" },
            4, 11f, "4发平行");

        AddHanzi("干",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Shu },
            new List<string> { "parallel", "pierce" },
            3, 10f, "3发穿透");

        AddHanzi("士",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "cross" },
            4, 11f, "4发十字");

        AddHanzi("土",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "ring" },
            6, 9f, "6发环形");

        AddHanzi("口",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng },
            new List<string> { "ring" },
            6, 9f, "6发环形");

        AddHanzi("兀",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Pie, StrokeType.Zhe },
            new List<string> { "scatter" },
            5, 10f, "5发扇形");

        // ---------- 4 画 ----------
        AddHanzi("王",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "parallel", "pierce" },
            4, 10f, "4发穿透");

        AddHanzi("木",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu, StrokeType.Pie, StrokeType.Na },
            new List<string> { "omni" },
            6, 9f, "6发全方位");

        AddHanzi("天",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Pie, StrokeType.Na },
            new List<string> { "parallel", "vshape" },
            5, 10f, "平行 + 斜向");

        AddHanzi("太",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Pie, StrokeType.Na, StrokeType.Dian },
            new List<string> { "scatter", "shotgun" },
            6, 9.5f, "扇形 + 霰弹");

        AddHanzi("日",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng, StrokeType.Heng },
            new List<string> { "ring", "cross" },
            6, 9f, "6发环形十字");

        AddHanzi("中",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng, StrokeType.Shu },
            new List<string> { "ring", "pierce" },
            6, 9f, "6发环形穿透");

        AddHanzi("水",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Pie, StrokeType.Na },
            new List<string> { "cross", "diagonal" },
            6, 9f, "6发斜射");

        AddHanzi("火",
            new List<StrokeType> { StrokeType.Dian, StrokeType.Dian, StrokeType.Pie, StrokeType.Na },
            new List<string> { "vshape", "shotgun" },
            6, 9f, "6发近距霰弹");

        // ---------- 5 画 ----------
        AddHanzi("主",
            new List<StrokeType> { StrokeType.Dian, StrokeType.Heng, StrokeType.Heng,
                                   StrokeType.Shu, StrokeType.Heng },
            new List<string> { "parallel", "pierce", "ring" },
            8, 9f, "8发穿透环形");

        AddHanzi("玉",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Shu,
                                   StrokeType.Heng, StrokeType.Dian },
            new List<string> { "parallel", "pierce", "shotgun" },
            8, 9f, "8发穿透霰弹");

        AddHanzi("白",
            new List<StrokeType> { StrokeType.Pie, StrokeType.Shu, StrokeType.Zhe,
                                   StrokeType.Heng, StrokeType.Heng },
            new List<string> { "ring", "pierce" },
            8, 8.5f, "8发环形穿透");

        AddHanzi("田",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng,
                                   StrokeType.Heng, StrokeType.Shu },
            new List<string> { "ring", "cross" },
            8, 8.5f, "8发环形十字");

        AddHanzi("目",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng,
                                   StrokeType.Heng, StrokeType.Heng },
            new List<string> { "ring", "parallel" },
            8, 8.5f, "8发环形平行");

        // ---------- 6 画 ----------
        AddHanzi("圭",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Shu, StrokeType.Heng,
                                   StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "parallel", "pierce", "ring" },
            8, 8.5f, "8发穿透环形");

        AddHanzi("里",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng, StrokeType.Heng,
                                   StrokeType.Shu, StrokeType.Heng, StrokeType.Heng },
            new List<string> { "omni", "pierce" },
            8, 8.5f, "8发全方位穿透");

        // ---------- 9 画：终极形态 ----------
        AddHanzi("皇",
            new List<StrokeType> { StrokeType.Pie, StrokeType.Shu, StrokeType.Zhe,
                                   StrokeType.Heng, StrokeType.Heng,
                                   StrokeType.Heng, StrokeType.Heng, StrokeType.Shu, StrokeType.Heng },
            new List<string> { "omni", "ring", "pierce" },
            10, 9f, "皇：10发全方位穿透");

        // ---------- 补充：字形库里已画好但此前没有弹幕定义的字 ----------
        // 这些字的折线坐标早就写在 HanziRenderer 里，只是漏了数据库登记，
        // 于是玩家永远拼不出来。补上之后可合成字数从 32 提到 38。

        AddHanzi("八",
            new List<StrokeType> { StrokeType.Pie, StrokeType.Na },
            new List<string> { "vshape" },
            2, 11.5f, "八：2发分开斜射");

        AddHanzi("刀",
            new List<StrokeType> { StrokeType.Zhe, StrokeType.Pie },
            new List<string> { "scatter" },
            3, 10.5f, "刀：3发扇形劈砍");

        AddHanzi("上",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Heng, StrokeType.Heng },
            new List<string> { "parallel" },
            3, 11f, "上：3发平行");

        AddHanzi("下",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Shu, StrokeType.Dian },
            new List<string> { "cross", "shotgun" },
            4, 10f, "下：4发十字霰弹");

        AddHanzi("丰",
            new List<StrokeType> { StrokeType.Heng, StrokeType.Heng, StrokeType.Heng, StrokeType.Shu },
            new List<string> { "parallel", "pierce" },
            5, 10.5f, "丰：5发平行穿透");

        AddHanzi("由",
            new List<StrokeType> { StrokeType.Shu, StrokeType.Zhe, StrokeType.Heng,
                                   StrokeType.Shu, StrokeType.Heng },
            new List<string> { "ring", "cross" },
            6, 9.5f, "由：6发环状十字");
    }
    // 简化添加方法
    // 注意：不再显式传伤害 —— 单发伤害统一由"笔画数"推导，
    // 这样强度只随弹数成长，不会出现"弹数 × 伤害"双重爆炸。
    void AddHanzi(string name, List<StrokeType> strokes, List<string> tags,
                  int count, float speed, string desc)
    {
        hanziDatabase.Add(new HanziDef
        {
            hanziName = name,
            strokes = strokes,
            attackTags = tags,
            bulletCount = count,
            bulletSpeed = speed,
            damage = 0,      // 由 StrokeDamage 计算
            fireRate = 0.30f,
            bulletScale = 1f,
            description = desc
        });
    }

    // ========== 笔画池与最优解（新的核心机制）==========
    //
    // 取代原来的"笔顺逐步成长"：不再要求目标 = 当前 +1 笔，
    // 也不再看笔顺是不是目标的有序子序列，而是看"手上所有笔画能拼出什么"。

    /// <summary>
    /// 永久笔画池（本局捡到过的全部笔画，只增不减）。
    /// 保证引用始终有效 —— 外面可能持有这个 List。
    /// </summary>
    static List<StrokeType> Pool
    {
        get
        {
            if (GameData.allStrokes == null)
                GameData.allStrokes = new List<StrokeType>();
            return GameData.allStrokes;
        }
    }

    /// <summary>把一笔永久记入笔画池，并同步一份到背包（给 UI 显示）</summary>
    public void AddStrokeToPool(StrokeType s)
    {
        EnsureInitialized();

        Pool.Add(s);

        StrokeInventory inv = GetComponent<StrokeInventory>();
        if (inv != null && inv.strokes != null)
        {
            inv.strokes.Add(s);
            inv.SaveToGameData();
        }
    }

    /// <summary>
    /// **当前可用**的笔画 = 永久池 − 当前汉字已占用的笔画。
    ///
    /// 这是"能拼出什么字"的真正输入：当前字占着的那几笔不能同时用于拼别的字，
    /// 而一旦切走，那几笔就自动回到可用集合里 —— 所以切换是可逆的。
    /// </summary>
    public List<StrokeType> AvailableStrokes()
    {
        EnsureInitialized();

        var avail = new List<StrokeType>(Pool);

        if (currentStrokes != null)
        {
            foreach (StrokeType s in currentStrokes)
            {
                int i = avail.IndexOf(s);
                if (i >= 0) avail.RemoveAt(i);
            }
        }

        return avail;
    }

    /// <summary>
    /// 已有的全部笔画（含当前字占用的）。UI 显示"仓库里有什么"时用它。
    /// </summary>
    public List<StrokeType> AllStrokes()
    {
        EnsureInitialized();
        return new List<StrokeType>(Pool);
    }

    /// <summary>当前汉字占用的笔画（UI 上标出来，让玩家看到"这些被字占着"）</summary>
    public List<StrokeType> CurrentCharacterStrokes()
    {
        return currentStrokes != null ? new List<StrokeType>(currentStrokes) : new List<StrokeType>();
    }

    /// <summary>
    /// 兼容旧调用：返回"当前字 + 可用笔画"的全集。
    /// 求解最优解（拾取时自动变字）用它，因为那时当前字的笔画也参与重排。
    /// </summary>
    public List<StrokeType> BuildStrokePool()
    {
        EnsureInitialized();
        return AllStrokes();
    }

    /// <summary>
    /// 在当前进度下求解可切换的候选汉字，最优解排在第一个。
    /// 背包面板用它来展示「可切换的汉字」列表。
    ///
    /// 注意用的是 <see cref="AvailableStrokes"/>：候选必须是**不拆当前字**
    /// 就能拼出来的字。这样切过去之后，原来的字一定还能切回来。
    /// </summary>
    public List<HanziOption> GetSwitchCandidates()
    {
        EnsureInitialized();
        return HanziSolver.Solve(AvailableStrokes(), hanziDatabase);
    }

    /// <summary>
    /// 仓库里能拼出的**全部**汉字（含那些笔画数较少的），按复杂度排序。
    /// 合成台要"列出所有能拼出的字"就用它。
    /// </summary>
    public List<HanziOption> GetAllFormable()
    {
        EnsureInitialized();

        List<StrokeType> avail = AvailableStrokes();
        var result = new List<HanziOption>();

        foreach (HanziDef def in hanziDatabase)
        {
            if (def.strokes == null || def.strokes.Count == 0) continue;
            if (currentHanziDef != null && def.hanziName == currentHanziDef.hanziName) continue;

            // 能不能从可用笔画里凑出来
            var rest = new List<StrokeType>(avail);
            bool ok = true;
            foreach (StrokeType s in def.strokes)
            {
                int i = rest.IndexOf(s);
                if (i < 0) { ok = false; break; }
                rest.RemoveAt(i);
            }
            if (!ok) continue;

            result.Add(new HanziOption
            {
                def = def,
                keyStrokeCount = 0,
                usedFromPool = def.strokes.Count,
                totalStrokes = def.strokes.Count
            });
        }

        result.Sort((a, b) =>
        {
            if (a.totalStrokes != b.totalStrokes) return b.totalStrokes.CompareTo(a.totalStrokes);
            return string.CompareOrdinal(a.Name, b.Name);
        });

        return result;
    }

    /// <summary>
    /// 切换到指定汉字。
    ///
    /// 新模型（可逆）：
    ///   1. 从**可用笔画**里扣掉目标字需要的笔画
    ///   2. 当前字占用的笔画自动回到可用集合（不需要显式搬动）
    ///   3. 记录这个字已合成过
    ///
    /// 永久池永不缩小，所以「切过去的字一定能切回来」。
    /// 旧的实现会把背包清空重填、装不下的笔画直接丢弃 ——
    /// 这就是"越切越简单、最后只能拼出一"的根因。
    /// </summary>
    public bool SwitchTo(HanziDef target)
    {
        EnsureInitialized();

        if (target == null || target.strokes == null || target.strokes.Count == 0)
            return false;

        List<StrokeType> avail = AvailableStrokes();

        // 校验：可用笔画够不够拼出目标字
        var check = new List<StrokeType>(avail);
        foreach (StrokeType s in target.strokes)
        {
            int idx = check.IndexOf(s);
            if (idx < 0)
            {
                GameLog.Warn($"切换「{target.hanziName}」失败：可用笔画里没有 {StrokeUtil.NameOf(s)}");
                return false;
            }
            check.RemoveAt(idx);
        }

        // 扣掉目标字用到的笔画
        var left = new List<StrokeType>(avail);
        foreach (StrokeType s in target.strokes)
        {
            int idx = left.IndexOf(s);
            if (idx < 0) return false;
            left.RemoveAt(idx);
        }

        // 背包同步成"剩余可用笔画"，让 UI 看到正确的仓库
        StrokeInventory inv = GetComponent<StrokeInventory>();
        if (inv != null && inv.strokes != null)
        {
            inv.strokes.Clear();
            inv.strokes.AddRange(left);
            inv.SaveToGameData();
        }

        CommitHanzi(target);
        RecordFormed(target.hanziName);
        return true;
    }

    /// <summary>记下"这个字合成过"，供背包的汉字列表显示</summary>
    void RecordFormed(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (GameData.formedHanzi == null) GameData.formedHanzi = new List<string>();
        if (!GameData.formedHanzi.Contains(name)) GameData.formedHanzi.Add(name);
    }

    /// <summary>合成过的汉字（按顺序），UI 用</summary>
    public List<string> FormedHanziNames
    {
        get
        {
            if (GameData.formedHanzi == null) GameData.formedHanzi = new List<string>();
            return GameData.formedHanzi;
        }
    }

    /// <summary>按名字找字库里的定义</summary>
    public HanziDef FindByName(string name)
    {
        if (string.IsNullOrEmpty(name) || hanziDatabase == null) return null;
        foreach (HanziDef d in hanziDatabase)
            if (d.hanziName == name) return d;
        return null;
    }

    /// <summary>
    /// 切换到"能用上某一笔"的最复杂汉字（数字键用）。
    ///
    /// 找法是：在**可用笔画 + 这一笔**的前提下求解最优解集合，
    /// 再从中挑第一个"确实需要这一笔"的字。
    ///
    /// 注意这一笔来自仓库、本来就已在池子里，所以这里只是加个筛选条件，
    /// 不会改变笔画总量。返回 null 表示没有用得上它的字（调用方可以退回普通最优解）。
    /// </summary>
    public HanziDef SwitchToBestUsing(StrokeType stroke)
    {
        EnsureInitialized();

        List<HanziOption> ranked = GetSwitchCandidates();

        foreach (HanziOption opt in ranked)
        {
            if (opt.def == null || opt.def.strokes == null) continue;
            if (IsSameHanzi(opt.def, currentHanziDef)) continue;

            // 这个字必须真的用到这一笔
            if (!opt.def.strokes.Contains(stroke)) continue;

            if (SwitchTo(opt.def)) return opt.def;
        }

        return null;
    }

    /// <summary>
    /// 切换到"当前可用笔画能拼出的最复杂汉字"（不考虑某一笔）。
    /// 返回 null 表示没有可切换的目标。
    /// </summary>
    public HanziDef SwitchToBestAvailable()
    {
        EnsureInitialized();

        foreach (HanziOption opt in GetSwitchCandidates())
        {
            if (opt.def == null) continue;
            if (IsSameHanzi(opt.def, currentHanziDef)) continue;
            if (SwitchTo(opt.def)) return opt.def;
        }

        return null;
    }

    /// <summary>按名字直接切换（背包里点击"已合成的字"用），成功返回定义</summary>
    public HanziDef SwitchToName(string name)
    {
        HanziDef def = FindByName(name);
        if (def == null) return null;
        if (IsSameHanzi(def, currentHanziDef)) return def;   // 已经是这个字
        return SwitchTo(def) ? def : null;
    }

    /// <summary>
    /// 旧的 SwitchTo 实现留在下面这段注释里作对照，说明为什么改：
    ///
    ///     var remaining = BuildStrokePool();
    ///     foreach (StrokeType s in target.strokes) { ... remaining.RemoveAt(idx); }
    ///     inv.strokes.Clear();
    ///     foreach (StrokeType s in remaining)
    ///         if (inv.strokes.Count < inv.maxSlots) inv.strokes.Add(s);
    ///         else dropped++;                 // ← 这里在丢笔画
    ///
    /// 「池子 = 当前字 + 背包」且背包有上限，所以每切一次都要丢几笔，
    /// 切几次之后池子只剩「一」，再也回不去。
    /// </summary>
    



    /// <summary>
    /// 捡到一笔之后的自动变字规则（按需求确定）：
    ///
    ///   **只有当这一笔正好是"仓库里出现次数最多的那种笔画"时，才自动换字。**
    ///
    ///   理由：如果捡到的是一笔冷门笔画（比如仓库里全是横，捡到一个点），
    ///   强行重排只会把当前这个复杂的字拆散、换成更简单的字 —— 就是之前
    ///   玩家反馈的"退化"。所以这种情况**保持当前汉字**，笔画只进仓库，
    ///   等玩家自己在合成台里拼。
    ///
    /// 无论是哪种情况，这一笔都会永久记入笔画池（只增不减），
    /// 所以之后随时能拿它去拼别的字。
    /// </summary>
    public bool ApplyBestFor(StrokeType newStroke)
    {
        EnsureInitialized();

        // 1) 永久入池（这是"自由可逆切换"的关键）
        AddStrokeToPool(newStroke);

        // 2) 这一笔是不是当前出现最多的种类？
        List<StrokeType> afterPool = AllStrokes();
        List<StrokeType> keyTypes;
        int maxFreq = HanziSolver.MaxFrequency(afterPool, out keyTypes);

        bool isKeyStroke = keyTypes.Contains(newStroke);

        if (!isKeyStroke)
        {
            GameLog.Info($"「{StrokeUtil.NameOf(newStroke)}」不是当前最多的笔画种类，" +
                         $"保持「{currentHanziName}」，笔画存入仓库");
            return false;   // 保持现状，等玩家手动合成
        }

        // 3) 是最多的种类 → 自动切到可用笔画能拼出的最复杂汉字
        List<HanziOption> ranked = GetSwitchCandidates();

        foreach (HanziOption opt in ranked)
        {
            if (IsSameHanzi(opt.def, currentHanziDef)) continue;
            if (SwitchTo(opt.def)) return true;
        }

        return false;
    }

    static bool IsSameHanzi(HanziDef a, HanziDef b)
    {
        if (a == null || b == null) return false;
        return a.hanziName == b.hanziName;
    }

    // ========== 笔画应用 ==========
    // 结果类型：区分"成功/需要玩家选/失败"三种情况
    public enum StrokeResult
    {
        Success,      // 成功成长或合成
        NeedChoice,   // 有多个可选目标，需要玩家在 UI 里选
        Failed        // 这一笔现在用不上
    }

    /// <summary>
    /// 加一笔。优先按"字形包含"长大（王 + 点 → 主）；
    /// 当前字长不大时，再看能不能直接凑成一个完整汉字。
    /// </summary>
    public StrokeResult TryAddStrokeDetailed(StrokeType newStroke, out List<HanziDef> candidates)
    {
        // ---- 1) 先试"长大"：当前字的笔顺是目标字的有序子序列 ----
        List<HanziDef> growth = GetGrowthCandidates(newStroke);

        if (growth.Count > 0)
        {
            // 多个候选时不能随便挑：像 二+竖 既能变 干 也能变 王，
            // 但 干(一丨一) 之后再也长不动了（王 的竖在第二横之后，插不进去），
            // 挑错就把玩家卡死。所以优先挑**后续成长空间最大**的那个。
            HanziDef pick = GetPreferredGrowth(growth);
            if (pick == null) pick = PickBestGrowth(growth);

            CommitHanzi(pick);
            candidates = growth;
            return StrokeResult.Success;
        }

        // ---- 2) 长不大，看能不能直接拼出一个汉字 ----
        List<StrokeType> tempStrokes = new List<StrokeType>(currentStrokes);
        tempStrokes.Add(newStroke);
        List<HanziDef> exact = FindAllMatchingHanzi(tempStrokes);

        if (exact.Count == 1)
        {
            CommitHanzi(exact[0]);
            candidates = exact;
            return StrokeResult.Success;
        }

        if (exact.Count > 1)
        {
            candidates = exact;
            GameLog.Info($"笔画 [{StrokeUtil.NameOf(newStroke)}] 可直接组成 {exact.Count} 个汉字，等待玩家选择");
            return StrokeResult.NeedChoice;
        }

        candidates = new List<HanziDef>();
        GameLog.Info($"笔画 [{StrokeUtil.NameOf(newStroke)}] 对「{currentHanziName}」现在用不上，进背包");
        return StrokeResult.Failed;
    }

    // 提交一个汉字（不处理歧义）
    void CommitHanzi(HanziDef matched)
    {
        if (matched == null) return;

        currentStrokes = new List<StrokeType>(matched.strokes);
        currentHanziDef = matched;
        currentHanziName = matched.hanziName;

        SaveStrokesToGameData();
        ApplyStatsToPlayer();
        // 关键：合成成功后立刻把画面上的字形换成新汉字
        SyncCharacterGlyph();

        // 笔画特点：含横→穿透、含竖→护罩、含撇捺→斩击、含点→灼烧、含折→折返
        // 先确保能力组件已挂载（运行时挂载，避免手改场景）
        PlayerKit.EnsureAttached();
        if (traits == null) traits = GetComponent<StrokeTraits>();
        if (traits != null) traits.Refresh(matched);

        // 记入"已合成"清单，背包里会把它们列出来给玩家点击切换
        RecordFormed(matched.hanziName);

        // 当前字占用的笔画可能在池子里缺额（例如开局），补齐
        EnsurePoolCoversCurrentStrokes();

        GameToast.Show($"「{currentHanziName}」  {matched.description}", new Color(1f, 0.95f, 0.6f));

        GameLog.Info($"获得笔画！当前汉字：「{currentHanziName}」- {matched.description}");
    }

    StrokeTraits traits;

    // 兼容旧调用：有歧义时自动取第一个可变候选（不会卡住流程）
    public bool TryAddStroke(StrokeType newStroke)
    {
        // 新机制：拾取后按"手上所有笔画能拼出的最复杂汉字"自动切换，
        // 不再走旧的"笔顺 +1 笔成长"判定。
        return ApplyBestFor(newStroke);
    }
    // 根据笔画列表返回能组成的汉字名（不能组成则返回"未知"）
    public string GetPossibleHanziName(List<StrokeType> strokes)
    {
        HanziDef def = FindMatchingHanzi(strokes);
        return def != null ? def.hanziName : "未知";
    }

    HanziDef FindMatchingHanzi(List<StrokeType> strokes)
    {
        // 只用扫"同样笔画数"的那个桶
        foreach (HanziDef def in Bucket(strokes.Count))
        {
            if (StrokeListsMatch(def.strokes, strokes))
                return def;
        }
        return null;
    }

    // ========== 匹配：按笔顺严格相等 ==========
    // 旧版是无序多重集合（排序后比较），导致"王 + 点"永远拼不出"主"。
    // 现在按笔顺逐位比较，每个字的笔画序列都唯一。
    bool StrokeListsMatch(List<StrokeType> a, List<StrokeType> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    // ========== 笔画序列索引 ==========
    // 之前每个查询都要遍历整个数据库（34+ 个字，每个都跑一遍子序列匹配），
    // 而 QuickBarUI / BackpackUI 每帧都会查好几次 → 累积成可观的每帧开销。
    // 这里按"笔画数"分桶，查询时只扫同长度的桶。

    Dictionary<int, List<HanziDef>> byStrokeCount;

    /// <summary>
    /// 确保字库与索引已经建好。可以重复调用（幂等）。
    ///
    /// 正常运行时由 Awake 调用。之所以单独暴露出来，是因为批处理/编辑器脚本
    /// 里 MonoBehaviour 的 Awake 不一定会被 PlayerLoop 驱动到 —— 那样字库会是空的，
    /// 所有合成逻辑静默失效。有了这个入口，任何调用方都能先保证状态正确。
    /// </summary>
    public void EnsureInitialized()
    {
        if (hanziDatabase == null || hanziDatabase.Count == 0)
            BuildDatabase();

        if (byStrokeCount == null || byStrokeCount.Count == 0)
            BuildIndex();

        if (currentStrokes == null || currentStrokes.Count == 0)
        {
            currentStrokes = new List<StrokeType> { StrokeType.Heng };
            SaveStrokesToGameData();
        }

        // 永久笔画池至少要包含"当前字占用的笔画"，否则开局（「一」）
        // 会出现"池子里一笔都没有"的情况，导致首次切换全部失败。
        // 只补差额，不重复加。
        EnsurePoolCoversCurrentStrokes();

        UpdateCurrentHanzi();
    }

    /// <summary>
    /// 保证永久池里至少有够当前字用的那些笔画。
    /// 开局当前字是「一」，池子补上一笔横，之后切换才有东西可扣。
    /// </summary>
    void EnsurePoolCoversCurrentStrokes()
    {
        if (currentStrokes == null) return;

        var have = new List<StrokeType>(Pool);
        foreach (StrokeType s in currentStrokes)
        {
            int i = have.IndexOf(s);
            if (i >= 0) { have.RemoveAt(i); continue; }
            Pool.Add(s);          // 池子里缺这一笔，补上
        }
    }

    void BuildIndex()
    {
        byStrokeCount = new Dictionary<int, List<HanziDef>>();
        foreach (HanziDef def in hanziDatabase)
        {
            int n = def.strokes != null ? def.strokes.Count : 0;
            List<HanziDef> bucket;
            if (!byStrokeCount.TryGetValue(n, out bucket))
            {
                bucket = new List<HanziDef>();
                byStrokeCount[n] = bucket;
            }
            bucket.Add(def);
        }
    }

    // 取某个笔画数的所有字（没有则返回空表）
    List<HanziDef> Bucket(int strokeCount)
    {
        if (byStrokeCount == null) BuildIndex();
        List<HanziDef> b;
        if (byStrokeCount.TryGetValue(strokeCount, out b)) return b;
        return emptyBucket;
    }

    static readonly List<HanziDef> emptyBucket = new List<HanziDef>();

    // ========== 字形包含：加一笔向外长 ==========
    //
    // 汉字是"层层长大"的：王(一丨一) 加上一点就是 主(丶一丨一)；
    // 大 加上一点就是 太/犬；口 加一竖就是 中/日/田。
    //
    // 判定方式：当前字的笔画序列，必须是目标字序列的**有序子序列**；
    // 且新加的那一笔要落在"已匹配前缀之后"，表示它确实是往更大处长的下一笔。

    /// <summary>当前字跳过笔画顺序匹配 target 时，最后一个笔画的匹配位置（-1 表示匹配不上）</summary>
    static int MatchPrefixEnd(List<StrokeType> current, List<StrokeType> target)
    {
        if (current == null || target == null)
            return -1;

        int ti = 0;
        int last = -1;

        for (int ci = 0; ci < current.Count; ci++)
        {
            bool found = false;
            while (ti < target.Count)
            {
                if (target[ti] == current[ci]) { last = ti; ti++; found = true; break; }
                ti++;
            }
            if (!found) return -1;
        }
        return last;
    }

    /// <summary>
    /// 当前汉字 + 一笔能"长大"成哪些汉字。
    ///
    /// 三条规则缺一不可：
    ///   1) 目标字 = 当前字 **+1 笔**（强制一步一步长，杜绝跨级乱跳）
    ///   2) 当前字的笔顺是目标字的**有序子序列**（允许新笔画插在中间）
    ///   3) 目标字里确实含有这一笔
    ///
    /// 只用规则 1 会把 二 + 竖 → 王 堵死（王 比 二 多 2 笔）；
    /// 只用规则 2 会让 一 + 横 直接跳到 王（跳过中间的竖），反而把 三 + 竖 堵死。
    /// 两条一起用才既干净又不会卡死。
    /// </summary>
    public List<HanziDef> GetGrowthCandidates(StrokeType stroke)
    {
        var result = new List<HanziDef>();
        if (currentHanziDef == null) return result;

        List<StrokeType> cur = currentHanziDef.strokes;

        // 目标字必然是"当前笔画数 +1"，所以只看下一个桶
        foreach (HanziDef def in Bucket(cur.Count + 1))
        {
            // 规则 2：当前字的笔顺必须是目标字的有序子序列
            if (MatchPrefixEnd(cur, def.strokes) < 0) continue;

            // 规则 3：目标字里确实含有这一笔
            bool found = false;
            foreach (StrokeType s in def.strokes)
            {
                if (s == stroke) { found = true; break; }
            }
            if (found) result.Add(def);
        }

        return result;
    }

    /// <summary>从候选里找出玩家以前选过的那个（没选过则返回 null）</summary>
    HanziDef GetPreferredGrowth(List<HanziDef> growth)
    {
        if (GameData.hanziPreferences == null) return null;

        foreach (HanziDef g in growth)
        {
            string want;
            if (GameData.hanziPreferences.TryGetValue(StrokeKey(g.strokes), out want) &&
                want == g.hanziName)
                return g;
        }
        return null;
    }

    /// <summary>
    /// 在多个成长方向里挑最不容易把玩家卡死的那个。
    ///
    /// 只看"下一步有几个去向"是不够的：二 + 横 的 5 个候选（三/士/干/工/土）
    /// 下一步都只有 1 个去向，但 三 走一步就断了，士/干/工/土 还能一路长到 王。
    /// 所以这里递归算"这条路最远还能走多少步"（到终极形态的距离），
    /// 优先选走得最远的；同样远再选笔画少的。
    /// </summary>
    HanziDef PickBestGrowth(List<HanziDef> growth)
    {
        HanziDef best = null;
        int bestDepth = -1;
        int bestStrokes = int.MaxValue;

        foreach (HanziDef g in growth)
        {
            int depth = MaxGrowthDepth(g, 0);
            int strokes = g.strokes.Count;

            if (depth > bestDepth || (depth == bestDepth && strokes < bestStrokes))
            {
                best = g;
                bestDepth = depth;
                bestStrokes = strokes;
            }
        }
        return best;
    }

    // 从某个字出发，最多还能连续长几步（防止递归爆掉，限制深度）
    int MaxGrowthDepth(HanziDef from, int depth)
    {
        if (from == null || depth > 12) return depth;

        int best = depth;
        List<StrokeType> cur = from.strokes;

        foreach (HanziDef def in hanziDatabase)
        {
            if (def.strokes.Count != cur.Count + 1) continue;
            if (MatchPrefixEnd(cur, def.strokes) < 0) continue;

            int d = MaxGrowthDepth(def, depth + 1);
            if (d > best) best = d;
        }
        return best;
    }


    /// <summary>加一笔后能凑成的最小汉字（用于自动成长，避免一笔跳到很大的字）</summary>
    public HanziDef GetSmallestGrowth(StrokeType stroke)
    {
        List<HanziDef> cands = GetGrowthCandidates(stroke);
        HanziDef best = null;
        foreach (HanziDef d in cands)
        {
            if (best == null || d.strokes.Count < best.strokes.Count) best = d;
        }
        return best;
    }

    /// <summary>这一笔现在有没有用（能长大，或能直接凑成一个新字）</summary>
    public bool StrokeIsUseful(StrokeType stroke)
    {
        return GetGrowthCandidates(stroke).Count > 0 || GetDirectCandidates(stroke).Count > 0;
    }

    // ============================================================
    // 多笔画组合：玩家在背包里勾选若干笔画，一次性试合成
    // ============================================================
    //
    // 两种合法结果：
    //   A) 当前汉字 + 勾选的笔画，正好等于某个汉字（主路径）
    //   B) 勾选的笔画自己就能组成某个汉字
    // 匹配按"笔画种类计数"（无序），玩家不用管笔顺，怎么勾都试得出来。

    /// <summary>勾选的笔画能拼出哪些汉字</summary>
    public List<HanziDef> GetCombineCandidates(List<StrokeType> selectedStrokes)
    {
        if (selectedStrokes == null || selectedStrokes.Count == 0)
            return new List<HanziDef>();

        var result = new List<HanziDef>();

        // 可用笔画池：当前字的笔画 + 勾选的笔画
        List<StrokeType> pool = new List<StrokeType>(currentStrokes);
        pool.AddRange(selectedStrokes);

        foreach (HanziDef def in hanziDatabase)
        {
            if (def.strokes == null || def.strokes.Count == 0) continue;

            // 还是当前这个字，没意义
            if (currentHanziDef != null && MultisetEquals(def.strokes, currentHanziDef.strokes))
                continue;

            // A/B：目标字的笔画能从「当前字 + 勾选」里凑出来
            if (ContainsMultiset(pool, def.strokes))
            {
                result.Add(def);
                continue;
            }

            // C 重塑：目标字的笔画完全来自勾选的那部分，当前字的笔画不参与 ——
            // 也就是"换成另一种结构"。
            //
            // 这一条是必须的：没有它，「一」永远变不成「丨」（一 的笔顺不是 丨 的
            // 有序子序列），于是 丨/丿/丶 以及只能从它们长出来的 人/水/火/刀
            // 全部不可达 —— 32 个字里有 7 个永远拿不到。
            if (ContainsMultiset(selectedStrokes, def.strokes))
                result.Add(def);
        }

        return result;
    }

    /// <summary>无序匹配：两组笔画的"种类计数"完全相同</summary>
    public static bool MultisetEquals(List<StrokeType> a, List<StrokeType> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        return ContainsMultiset(a, b);
    }

    /// <summary>b 的每一种笔画，a 里都有足够数量</summary>
    public static bool ContainsMultiset(List<StrokeType> a, List<StrokeType> b)
    {
        if (a == null || b == null) return false;
        int[] count = new int[8];
        foreach (StrokeType s in a) count[(int)s]++;
        foreach (StrokeType s in b)
        {
            if (count[(int)s] <= 0) return false;
            count[(int)s]--;
        }
        return true;
    }

    /// <summary>预览：勾选这些笔画后能变成什么</summary>
    public string PreviewCombine(List<StrokeType> selectedStrokes)
    {
        if (selectedStrokes == null || selectedStrokes.Count == 0) return "";
        List<HanziDef> c = GetCombineCandidates(selectedStrokes);
        if (c.Count == 0) return "凑不出字";
        if (c.Count == 1) return $"→ {c[0].hanziName}";

        string s = "→ ";
        for (int i = 0; i < c.Count && i < 4; i++)
        {
            if (i > 0) s += "/";
            s += c[i].hanziName;
        }
        return s;
    }

    /// <summary>
    /// 执行组合并把结果套用到玩家身上。
    /// 返回本次从 selectedStrokes 里**实际消耗掉**的那几笔（可能只是一部分）。
    /// </summary>
    public HanziDef ApplyCombine(List<StrokeType> selectedStrokes, out List<StrokeType> usedStrokes)
    {
        usedStrokes = new List<StrokeType>();
        List<HanziDef> cands = GetCombineCandidates(selectedStrokes);
        if (cands.Count == 0) return null;

        HanziDef target = cands[0];

        // 目标字需要的笔画总数 = 当前汉字的笔画 + 勾选里用掉的部分。
        // 所以"还缺多少" = 目标笔画数 - 当前笔画数，从勾选里按需扣。
        int needFromSelection = Mathf.Max(0, target.strokes.Count - currentStrokes.Count);
        needFromSelection = Mathf.Min(needFromSelection, selectedStrokes.Count);

        for (int i = 0; i < needFromSelection; i++)
            usedStrokes.Add(selectedStrokes[i]);

        CommitHanzi(target);
        return target;
    }

    static bool RemoveOne(List<StrokeType> list, StrokeType s)
    {
        int i = list.IndexOf(s);
        if (i < 0) return false;
        list.RemoveAt(i);
        return true;
    }

    /// <summary>直接凑成一个完整汉字（不经过当前字）</summary>
    public List<HanziDef> GetDirectCandidates(StrokeType stroke)
    {
        List<StrokeType> temp = new List<StrokeType>(currentStrokes);
        temp.Add(stroke);
        return FindAllMatchingHanzi(temp);
    }

    void UpdateCurrentHanzi()
    {
        currentHanziDef = FindMatchingHanzi(currentStrokes);
        if (currentHanziDef != null)
            currentHanziName = currentHanziDef.hanziName;
        else
            currentHanziName = "未知";
        ApplyStatsToPlayer();
    }

    void ApplyStatsToPlayer()
    {
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null && currentHanziDef != null)
        {
            // 汉字本身决定基础射速，词条再乘算
            pc.fireRate = currentHanziDef.fireRate / Mathf.Max(0.01f, PlayerStats.fireRateMultiplier);
        }
    }

    // 词条变化后重新套用（射速等）
    public void RefreshStats()
    {
        ApplyStatsToPlayer();

        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null) pc.RefreshStats();
    }

    // 单发伤害 = 笔画数（最低 2），再乘词条加成。
    //
    // 另外要除以一个"弹数惩罚"：像 田/里/皇 这种一次打 8~12 发的形态，
    // 如果每发都按满伤害算，DPS 会飙到 300+，怪物瞬间被秒。
    // 弹数越多单发越轻，整体 DPS 保持在一条平缓的曲线上。
    int CurrentDamage
    {
        get
        {
            int strokes = currentHanziDef != null ? currentHanziDef.strokes.Count : 1;
            int bullets = currentHanziDef != null ? Mathf.Max(1, currentHanziDef.bulletCount) : 1;

            int baseDmg = Mathf.Max(2, strokes);
            int spreadPenalty = 1 + bullets / 5;   // 弹数越多单发越轻，但曲线保持单调上升
            int dmg = Mathf.Max(1, Mathf.RoundToInt((float)baseDmg / spreadPenalty));

            return Mathf.Max(1, Mathf.RoundToInt(dmg * PlayerStats.damageMultiplier));
        }
    }

    // 每秒理论伤害（UI 展示用）
    public float EstimatedDPS
    {
        get
        {
            if (currentHanziDef == null) return 0f;
            float rate = Mathf.Max(0.05f, fireRateNow);
            return currentHanziDef.bulletCount * CurrentDamage / rate;
        }
    }

    float fireRateNow
    {
        get
        {
            PlayerController pc = GetComponent<PlayerController>();
            return pc != null ? pc.fireRate : 0.30f;
        }
    }

    // 给 UI 用的只读访问
    public HanziDef CurrentHanziDef { get { return currentHanziDef; } }
    public int CurrentDamageValue { get { return CurrentDamage; } }

    float CurrentBulletSpeed(float baseSpeed)
    {
        return baseSpeed * PlayerStats.bulletSpeedMultiplier;
    }

    // ========== 候选汉字查询（供歧义选择用）==========

    // 当前汉字 + 一个笔画，能变成哪些汉字？（返回全部候选）
    public List<HanziDef> GetCandidatesForStroke(StrokeType stroke)
    {
        List<StrokeType> temp = new List<StrokeType>(currentStrokes);
        temp.Add(stroke);
        return FindAllMatchingHanzi(temp);
    }

    // 任意笔画组合的全部匹配
    public List<HanziDef> FindAllMatchingHanzi(List<StrokeType> strokes)
    {
        List<HanziDef> result = new List<HanziDef>();
        foreach (HanziDef def in Bucket(strokes.Count))
        {
            if (StrokeListsMatch(def.strokes, strokes))
                result.Add(def);
        }
        return result;
    }

    // 把当前汉字直接切换为指定定义（玩家在候选里选了一个）
    public void ApplyHanziDef(HanziDef def)
    {
        if (def == null) return;

        currentStrokes = new List<StrokeType>(def.strokes);
        currentHanziDef = def;
        currentHanziName = def.hanziName;

        SaveStrokesToGameData();
        ApplyStatsToPlayer();
        SyncCharacterGlyph();

        GameLog.Info($"汉字变更为「{currentHanziName}」- {def.description}");
    }

    // 加一个笔画但不提交（用于预览）
    public bool CanAddStroke(StrokeType stroke)
    {
        return GetCandidatesForStroke(stroke).Count > 0;
    }

    // ========== 歧义选择（核心）==========
    // 问题根源：笔画匹配是"按种类计数、忽略笔顺"，所以有些汉字的多重集合完全一样。
    //   横横竖     → 干 / 工   （数值相同，纯外形差异）
    //   横横撇捺   → 天 / 本
    // 这类字互相之间**永远无法通过加减笔画转换**，因此必须在"凑齐的那一刻"让玩家选。
    // 只有一个合法汉字的组合则保持自动合成，不打断节奏。

    // 返回当前汉字 + 新笔画后，真正需要玩家做选择的候选（可变的那些）
    public List<HanziDef> GetAmbiguousCandidates(StrokeType stroke)
    {
        List<HanziDef> all = GetCandidatesForStroke(stroke);
        if (all.Count <= 1) return new List<HanziDef>();

        List<HanziDef> mutable = new List<HanziDef>();
        foreach (HanziDef d in all)
        {
            if (IsMutable(d)) mutable.Add(d);
        }

        // 只有可变字才需要玩家选；如果全是唯一可达的，就不用打扰玩家
        return mutable.Count > 1 ? mutable : new List<HanziDef>();
    }

    // 该笔画组合是否有多个汉字共享（共享则意味着玩家必须选一个，否则另一些永远拿不到）
    public bool IsMutable(HanziDef def)
    {
        if (def == null) return false;
        return FindAllMatchingHanzi(def.strokes).Count > 1;
    }

    // 玩家在候选里选了一个 → 记住这个偏好，以后再凑齐同一组笔画时自动沿用
    public void PinPreference(HanziDef def)
    {
        if (def == null) return;
        if (GameData.hanziPreferences == null)
            GameData.hanziPreferences = new Dictionary<string, string>();

        GameData.hanziPreferences[StrokeKey(def.strokes)] = def.hanziName;
    }

    // 查询某个笔画组合以前记住的偏好
    public HanziDef GetPreferredFor(List<StrokeType> strokes)
    {
        if (GameData.hanziPreferences == null) return null;

        string name;
        if (!GameData.hanziPreferences.TryGetValue(StrokeKey(strokes), out name)) return null;

        foreach (HanziDef d in hanziDatabase)
        {
            if (d.hanziName == name && StrokeListsMatch(d.strokes, strokes)) return d;
        }
        return null;
    }

    // 把笔画组合编码成稳定的 key
    public static string StrokeKey(List<StrokeType> strokes)
    {
        List<StrokeType> sorted = new List<StrokeType>(strokes);
        sorted.Sort();
        return string.Join(",", sorted.ConvertAll(s => ((int)s).ToString()).ToArray());
    }

    // 把当前汉字同步到画面上的字形（合成成功后必须调用，否则玩家永远显示"一"）
    public void SyncCharacterGlyph()
    {
        HanziRenderer hr = GetComponent<HanziRenderer>();
        if (hr != null)
            hr.SetCharacter(currentHanziName);
    }

    // ========== 发射逻辑（支持多标签继承） ==========
    public void FireBulletPattern(Vector3 firePos, Vector3 baseDirection)
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("弹幕预制体未设置！");
            return;
        }

        if (currentHanziDef == null)
        {
            SpawnBullet(firePos, baseDirection, CurrentBulletSpeed(10f), CurrentDamage, 1f, false);
            return;
        }

        // 使用当前汉字的攻击标签
        SpawnByTags(firePos, baseDirection, currentHanziDef);
    }

        /// <summary>
    /// 按标签生成弹幕。
    ///
    /// 关键设计：**bulletCount 是权威的子弹总数**，攻击标签只决定"这些子弹怎么摆"，
    /// 而不是每个标签各自开火一轮。
    ///
    /// 之前的写法是 foreach(tag) + switch，每个标签都会独立发射：
    /// 皇 声明 10 发却实际打出 18 发（omni 8 + ring 10），田/主/圭 都是 12 发。
    /// 后果是越复杂的字画面越糊、而且伤害摊薄除数只按声明值算（1/3），
    /// 导致复杂度收益被系统性低估，各个字的手感也趋于雷同。
    ///
    /// 现在：把 bulletCount 按"形态标签"均分，每个标签只负责自己那一份，
    /// "pierce" 之类的纯增益标签不参与分配。
    /// </summary>
    void SpawnByTags(Vector3 firePos, Vector3 baseDir, HanziDef def)
    {
        int total = Mathf.Max(1, def.bulletCount);
        float speed = CurrentBulletSpeed(def.bulletSpeed);
        int dmg = CurrentDamage;
        float scale = def.bulletScale;

        // 穿透不再看标签，而是看"这个字含不含横画"（含横 → 所有弹幕穿透）。
        // 层数由 StrokeTraits 决定：基础 3 层，王 +2，皇再追加。
        bool pierce = traits != null && traits.Pierce;

        // 收集"形态标签"（决定子弹怎么摆的），纯增益标签跳过
        var shapes = new System.Collections.Generic.List<string>();
        foreach (string tag in def.attackTags)
        {
            if (tag == "pierce") continue;
            shapes.Add(tag);
        }

        // 没有任何形态标签 → 单发直线
        if (shapes.Count == 0)
        {
            SpawnBullet(firePos, baseDir, speed, dmg, scale, pierce,
                PickStrokeFor(0), true);
            return;
        }

        // 把 total 发子弹按形态标签轮流均分：
        // 第一个标签拿到最多（保证主形态可辨识），余数依次分给后面的标签。
        //   例：田（ring+cross，8 发）→ ring 4 发 + cross 4 发
        //       皇（omni+ring，10 发）→ omni 5 发 + ring 5 发
        int[] quota = new int[shapes.Count];
        int baseQuota = total / shapes.Count;
        int remainder = total % shapes.Count;
        for (int i = 0; i < shapes.Count; i++)
            quota[i] = baseQuota + (i < remainder ? 1 : 0);

        // 子弹外观的笔画来源：遍历这个字实际的笔画。
        // 需求是"每多一个横就多一个横形状的子弹"，所以顺序遍历即可 ——
        // 3 横的字就会连出 3 发横条，横竖混排的字会混出两种形状。
        var strokeList = new System.Collections.Generic.List<StrokeType>();
        if (def.strokes != null) strokeList.AddRange(def.strokes);

        int strokeCursor = 0;
        for (int s = 0; s < shapes.Count; s++)
        {
            if (quota[s] <= 0) continue;

            int groupStart = strokeCursor;
            SpawnShape(shapes[s], quota[s], firePos, baseDir, speed, dmg, scale, pierce,
                       i => StrokeAt(strokeList, groupStart + i));
            strokeCursor += quota[s];
        }
    }

    /// <summary>取字形里第 index 个笔画（循环使用），保证不会越界返回异常值</summary>
    static StrokeType StrokeAt(System.Collections.Generic.List<StrokeType> list, int index)
    {
        if (list == null || list.Count == 0) return StrokeType.Heng;
        return list[index % list.Count];
    }

    /// <summary>取当前字第 index 个笔画</summary>
    StrokeType PickStrokeFor(int index)
    {
        if (currentHanziDef == null || currentHanziDef.strokes == null ||
            currentHanziDef.strokes.Count == 0)
            return StrokeType.Heng;

        return StrokeAt(currentHanziDef.strokes, index);
    }

    /// <summary>按指定形态摆放 n 发子弹，每发的形状取自 strokeOf 给出的笔画</summary>
    void SpawnShape(string shape, int n, Vector3 firePos, Vector3 baseDir,
                    float speed, int dmg, float scale, bool pierce,
                    System.Func<int, StrokeType> strokeOf)
    {
        // baseDir 是 XY 平面向量，Vector3.Cross(baseDir, forward) 的 XY 分量为 0
        // 会导致子弹完全重叠，所以统一用 2D 垂直向量 (-y, x)。
        Vector3 side = new Vector3(-baseDir.y, baseDir.x, 0f).normalized;

        for (int i = 0; i < n; i++)
        {
            StrokeType st = strokeOf != null ? strokeOf(i) : StrokeType.Heng;
            Vector3 dir = baseDir;
            Vector3 pos = firePos;      // 注意：不能改 firePos 本身，否则偏移会逐发累加
            float spd = speed;
            float sc = scale;

            switch (shape)
            {
                case "straight":
                    break;

                case "parallel":
                    pos = firePos + side * ((i - (n - 1) / 2f) * 0.4f);
                    break;

                case "cross":
                    dir = Quaternion.Euler(0, 0, 90f * (i % 4)) * baseDir;
                    break;

                case "vshape":
                    dir = Quaternion.Euler(0, 0, (i % 2 == 0) ? 45f : -45f) * baseDir;
                    break;

                case "diagonal":
                    float[] diag = { 30f, -30f, 150f, -150f };
                    dir = Quaternion.Euler(0, 0, diag[i % 4]) * baseDir;
                    break;

                case "scatter":
                    float ts = n > 1 ? (float)i / (n - 1) : 0.5f;
                    dir = Quaternion.Euler(0, 0, -40f + 80f * ts) * baseDir;
                    break;

                case "omni":
                    dir = Quaternion.Euler(0, 0, 360f / n * i) * baseDir;
                    break;

                case "ring":
                    dir = Quaternion.Euler(0, 0, 360f / n * i) * baseDir;
                    spd = speed * 0.7f;
                    sc = scale * 0.8f;
                    break;

                case "shotgun":
                    float tp = n > 1 ? (float)i / (n - 1) : 0.5f;
                    dir = Quaternion.Euler(0, 0, -15f + 30f * tp) * baseDir;
                    spd = speed * 0.6f;
                    sc = scale * 0.8f;
                    break;

                default:
                    break;
            }

            SpawnBullet(pos, dir, spd, dmg, sc, pierce, st, true);
        }
    }

void SpawnBullet(Vector3 pos, Vector3 dir, float spd, int dmg, float scale, bool canPierce)
    {
        SpawnBullet(pos, dir, spd, dmg, scale, canPierce, StrokeType.Heng, true);
    }

    /// <summary>
    /// 生成一发子弹。stroke 决定弹幕外观（横条 / 竖条 / 斜条 / 圆点 / 折角）。
    /// 需求：子弹要长成对应笔画的形状，所以每发都按它所属的笔画来画。
    /// </summary>
    void SpawnBullet(Vector3 pos, Vector3 dir, float spd, int dmg, float scale,
                     bool canPierce, StrokeType stroke, bool applyShape)
    {
        GameObject bullet = Instantiate(bulletPrefab, pos, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetDirection(dir);
            b.isPlayerBullet = true;
            b.Setup(spd, dmg, scale, canPierce);

            if (applyShape)
                b.SetStrokeShape(stroke, StrokeUtil.ColorOf(stroke));

            // 含「点」→ 弹幕附带灼烧（最简版：复用现有的火 Buff 通道）
            if (traits != null && traits.Burn)
            {
                b.hasFireBuff = true;
                b.fireDPS = 1.2f;
                b.fireDuration = 2.5f;
            }
        }

        // 应用Buff
        BulletBuff buff = GetComponent<BulletBuff>();
        if (buff != null)
            buff.ApplyBuffToBullet(bullet);

        // ← 新增：应用元素效果
        ElementSystem elem = GetComponent<ElementSystem>();
        if (elem != null)
            elem.ApplyElementToBullet(bullet);
    }
}