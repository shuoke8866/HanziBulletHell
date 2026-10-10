using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家本轮（run）的全局属性加成。
/// 来源：房间清房奖励、Boss 奖励、未来的商店。
/// 由 PlayerController / HanziSystem / PlayerHealth 在需要时读取。
/// </summary>
public static class PlayerStats
{
    // 加法叠加的倍率加成（1.0 = 无加成）
    public static float fireRateMultiplier = 1f;   // 射速（越大越快）
    public static float moveSpeedMultiplier = 1f;  // 移速
    public static float damageMultiplier = 1f;     // 弹幕伤害
    public static float bulletSpeedMultiplier = 1f;// 弹速

    // 加法叠加的固定值
    public static int bonusMaxHealth = 0;
    public static float bonusPickupRadius = 0f;

    // 本轮拿到的词条记录（用于 UI 展示）
    static List<string> acquiredBoons = new List<string>();
    public static IReadOnlyList<string> AcquiredBoons { get { return acquiredBoons; } }

    public static void ResetAll()
    {
        fireRateMultiplier = 1f;
        moveSpeedMultiplier = 1f;
        damageMultiplier = 1f;
        bulletSpeedMultiplier = 1f;
        bonusMaxHealth = 0;
        bonusPickupRadius = 0f;
        acquiredBoons.Clear();
    }

    // 应用一条词条
    public static void Apply(BoonDef boon)
    {
        if (boon == null) return;

        switch (boon.kind)
        {
            case BoonKind.FireRate:   fireRateMultiplier += boon.amount; break;
            case BoonKind.MoveSpeed:  moveSpeedMultiplier += boon.amount; break;
            case BoonKind.Damage:     damageMultiplier += boon.amount; break;
            case BoonKind.BulletSpeed: bulletSpeedMultiplier += boon.amount; break;
            case BoonKind.MaxHealth:  bonusMaxHealth += Mathf.RoundToInt(boon.amount); break;
            case BoonKind.Heal:       break; // 立即治疗，由 PlayerHealth 处理
        }

        acquiredBoons.Add(boon.displayName);
        GameLog.Info($"【词条】获得 {boon.displayName} —— {boon.description}");

        // 立刻把新加成套到玩家身上
        PlayerController pc = Object.FindObjectOfType<PlayerController>();
        if (pc != null) pc.RefreshStats();

        PlayerHealth ph = Object.FindObjectOfType<PlayerHealth>();
        if (ph != null)
        {
            if (boon.kind == BoonKind.MaxHealth) ph.AddMaxHealth(Mathf.RoundToInt(boon.amount));
            if (boon.kind == BoonKind.Heal) ph.Heal(Mathf.RoundToInt(boon.amount));
        }
    }

    public static string BoonsSummary()
    {
        if (acquiredBoons.Count == 0) return "（无）";
        return string.Join(" / ", acquiredBoons.ToArray());
    }
}

public enum BoonKind
{
    FireRate,
    MoveSpeed,
    Damage,
    BulletSpeed,
    MaxHealth,
    Heal
}

[System.Serializable]
public class BoonDef
{
    public string id;
    public string displayName;
    public string description;
    public BoonKind kind;
    public float amount;
    public Color color = Color.white;
}

/// <summary>词条池：清房时随机抽一条</summary>
public static class BoonPool
{
    static List<BoonDef> pool;

    public static List<BoonDef> All
    {
        get { EnsureBuilt(); return pool; }
    }

    static void EnsureBuilt()
    {
        if (pool != null) return;
        pool = new List<BoonDef>
        {
            new BoonDef { id="rate1", displayName="疾书", description="射速 +20%", kind=BoonKind.FireRate, amount=0.20f, color=new Color(1f,0.9f,0.4f) },
            new BoonDef { id="rate2", displayName="神速", description="射速 +35%", kind=BoonKind.FireRate, amount=0.35f, color=new Color(1f,0.8f,0.2f) },
            new BoonDef { id="spd1",  displayName="轻身", description="移速 +15%", kind=BoonKind.MoveSpeed, amount=0.15f, color=new Color(0.5f,1f,0.7f) },
            new BoonDef { id="spd2",  displayName="疾风", description="移速 +30%", kind=BoonKind.MoveSpeed, amount=0.30f, color=new Color(0.3f,1f,0.6f) },
            new BoonDef { id="dmg1",  displayName="重墨", description="弹幕伤害 +1", kind=BoonKind.Damage, amount=1f, color=new Color(1f,0.5f,0.4f) },
            new BoonDef { id="bspd",  displayName="穿云", description="弹速 +25%", kind=BoonKind.BulletSpeed, amount=0.25f, color=new Color(0.6f,0.8f,1f) },
            new BoonDef { id="hp1",   displayName="厚甲", description="最大生命 +2", kind=BoonKind.MaxHealth, amount=2f, color=new Color(0.8f,0.3f,0.3f) },
            new BoonDef { id="heal",  displayName="回春", description="回复 3 点生命", kind=BoonKind.Heal, amount=3f, color=new Color(0.4f,1f,0.5f) },
        };
    }

    public static BoonDef Random()
    {
        EnsureBuilt();
        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }
}
