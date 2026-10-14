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
    /// 各层 BOSS 的**额外**血量倍率。
    ///
    /// 设计意图：一二层是"教学 BOSS"，应该脆一些，主要让玩家熟悉弹幕节奏；
    /// 第三层关底「皇」才是真正的耐力战。
    /// 索引 0 对应第 1 层，超出范围取最后一个。
    /// </summary>
    static readonly float[] BossHealthPerFloor = { 0.70f, 0.85f, 1.15f };

    /// <summary>取某一层 BOSS 的血量倍率</summary>
    public static float BossHealthForFloor(int floor)
    {
        if (BossHealthPerFloor.Length == 0) return 1f;
        int i = Mathf.Clamp(floor - 1, 0, BossHealthPerFloor.Length - 1);
        return BossHealthPerFloor[i];
    }

    // ---------- 房间奖励 ----------
    /// <summary>清空战斗房额外掉落的笔画数（第 1 层给得多，方便起手）</summary>
    static readonly int[] CombatBonusStrokesPerFloor = { 2, 2, 1 };

    public static int CombatBonusStrokesForFloor(int floor)
    {
        if (CombatBonusStrokesPerFloor.Length == 0) return 1;
        int i = Mathf.Clamp(floor - 1, 0, CombatBonusStrokesPerFloor.Length - 1);
        return CombatBonusStrokesPerFloor[i];
    }

    /// <summary>宝箱房额外掉落的笔画数</summary>
    static readonly int[] TreasureBonusStrokesPerFloor = { 3, 2, 2 };

    public static int TreasureBonusStrokesForFloor(int floor)
    {
        if (TreasureBonusStrokesPerFloor.Length == 0) return 1;
        int i = Mathf.Clamp(floor - 1, 0, TreasureBonusStrokesPerFloor.Length - 1);
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

    /// <summary>层数 → 场景名</summary>
    public static string SceneForFloor(int floor)
    {
        if (floor <= 1) return "Level1";
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
