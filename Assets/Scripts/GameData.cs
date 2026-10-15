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

    /// <summary>
    /// **离开展教程、进入正式第一层时调用**：把玩家身上一切"跨层携带"的
    /// 状态清空，让他真的一无所有地开始。
    ///
    /// 为什么单独抽一个方法：<see cref="BeginRealRun"/> 要等玩家在开局界面
    /// 选完笔画才执行，但玩家**走进第一层的那一刻**身上就已经带着教程的战利品了 ——
    /// PlayerHealth.Start() 会读 carriedHealth 把血补上，
    /// ElementSystem 也会带着教程里吃的元素。所以必须在切场景之前先清一遍。
    ///
    /// 清的是"人身上的东西"（血、元素、词条加成），
    /// 笔画和汉字由 BeginRealRun 负责，两步配合才是完整的开局。
    /// </summary>
    public static void ClearCarriedPlayerState()
    {
        carriedHealth = 0;               // PlayerHealth 会回到满血
        currentElement = ElementType.None;

        PlayerStats.ResetAll();          // 词条加成（射速/移速/伤害/弹速/额外血）

        GameLog.Info("已清空玩家携带状态（血量 / 元素 / 词条）—— 正式开局一无所有");
    }

    // 重置为新游戏（死亡或主动重开时调用）
    public static void ResetAll()
    {
        ClearRunState();

        currentElement = ElementType.None;
        currentFloor = 0;   // 从新手教程层开始
        inTutorial = true;
        openingStrokeChosen = false;

        hanziPreferences.Clear();

        // 词条加成也要清掉
        PlayerStats.ResetAll();

        GameLog.Info("GameData 已重置为新游戏");
    }

    /// <summary>
    /// **死亡重开**：回到正式第一层，但要保留开局选的起手笔画。
    ///
    /// 和 ResetAll 的区别：
    ///   ResetAll     —— 回教程层，一切重来（玩家主动"新游戏"时用）
    ///   RespawnAtRealRun —— 回第一层，起手笔画还记得（死亡时用）
    ///
    /// 血量、元素、词条、捡到的笔画都要清 —— 死亡就是从头再来。
    /// </summary>
    public static void RespawnAtRealRun()
    {
        StrokeType starter = StrokeType.Heng;
        if (currentStrokes != null && currentStrokes.Count > 0)
            starter = currentStrokes[0];

        ClearRunState();
        ClearCarriedPlayerState();

        currentFloor = 1;             // 正式第一层
        inTutorial = false;

        hanziPreferences.Clear();

        BeginRealRun(starter);        // 这里会连词条一起再清一次，无妨，幂等

        GameLog.Info($"死亡重开：回到第 1 层，起手笔画保留为「{StrokeUtil.NameOf(starter)}」");
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
    /// 单支笔画能组成的最简汉字（开局起手字 / 教程展台显示的字形）。
    ///
    /// 六种笔画各有自己的单笔画字，「折」用「乛」（横折）——
    /// 它就是为了这件事专门加进字库的。
    ///
    /// 原来折借用「刀」，但刀是"折 + 撇"两画，
    /// 教程展台上摆一个"刀"去教"折"会让人误解成"折就是这个形状"。
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
            case StrokeType.Zhe: return "乛";
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
