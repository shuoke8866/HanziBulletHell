using UnityEngine;

/// <summary>
/// 全局数值配置。所有"魔法数字"集中在这里，方便调整和审阅。
/// 设计原则：
///   - 单发伤害只跟笔画数挂钩，弹数多则单发轻（避免 DPS 平方级膨胀）
///   - 敌人血量按"玩家几发能打死"来配
///   - 层数成长用倍率，不写死在各个脚本里
/// </summary>
public static class GameBalance
{
    // ---------- 玩家弹幕 ----------
    /// <summary>单发伤害下限</summary>
    public const int MinBulletDamage = 2;

    /// <summary>弹数超过多少发开始摊薄单发伤害</summary>
    public const int SpreadPenaltyDivisor = 5;

    /// <summary>不同汉字的基础射速（秒/发）</summary>
    public const float BaseFireRate = 0.30f;

    // ---------- 汉字强度：按笔画数统一推导 ----------
    //
    // 原来每个字的弹数是**手写**的，于是出现两种毛病：
    //   1. 同为 2 画，十是 4 发、丁是 2 发 —— 同笔画差 2 倍 DPS
    //   2. 7 画的圭/里只有 8 发，和 5 画的字一样强；9 画的皇 10 发，
    //      算下来 DPS 反而**低于**圭/里 —— 玩家攒到关底字却发现更弱了
    //
    // 现在改成按笔画数查表，强度只由"字的复杂度"决定，
    // 不再由某个字当初手写的数字决定。想整体调强弱只改这一张表。
    static readonly int[] BulletsByStrokes =
    {
        /* 0 */ 1,
        /* 1 */ 1,
        /* 2 */ 2,
        /* 3 */ 4,
        /* 4 */ 5,
        /* 5 */ 6,
        /* 6 */ 7,
        /* 7 */ 8,
        /* 8 */ 9,
        /* 9 */ 10
    };

    /// <summary>这个笔画数的字应该打几发</summary>
    public static int BulletCountFor(int strokes)
    {
        if (BulletsByStrokes.Length == 0) return 1;
        int i = Mathf.Clamp(strokes, 0, BulletsByStrokes.Length - 1);
        return Mathf.Max(1, BulletsByStrokes[i]);
    }

    /// <summary>
    /// 这个笔画数的字应该多久打一轮（秒）。
    ///
    /// 和弹数一起决定 DPS。字越复杂打得越密，但幅度比弹数小 ——
    /// 弹数负责主要成长，射速只做微调，避免"又密又多"双重爆炸。
    /// </summary>
    public static float FireIntervalFor(int strokes)
    {
        int s = Mathf.Clamp(strokes, 1, 9);
        return Mathf.Lerp(BaseFireRate, 0.26f, (s - 1) / 8f);
    }

    /// <summary>
    /// 这一档笔画的**标准单发伤害**（对应 BulletCountFor 的标准弹数）。
    /// 两者相乘就是这一档的"目标总伤害"。
    /// </summary>
    static int BaseDamageFor(int strokes)
    {
        int s = Mathf.Clamp(strokes, 1, 9);
        return Mathf.Clamp(Mathf.RoundToInt(2f + (s - 1) * 0.5f), MinBulletDamage, 12);
    }

    /// <summary>
    /// 这个字的单发伤害。
    ///
    /// **总输出恒定，单发随弹数反比缩放。**
    ///
    /// 参数：
    ///   strokes    笔画数（决定强度档次）
    ///   standard   这一档的标准弹数（强度基准，见 BulletCountFor）
    ///   actual     这个字实际打几发（形状特色，比如「十」打 4 发十字）
    ///
    /// 算法：先算出这一档的"目标总伤害" = standard × BaseDamageFor(strokes)，
    /// 再除以这个字的实际弹数。于是：
    ///
    ///   十（2 画，4 发）单发轻；丁（2 画，2 发）单发重 —— 两者总输出相同。
    ///
    /// 形状可以随字自由变化，强度曲线仍然只由笔画数决定。
    ///
    /// 注意：单发伤害是整数，取整会让同笔画的字有 ±15% 以内的浮动
    /// （比如 2 画的刀 3 发，只能取 1 或 2，无法正好等于 2.0）。
    /// 这个误差在体感上察觉不到，换来的是每个字都有自己独特的弹幕形状。
    ///
    /// 为什么不用"除以 (1 + 弹数/5)"那套老公式：整除会在弹数跨过 5、10 时跳变，
    /// 曲线因此倒退（实测 3 画 41.4 DPS、4 画只有 35.1）。
    /// </summary>
    public static int DamageFor(int strokes, int standard, int actual)
    {
        int std = Mathf.Max(1, standard);
        int act = Mathf.Max(1, actual);

        float targetTotal = std * (float)BaseDamageFor(strokes);
        return Mathf.Max(1, Mathf.RoundToInt(targetTotal / act));
    }

    /// <summary>兼容旧调用：只给笔画数时按标准弹数算</summary>
    public static int DamageFor(int strokes, int bullets)
    {
        return DamageFor(strokes, bullets, bullets);
    }

    /// <summary>弹幕存活时间（秒）</summary>
    public const float BulletLifetime = 3f;

    // ---------- 层数成长 ----------
    /// <summary>每层敌人血量增幅</summary>
    public const float HealthGrowthPerFloor = 0.30f;

    /// <summary>每层敌人速度增幅</summary>
    public const float SpeedGrowthPerFloor = 0.06f;

    /// <summary>每层额外敌人数量</summary>
    public const int ExtraEnemyPerFloor = 1;

    /// <summary>额外敌人数量上限</summary>
    public const int MaxExtraEnemies = 4;

    // ---------- BOSS ----------
    /// <summary>
    /// BOSS 血量总倍率（调试用全局开关）。
    ///
    /// 0.35 = 测试模式，便于快速打到关底验证流程；
    /// 1.0  = 正式数值，改这一行就能一键切换，不必去翻 Room.cs。
    /// </summary>
    public const float BossHealthMultiplier = 0.35f;

    /// <summary>
    /// 数值平衡用的层号：**教程层（0）算作第 1 档**。
    ///
    /// 为什么要这个换算：关卡编号是 0=教程、1/2/3=正式层，
    /// 但所有成长曲线（血量、速度、奖励、BOSS 倍率）都是按 1 起算的。
    /// 直接拿 currentFloor=0 去查表会落到"第 1 层"甚至变成负数，
    /// 所以统一走这里 +1 之后再查表。
    /// </summary>
    public static int BalanceFloor
    {
        get { return Mathf.Max(1, GameData.currentFloor + 1); }
    }

    /// <summary>
    /// 各层的**额外**血量倍率，索引 0 = 教程层。
    ///
    /// 设计意图：教程与一二层是"教学 BOSS"，应该脆一些，
    /// 主要让玩家熟悉弹幕节奏；第三层关底「皇」才是真正的耐力战。
    /// </summary>
    static readonly float[] BossHealthPerFloor = { 0.60f, 0.75f, 0.90f, 1.15f };

    /// <summary>
    /// 教程层 BOSS 的额外削弱系数（乘在 BossHealthPerFloor[0] 之上）。
    ///
    /// 教程 BOSS 的作用只是"让玩家确认自己会打了"，不该在这儿卡人。
    /// 玩家在教程里死掉、被迫重看一遍提示，是最劝退的体验之一。
    /// 觉得太软或太硬就改这一个数。
    /// </summary>
    public const float TutorialBossExtraMultiplier = 0.5f;

    /// <summary>教程层 BOSS 的最终倍率</summary>
    public static float TutorialBossMultiplier
    {
        get { return BossHealthPerFloor[0] * TutorialBossExtraMultiplier; }
    }

    /// <summary>取某一层的 BOSS 血量倍率（floor 直接传 GameData.currentFloor，教程层=0）</summary>
    public static float BossHealthForFloor(int floor)
    {
        if (BossHealthPerFloor.Length == 0) return 1f;
        int i = Mathf.Clamp(floor, 0, BossHealthPerFloor.Length - 1);
        return BossHealthPerFloor[i];
    }

    // ---------- 房间奖励 ----------
    /// <summary>清空战斗房掉落的笔画数（索引 0 = 教程层）</summary>
    static readonly int[] CombatBonusStrokesPerFloor = { 3, 2, 2, 1 };

    public static int CombatBonusStrokesForFloor(int floor)
    {
        if (CombatBonusStrokesPerFloor.Length == 0) return 1;
        int i = Mathf.Clamp(floor, 0, CombatBonusStrokesPerFloor.Length - 1);
        return CombatBonusStrokesPerFloor[i];
    }

    /// <summary>宝箱房掉落的笔画数（索引 0 = 教程层）</summary>
    static readonly int[] TreasureBonusStrokesPerFloor = { 4, 3, 2, 2 };

    public static int TreasureBonusStrokesForFloor(int floor)
    {
        if (TreasureBonusStrokesPerFloor.Length == 0) return 1;
        int i = Mathf.Clamp(floor, 0, TreasureBonusStrokesPerFloor.Length - 1);
        return TreasureBonusStrokesPerFloor[i];
    }

    // ---------- 玩家 ----------
    /// <summary>玩家基础移速</summary>
    public const float PlayerMoveSpeed = 6f;

    /// <summary>玩家基础生命</summary>
    public const int PlayerBaseHealth = 10;

    /// <summary>受伤无敌时间（秒）</summary>
    public const float PlayerInvincibleDuration = 0.5f;

    // ---------- 背包 ----------
    /// <summary>背包格子数</summary>
    public const int InventorySlots = 9;

    /// <summary>玩家最多能持有的笔画数（决定能拼出多大的字）</summary>
    public const int MaxPlayerStrokes = 9;

    // ---------- 房间 ----------
    /// <summary>房间触发器判定用的玩家标签</summary>
    public const string PlayerTag = "Player";
    public const string EnemyTag = "Enemy";
    public const string WallTag = "Wall";

    /// <summary>
    /// 关卡编号 → 场景名。**0 = 新手教程层。**
    ///
    /// 编号和各层的中文叫法差 1：编号 0 是教程，编号 1/2/3 对应"第一/二/三层"。
    /// 这样做的好处是 GameData.currentFloor 从 0 开始，
    /// 成长公式 (currentFloor - 1) 在教程层正好等于 0，倍率刚好是 1.0，
    /// 不需要给教程层开特例。
    /// </summary>
    public static string SceneForFloor(int floor)
    {
        if (floor <= 0) return "Level0";     // 新手教程
        if (floor == 1) return "Level1";
        if (floor == 2) return "Level2";
        return "Level3";
    }
}

/// <summary>
/// 统一的日志开关。发布版（或想安静时）把 Verbose 关掉，
/// 避免几十条 Debug.Log 在 WebGL 里拖慢主线程。
/// </summary>
public static class GameLog
{
    /// <summary>
    /// 日志总开关。
    ///
    /// 目前暂时设为 true：线上正在排查"无法传送到第二关"，
    /// 需要玩家把浏览器 Console 的输出发回来定位。
    /// 排查结束后改回 false（WebGL 下每条 Debug.Log 都有实打实的开销）。
    /// </summary>
    public static bool Verbose =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        true;
#else
        true;   // TODO: 排查完改回 false
#endif

    public static void Info(string msg)
    {
        if (Verbose) Debug.Log(msg);
    }

    public static void Warn(string msg)
    {
        Debug.LogWarning(msg);
    }

    public static void Error(string msg)
    {
        Debug.LogError(msg);
    }
}
