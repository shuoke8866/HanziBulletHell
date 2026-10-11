using UnityEngine;

/// <summary>
/// 玩家战力估算与 BOSS 血量缩放。
///
/// 要解决的问题：BOSS 血量原本是固定值（皇 = 1100），但玩家 DPS 会随汉字复杂度
/// 从约 6 涨到 75。更麻烦的是**BOSS 战期间玩家无法变强**（笔画只从小怪掉落），
/// 所以 DPS 在整个 BOSS 战里被锁死 —— 同一个 BOSS 对弱玩家要打 77 秒、
/// 对强玩家只要 15 秒，而 77 秒的弹幕战对作品集演示来说是劝退级的。
///
/// 做法：按玩家当前 DPS 反推血量，使击杀时长落在目标区间。
/// </summary>
public static class PlayerPower
{
    /// <summary>
    /// 估算玩家当前每秒伤害。
    ///
    /// 直接复用 HanziSystem.EstimatedDPS，不在这里重写伤害公式 ——
    /// 公式一旦有两份实现必然漂移（改平衡时容易漏掉一份），这个项目已经踩过同类坑。
    /// </summary>
    public static float EstimatedDps()
    {
        HanziSystem hanzi = Object.FindObjectOfType<HanziSystem>();
        if (hanzi == null) return 8f;

        float dps = hanzi.EstimatedDPS;
        return dps > 0.1f ? dps : 8f;
    }

    /// <summary>
    /// 按玩家战力反推 BOSS 血量。
    ///
    /// 注意 desiredSeconds 应当**包含层数倍率**：这个函数算完之后 Room 还会再乘一次
    /// FloorHealthMultiplier。早先的写法是"先算基础血量再乘倍率"，结果 3 层所有 build
    /// 都撞到 maxRatio 上限、时长被倍率单一决定（都变成 42 秒），缩放完全失效。
    ///
    /// 下限（minRatio）刻意压得较低：如果玩家到第 3 层还只有「一」，
    /// 让 BOSS 血量降下来、给他一场 46 秒的仗，好过 170 秒的折磨。
    /// </summary>
    public static int HealthForFight(int baseHealth, float desiredSeconds,
                                     float floorMultiplier = 1f,
                                     float minRatio = 0.15f, float maxRatio = 1.6f)
    {
        if (baseHealth <= 0) return baseHealth;

        float mul = Mathf.Max(0.01f, floorMultiplier);

        // 期望的最终血量（含层数倍率），除以倍率得到"乘倍率之前"的值
        float wantedFinal = EstimatedDps() * desiredSeconds;
        float wantedBase = wantedFinal / mul;

        float ratio = Mathf.Clamp(wantedBase / baseHealth, minRatio, maxRatio);
        return Mathf.Max(1, Mathf.RoundToInt(baseHealth * ratio));
    }
}
