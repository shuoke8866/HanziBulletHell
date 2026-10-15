using System.Collections.Generic;

// 跨场景保存的游戏数据（静态类，不随场景销毁）
// 注意：所有 List 都"就地修改"，不要重新赋值整个 List——
// 否则其他系统持有的旧引用会指向已经被丢弃的列表。
public static class GameData
{
    /// <summary>
    /// 当前汉字的笔画。
    ///
    /// 开局为空：等玩家在开局界面选完起手笔画，再按那个字的笔顺装上
    /// （见 <see cref="BeginRealRun"/> 与 HanziSystem.ApplyOpeningStarter）。
    /// 教程层则是空着进场，靠捡笔画长起来。
    /// </summary>
    public static List<StrokeType> currentStrokes = new List<StrokeType>();

    // 背包笔画
    public static List<StrokeType> inventoryStrokes = new List<StrokeType>();

    /// <summary>
    /// **永久笔画池**：本局捡到过的所有笔画，只增不减。
    ///
    /// 这是"自由切换汉字"的基础。原来的模型把背包当作"当前可用笔画"：
    /// 切换汉字时背包被清空重填，装不下的笔画直接丢弃 —— 于是每切一次字，
    /// 池子就小一点，切几次之后只能拼出「一」，而且再也回不去。
    ///
    /// 改成的模型：
    ///   可用笔画 = allStrokes - 当前汉字占用的笔画
    ///   切换汉字只是换掉"当前字占用的那几笔"，池子本身从不缩小
    ///   → 之前拼出过的任何字都还能再拼出来（可自由回退）
    /// </summary>
    public static List<StrokeType> allStrokes = new List<StrokeType>();

    /// <summary>
    /// 本局合成过的汉字名（按首次合成顺序）。
    /// 背包里会把它们列出来供玩家点击切换。
    /// </summary>
    public static List<string> formedHanzi = new List<string>();

    // 当前元素
    public static ElementType currentElement = ElementType.None;

    // 当前层数（唯一真实来源，GameManager 只是它的视图）
    public static int currentFloor = 0;   // 0 = 新手教程层

    // 本轮统计
    public static int enemiesKilled = 0;
    public static int bossesKilled = 0;
    public static int strokesCollected = 0;

    // 跨层保留的当前生命（0 表示还没初始化，由 PlayerHealth 决定）
    public static int carriedHealth = 0;

    // 汉字歧义偏好：笔画组合 key → 玩家选过的汉字名
    // 例：横横竖 这个组合玩家选了"工"，以后凑齐横横竖就自动变"工"
    public static Dictionary<string, string> hanziPreferences = new Dictionary<string, string>();

    /// <summary>
    /// 当前是不是在**新手教程层**。
    ///
    /// 教程和实战的数据必须完全独立：
    /// 玩家在教程里把六种笔画都捡了一遍、试过各种换字，
    /// 这些如果带进第一层，开局就等于半套装备，成长曲线直接废掉。
    ///
    /// 所以教程里捡到的东西只活在教程层，走出教程时
    /// <see cref="BeginRealRun"/> 会把一切清零，实战从选一支笔画重新开始。
    /// </summary>
    public static bool inTutorial = true;

    /// <summary>
    /// 实战是否已经"开局选过笔画"。
    /// 选过之后就不再弹选择界面（重开关卡也不会重复弹）。
    /// </summary>
    public static bool openingStrokeChosen = false;

    /// <summary>实战可选的开局笔画（六种基本笔画）</summary>
    public static readonly StrokeType[] StarterChoices =
    {
        StrokeType.Heng, StrokeType.Shu, StrokeType.Pie,
        StrokeType.Na, StrokeType.Dian, StrokeType.Zhe
    };

    // 重置为新游戏（死亡或主动重开时调用）
    public static void ResetAll()
    {
        ClearRunState();

        currentElement = ElementType.None;
        currentFloor = 0;   // 从新手教程层开始

        hanziPreferences.Clear();

        // 词条加成也要清掉
        PlayerStats.ResetAll();

        GameLog.Info("GameData 已重置为新游戏");
    }

    /// <summary>
    /// **开始正式一局**：把教程层的一切成果清空，用玩家选的笔画重新开局。
    ///
    /// 这是"教程与实战独立"的落地点 —— 教程里捡了多少笔画、
    /// 合成过多少字，走到这里全部归零。
    /// </summary>
    public static void BeginRealRun(StrokeType starter)
    {
        ClearRunState();

        currentStrokes.Add(starter);
        allStrokes.Add(starter);

        // 开局只有一支笔画，就按这一支对应的最简汉字起手
        string startHanzi = HanziForSingleStroke(starter);
        formedHanzi.Add(startHanzi);

        currentElement = ElementType.None;
        currentFloor = 1;             // 正式第一层
        inTutorial = false;
        openingStrokeChosen = true;

        PlayerStats.ResetAll();

        GameLog.Info($"正式开局：起始笔画「{StrokeUtil.NameOf(starter)}」→ 汉字「{startHanzi}」");
    }

    /// <summary>把各种临时状态清干净（教程和实战共用）</summary>
    static void ClearRunState()
    {
        currentStrokes.Clear();
        inventoryStrokes.Clear();
        allStrokes.Clear();
        formedHanzi.Clear();

        enemiesKilled = 0;
        bossesKilled = 0;
        strokesCollected = 0;
        carriedHealth = 0;
    }

    /// <summary>
    /// 单支笔画能组成的最简汉字（开局起手字）。
    ///
    /// 一到五画各有自己的单笔画字；**折没有** —— 字库里没有「乛」这个字形，
    /// 所以折用「刀」（Zhe,Pie）代替：它是字库里以折起笔、笔画最少的字。
    /// 选折开局的玩家起手就是「刀」，比别的开局多一笔，算是这点小补偿。
    /// </summary>
    public static string HanziForSingleStroke(StrokeType s)
    {
        switch (s)
        {
            case StrokeType.Heng: return "一";
            case StrokeType.Shu: return "丨";
            case StrokeType.Pie: return "丿";
            case StrokeType.Na: return "乀";
            case StrokeType.Dian: return "丶";
            case StrokeType.Zhe: return "刀";
            default: return "一";
        }
    }

    // 本轮进度摘要
    public static string RunSummary()
    {
        return $"第 {currentFloor} 层 | 笔画 {currentStrokes.Count} 画 | 背包 {inventoryStrokes.Count} | " +
               $"击杀 {enemiesKilled} 小怪 / {bossesKilled} BOSS";
    }
}
