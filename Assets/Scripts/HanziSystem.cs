using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HanziDef
{
    public string hanziName;             // 汉字名称
    public List<StrokeType> strokes;     // 笔画组合
    public List<string> attackTags;      // 攻击特征标签（继承体现）
    /// <summary>实际发射的弹幕数量（决定形状与"手感"，每个字保留自己的特色）</summary>
    public int bulletCount;

    /// <summary>
    /// 这个字"设计上"的弹数 —— 由笔画数推导（GameBalance.BulletCountFor）。
    ///
    /// 和 bulletCount 分开存的原因：
    ///   bulletCount      = 真的打几发（形状特色，比如「十」打 4 发十字）
    ///   savedBulletCount = 这一档笔画"标准"该打几发（强度基准）
    ///
    /// 伤害按 标准总伤 / 实际弹数 反推，所以弹数多则单发轻、弹数少则单发重，
    /// **总输出恒定**。这样形状可以随字自由变化，强度曲线仍然只由笔画数决定。
    /// </summary>
    public int savedBulletCount;
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
            // 空手进场：教程层是空着开始、靠捡笔画长起来的；
            // 实战层则由开局界面选完起手笔画后再装上（ApplyOpeningStarter）。
            //
            // 原来这里会硬塞一支「横」，结果是"教程里刚捡的笔画被顶掉"、
            // "实战选的起手笔画被覆盖成「一」"。
            currentStrokes.Clear();
            SaveStrokesToGameData();
            GameLog.Info("进场时手上没有笔画（等待拾取或开局选择）");
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

    /// <summary>
    /// 正式开局：把玩家的**起手笔画**装进去。
    ///
    /// 和 <see cref="ResetToDefaultHanzi"/> 的区别：那个永远回到「一」，
    /// 这个用玩家在开局界面选的笔画 —— 选竖就以「丨」起手、选折就以「刀」起手。
    ///
    /// 调用方（OpeningStrokeUI）已经先调用过 GameData.BeginRealRun，
    /// 那边会把教程层的全部成果清零，所以这里是从干净状态开始装。
    /// </summary>
    public void ApplyOpeningStarter(StrokeType starter)
    {
        EnsureInitialized();

        string startHanzi = GameData.HanziForSingleStroke(starter);
        currentHanziDef = FindByName(startHanzi);

        currentStrokes.Clear();

        if (currentHanziDef != null && currentHanziDef.strokes != null &&
            currentHanziDef.strokes.Count > 0)
        {
            // 用起手字的完整笔顺。
            // 「折」的起手字「刀」是两画（Zhe+Pie），
            // 只塞一支 Zhe 的话池子凑不出这个字，开局会显示不出汉字。
            currentStrokes.AddRange(currentHanziDef.strokes);
        }
        else
        {
            currentStrokes.Add(starter);
        }

        SaveStrokesToGameData();

        // 永久池 = 起手字占用的笔画，此外什么都没有
        Pool.Clear();
        Pool.AddRange(currentStrokes);

        if (GameData.allStrokes == null) GameData.allStrokes = new List<StrokeType>();
        GameData.allStrokes.Clear();
        GameData.allStrokes.AddRange(currentStrokes);

        if (GameData.formedHanzi == null) GameData.formedHanzi = new List<string>();
        GameData.formedHanzi.Clear();
        GameData.formedHanzi.Add(startHanzi);

        StrokeInventory inv = GetComponent<StrokeInventory>();
        if (inv != null && inv.strokes != null)
        {
            inv.strokes.Clear();
            inv.SaveToGameData();
        }

        UpdateCurrentHanzi();
        SyncCharacterGlyph();

        GameLog.Info($"开局起手：笔画「{StrokeUtil.NameOf(starter)}」→ 汉字「{startHanzi}」" +
                     $"（{currentStrokes.Count} 画）");
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

        // 乀 / 乛 是另外两种笔画的"单笔画字"。
        //
        // 玩家平时拼不到它们（得先有对应的那一支笔画，而那要靠捡），
        // 但它们必须在库里：开局选笔画界面和教程展台都要按名字取字形，
        // 没有定义的话 FindByName 返回 null、开局会开不出来。
        // 数值和别的单笔画字拉平，不构成额外优势。
        AddHanzi("乀",
            new List<StrokeType> { StrokeType.Na },
            new List<string> { "vshape" },
            1, 12f, "1发斜射");

        AddHanzi("乛",
            new List<StrokeType> { StrokeType.Zhe },
            new List<string> { "straight" },
            1, 12f, "1发折射");

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
    /// <summary>
    /// 注册一个汉字。
    ///
    /// **弹数、射速、单发伤害都不再手写** —— 一律由笔画数推导
    /// （见 GameBalance.BulletCountFor / FireIntervalFor / DamageFor）。
    ///
    /// 为什么改成这样：手写时出现了两类毛病 ——
    ///   · 同为 2 画的「十」4 发、「丁」2 发，同笔画差 2 倍 DPS
    ///   · 7 画的圭/里只有 8 发（和 5 画一样强），9 画的皇算下来
    ///     DPS 反而低于它们 —— 玩家攒到关底字发现更弱了
    /// 现在强度只由"字有多复杂"决定，想整体调强弱改 GameBalance 那张表即可。
    ///
    /// bulletSpeed 保留为参数：它决定的是**手感**（快弹难躲 / 慢弹好躲），
    /// 不是强度，所以让每个字保留自己的性格。
    /// </summary>
    /// <summary>
    /// 注册一个汉字。
    ///
    /// shotCount 是**实际发射的弹数**，每个字保留自己的形状特色
    /// （十 = 4 发十字、土 = 6 发环形、皇 = 10 发全方位）。
    ///
    /// 但强度不跟着弹数走：伤害按「这一档笔画的标准总伤 ÷ 实际弹数」反推，
    /// 所以弹数多则单发轻、弹数少则单发重，**总输出恒定**。
    /// 这样形状可以随字自由变化，强度曲线仍然只由笔画数决定。
    /// </summary>
    void AddHanzi(string name, List<StrokeType> strokes, List<string> tags,
                  int shotCount, float bulletSpeed, string desc)
    {
        int n = strokes != null ? strokes.Count : 1;

        int standard = GameBalance.BulletCountFor(n);        // 强度基准
        int actual = Mathf.Max(1, shotCount);                // 形状特色

        hanziDatabase.Add(new HanziDef
        {
            hanziName = name,
            strokes = strokes,
            attackTags = tags,
            bulletCount = actual,
            savedBulletCount = standard,
            bulletSpeed = bulletSpeed,
            damage = GameBalance.DamageFor(n, standard, actual),
            fireRate = GameBalance.FireIntervalFor(n),
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
        return SolveWithGrowth(AvailableStrokes());
    }

    // ============================================================
    //  成长求解：把"当前字占用的笔画"也算进来
    // ============================================================
    //
    // ## 为什么需要单独一套
    //
    // AvailableStrokes() 的语义是「还有哪些笔画是空闲的」——
    // 这是**切换**用的：切到别的字时，当前字占的那几笔要留下来。
    //
    // 但**成长**是另一回事。玩家当前「一」（占 1 个横）、池子里有 [横,竖]，
    // 想变成「十」只需要**补一个竖** —— 那个横本来就要留在十里面。
    // 如果只把 [竖] 喂给求解器，它算出来只有 1 画，永远拼不出「十」。
    //
    // ## 正确规则
    //
    //   目标字的笔顺 ⊇ 当前字的笔顺（作为子序列）
    //   且  需要补的笔画 ⊆ 可用笔画
    //
    // 例：当前「一」=[横]，目标「十」=[横,竖]
    //     [横] 是 [横,竖] 的子序列 ✓，需要补 [竖]，可用 [竖] ✓ → 可行
    //
    // 例：当前「一」=[横]，目标「二」=[横,横]
    //     [横] 是 [横,横] 的子序列 ✓，需要补 [横]，可用 [竖] ✗ → 不可行

    /// <summary>a 是不是 b 的子序列（保持顺序，可以不连续）</summary>
    public static bool IsSubsequence(List<StrokeType> a, List<StrokeType> b)
    {
        // 空集是任何集合的子序列 —— 注意 null 也要当成空集，
        // 否则"当前没有字"时会被误判成"完全不是子序列"。
        if (a == null || a.Count == 0) return true;
        if (b == null || b.Count < a.Count) return false;

        int i = 0;
        for (int j = 0; j < b.Count && i < a.Count; j++)
        {
            if (b[j] == a[i]) i++;
        }
        return i == a.Count;
    }

    /// <summary>
    /// 要把 current 变成 target，**还需要补哪些笔画**（按 target 的笔顺）。
    ///
    /// current 必须是 target 的子序列，否则返回 null。
    /// 例：current=[横], target=[横,竖] → [竖]
    ///
    /// **current 为 null/空时返回空列表，不是全部笔画** ——
    /// 手上还没有字，就不存在"要补"的概念，target 的笔画全都得从池子里出，
    /// 那个判断交给调用方的 CanCover / TryForm。
    /// 之前这里没区分，导致 null 被当成"和 target 不匹配"，白白要求多补笔画。
    /// </summary>
    public static List<StrokeType> StrokesToAdd(List<StrokeType> current,
                                                List<StrokeType> target)
    {
        if (current == null || current.Count == 0)
            return new List<StrokeType>();      // 没有当前字 → 没有"要补"的

        if (!IsSubsequence(current, target)) return null;

        var need = new List<StrokeType>();
        int i = 0;

        for (int j = 0; j < target.Count; j++)
        {
            if (i < current.Count && target[j] == current[i])
            {
                i++;            // 这一笔当前字已经占着，不用补
                continue;
            }
            need.Add(target[j]);
        }

        return need;
    }

    /// <summary>need 能不能从 available 里凑出来（多重集包含）</summary>
    public static bool CanCover(List<StrokeType> available, List<StrokeType> need)
    {
        if (need == null) return false;
        if (need.Count == 0) return true;
        if (available == null) return false;

        var pool = new List<StrokeType>(available);
        foreach (StrokeType s in need)
        {
            int i = pool.IndexOf(s);
            if (i < 0) return false;
            pool.RemoveAt(i);
        }
        return true;
    }

    /// <summary>
    /// **从当前字变成目标字，要从可用笔画里拿哪几笔。**
    ///
    /// 三种情况，全项目统一走这一个入口 —— 原来是各写各的，
    /// 结果"当前字算不算数"在 SwitchTo / GetAllFormable / TiedCandidatesFor
    /// 里各有各的答案，互相打架。
    ///
    ///   A) 目标是当前字的**成长**（当前笔顺是它的子序列且不同）
    ///      只需要补差的那几笔。
    ///      例：当前「一」=[横] → 目标「十」=[横,竖] → [竖]
    ///
    ///   B) 目标是**另一个字**（不构成成长关系）
    ///      得用可用笔画完整拼出来。
    ///      例：当前「十」=[横,竖] → 目标「丨」=[竖] → [竖]
    ///
    ///   C) 手上**还没有字**（cur 为空）
    ///      目标整体都要从可用笔画里出。
    ///
    /// 返回 null 表示理论上不可行（调用方直接跳过）。
    /// </summary>
    public static List<StrokeType> StrokesNeeded(List<StrokeType> cur,
                                                 List<StrokeType> target)
    {
        if (target == null || target.Count == 0) return null;

        // C) 没有当前字
        if (cur == null || cur.Count == 0)
            return new List<StrokeType>(target);

        // A) 成长
        if (IsSubsequence(cur, target))
        {
            var add = StrokesToAdd(cur, target);
            if (add != null) return add;
        }

        // B) 换成另一个字
        return new List<StrokeType>(target);
    }

    /// <summary>
    /// 求解"把当前字成长/切换成什么最好"，**同时考虑当前字已占用的笔画**。
    ///
    /// 做法：把「当前字占用的笔画 + 可用笔画」合成一个完整池子喂给求解器，
    /// 再过滤掉"补不上"的目标（需要补的笔画超出了可用笔画）。
    ///
    /// 这样：
    ///   · 一 → 十 可行（补一个竖）
    ///   · 一 → 二 不可行（要补一个横，但手上只有竖）
    ///   · 切换回已经拼过的复杂字也可行（比如当前「十」想回「一」）
    /// </summary>
    List<HanziOption> SolveWithGrowth(List<StrokeType> available)
    {
        var full = new List<StrokeType>();
        if (currentHanziDef != null && currentHanziDef.strokes != null)
            full.AddRange(currentHanziDef.strokes);
        if (available != null) full.AddRange(available);

        var raw = HanziSolver.Solve(full, hanziDatabase);
        var result = new List<HanziOption>();

        foreach (HanziOption opt in raw)
        {
            if (opt == null || opt.def == null || opt.def.strokes == null) continue;

            // 这一笔要补多少 —— 统一走 StrokesNeeded
            List<StrokeType> need = StrokesNeeded(
                currentHanziDef != null ? currentHanziDef.strokes : null,
                opt.def.strokes);

            if (need == null) continue;
            if (!CanCover(available, need)) continue;

            opt.usedFromPool = need.Count;     // "要补几笔"，不是"总共几笔"
            result.Add(opt);
        }

        // 重新排序（usedFromPool 被改过，按同一套规则再排一遍）
        HanziSolver.SortOptions(result, HanziSolver.MaxFrequency(full, out _));
        return result;
    }


    /// <summary>
    /// 仓库里能拼出的**全部**汉字（含那些笔画数较少的），按复杂度排序。
    /// 合成台要"列出所有能拼出的字"就用它。
    /// </summary>
    public List<HanziOption> GetAllFormable()
    {
        EnsureInitialized();

        List<StrokeType> avail = AvailableStrokes();
        var cur = currentHanziDef != null ? currentHanziDef.strokes : null;
        var result = new List<HanziOption>();

        foreach (HanziDef def in hanziDatabase)
        {
            if (def.strokes == null || def.strokes.Count == 0) continue;
            if (currentHanziDef != null && def.hanziName == currentHanziDef.hanziName) continue;

            // 和 SwitchTo 用同一个入口，保证"背包里列出来的"和"点了真能切过去的"完全一致
            List<StrokeType> need = StrokesNeeded(cur, def.strokes);
            if (need == null) continue;
            if (!CanCover(avail, need)) continue;

            result.Add(new HanziOption
            {
                def = def,
                keyStrokeCount = 0,
                usedFromPool = need.Count,     // "要补几笔"，不是"总共几笔"
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

        var cur = currentHanziDef != null ? currentHanziDef.strokes : null;

        // 从当前字变成目标字要从可用笔画里拿哪几笔 ——
        // 统一走 StrokesNeeded（成长 / 换字 / 手上没字，三种情况都在里面分清）。
        //
        // 这里就是"当前字算不算进合成系统"的关键：
        // 当前「一」=[横] 想变「十」=[横,竖]，只需要补一个竖 ——
        // 那个横本来就要留在十里面，不该再拿一个横出来。
        List<StrokeType> need = StrokesNeeded(cur, target.strokes);
        if (need == null) return false;

        // 校验 + 扣除（合并成一次，避免两处逻辑漂移）
        var left = new List<StrokeType>(avail);
        foreach (StrokeType s in need)
        {
            int idx = left.IndexOf(s);
            if (idx < 0)
            {
                GameLog.Warn($"切换「{target.hanziName}」失败：还差一个 {StrokeUtil.NameOf(s)}");
                return false;
            }
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

        // 2.5) 手上的可用笔画是不是"整好能拼出好几个笔顺相同的字"？
        //
        // 汉字里本来就存在笔顺一样、只靠字形区分的字：十/丁、人/八、工/士/土。
        // 光看笔顺没法决定给哪个 —— 原来按名字 Unicode 排序取第一个，
        // 结果十、人、士、土这四个字玩家**永远拿不到**。
        // 现在弹一个选择界面让玩家点，选过的会被记住。
        List<StrokeType> avail = AvailableStrokes();
        List<HanziDef> tied = TiedCandidatesFor(avail);

        if (tied.Count >= 2 && ChoiceRequested != null)
        {
            // 玩家已经为这个组合定过偏好 → 直接沿用，不再打扰
            HanziDef pref = GetPreferredFor(avail);
            if (pref != null)
            {
                foreach (HanziDef d in tied)
                {
                    if (d.hanziName != pref.hanziName) continue;
                    if (IsSameHanzi(d, currentHanziDef)) return false;
                    if (SwitchTo(d)) return true;
                    break;
                }
            }
            else
            {
                AwaitingChoice = true;
                GameLog.Info($"「{Name(avail)}」可以拼出 {tied.Count} 个字，" +
                             $"等玩家选：{JoinNames(tied)}");
                ChoiceRequested(tied);
                return true;
            }
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

    static string JoinNames(List<HanziDef> list)
    {
        string s = "";
        foreach (HanziDef d in list)
        {
            if (s.Length > 0) s += " / ";
            s += d.hanziName;
        }
        return s;
    }

    static string Name(List<StrokeType> list)
    {
        string s = "";
        foreach (StrokeType t in list) s += StrokeUtil.NameOf(t);
        return s;
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

        // **不**在这里补笔画：手上没有笔画是合法状态 ——
        // 教程层空手进场、靠捡笔画长起来；实战层则由开局界面选完起手笔画再装上。
        //
        // 原来这里会硬塞一支「横」（Awake 里还塞过一次），结果是
        // "教程里刚捡到的笔画被顶掉"、"实战选的起手笔画被覆盖成「一」"。
        if (currentStrokes == null)
            currentStrokes = new List<StrokeType>();

        // 永久笔画池至少要包含"当前字占用的笔画"，
        // 否则会出现"池子里一笔都没有"、导致切换全部失败。
        // 手上没笔画时这里是空操作（currentStrokes 也是空的）。
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
            int actual = currentHanziDef != null ? Mathf.Max(1, currentHanziDef.bulletCount) : 1;
            int standard = currentHanziDef != null
                ? Mathf.Max(1, currentHanziDef.savedBulletCount)
                : 1;

            // 伤害公式统一放在 GameBalance，和 AddHanzi 用的是同一份 ——
            // 免得"定义时算一遍、发射时又算一遍"两处对不上。
            int dmg = GameBalance.DamageFor(strokes, standard, actual);

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

    /// <summary>
    /// 用当前**可用笔画**能拼出的、笔顺完全相同的那些字。
    ///
    /// 这是"十 / 丁 到底给哪个"的入口。汉字里本来就存在
    /// 笔顺一样、只靠字形区分的字（十丁、人八、工士土），
    /// 光靠笔顺没法自动决定 —— 所以交给玩家选。
    ///
    /// 返回的列表：
    ///   · 长度 &lt;= 1 → 没有歧义，调用方照常自动切
    ///   · 长度 &gt;= 2 → 有歧义，应当弹选择界面
    ///
    /// 玩家选过的会被 PinPreference 记住，下次同组合直接沿用。
    /// </summary>
    public List<HanziDef> TiedCandidatesFor(List<StrokeType> availableStrokes)
    {
        EnsureInitialized();

        var result = new List<HanziDef>();
        if (availableStrokes == null) return result;

        // 判定依据是"把当前字成长之后，笔顺会是哪个组合"，
        // 所以要把**当前字占用的笔画**也算进来 ——
        // 否则玩家当前「一」、捡到「竖」时，这里是拿 [竖] 去比，
        // 只匹配到「丨」，根本看不到「十」/「丁」这一组。
        //
        // full = 当前字笔顺 ++ 可用笔画。玩家手上"总共能凑出的笔顺"就是它 ——
        // 当前字占的那几笔本来就会留在成长后的字里，不用额外补。
        var full = new List<StrokeType>();
        if (currentHanziDef != null && currentHanziDef.strokes != null)
            full.AddRange(currentHanziDef.strokes);
        full.AddRange(availableStrokes);

        foreach (HanziDef def in FindAllMatchingHanzi(full))
        {
            if (def == null || def.strokes == null) continue;
            if (def.hanziName == currentHanziName) continue;    // 就是当前字，不算候选
            result.Add(def);
        }

        // 排序：玩家记过的偏好优先，其余按名字，保证顺序稳定
        string preferred = null;
        HanziDef pref = GetPreferredFor(full);
        if (pref != null) preferred = pref.hanziName;

        result.Sort((a, b) =>
        {
            if (preferred != null)
            {
                bool pa = a.hanziName == preferred;
                bool pb = b.hanziName == preferred;
                if (pa != pb) return pa ? -1 : 1;
            }
            return string.CompareOrdinal(a.hanziName, b.hanziName);
        });

        return result;
    }

    /// <summary>
    /// 需要玩家选字时发出。参数是候选列表（至少 2 个）。
    /// 由 HanziPickerUI 订阅；没有订阅者时 ApplyBestFor 会退回自动选择。
    /// </summary>
    public static event System.Action<List<HanziDef>> ChoiceRequested;

    /// <summary>
    /// 当前是否正等着玩家选字。
    /// UI 用它来避让按键（别让数字键同时触发"换字"和"选字"）。
    /// </summary>
    public static bool AwaitingChoice;

    /// <summary>
    /// 玩家在候选里点了一个：记住偏好并立刻切过去。
    /// </summary>
    public void ResolveChoice(HanziDef chosen)
    {
        AwaitingChoice = false;

        if (chosen == null) return;

        PinPreference(chosen);

        if (SwitchTo(chosen))
            GameLog.Info($"玩家选择「{chosen.hanziName}」（已记住这个偏好）");
        else
            GameLog.Warn($"玩家选了「{chosen.hanziName}」但切换失败（笔画不足）");
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