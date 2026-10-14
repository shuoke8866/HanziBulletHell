using UnityEngine;

/// <summary>
/// 近身斩击（来自"撇/捺"笔画的特点）。
///
/// 最简版按需求约定实现：
///   - 敌人进入斩击范围时自动挥砍，造成一次伤害并击退
///   - 有短暂冷却，避免贴身时每帧狂砍
///   - 范围受 StrokeTraits.SlashRangeMultiplier 影响（"工"字会扩大范围）
///
/// 只对玩家自己生效，不依赖任何美术资源（挥砍用一个程序化弧线特效表示）。
/// </summary>
public class MeleeSlash : MonoBehaviour
{
    public static MeleeSlash Instance { get; private set; }

    [Header("参数")]
    /// <summary>基础斩击半径（世界单位）</summary>
    public float baseRadius = 2.2f;
    /// <summary>斩击冷却（秒）</summary>
    public float cooldown = 0.55f;
    /// <summary>斩击伤害（会乘上玩家伤害倍率）</summary>
    public int baseDamage = 6;
    /// <summary>击退力度</summary>
    public float knockback = 6f;

    float nextSlashTime;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    float CurrentRadius
    {
        get
        {
            float mul = 1f;
            if (StrokeTraits.Instance != null)
                mul = StrokeTraits.Instance.SlashRangeMultiplier;
            return baseRadius * mul;
        }
    }

    void Update()
    {
        // 只有当前汉字带撇/捺才有斩击能力
        if (StrokeTraits.Instance == null || !StrokeTraits.Instance.MeleeSlash) return;
        if (Time.time < nextSlashTime) return;

        float radius = CurrentRadius;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);
        bool struck = false;
        Vector2 lastDir = Vector2.right;
        float nearest = float.MaxValue;

        foreach (Collider2D col in hits)
        {
            if (col == null || !col.CompareTag("Enemy")) continue;

            IDamageable target = col.GetComponent<IDamageable>();
            if (target == null) continue;

            Vector2 dir = ((Vector2)col.transform.position - (Vector2)transform.position).normalized;

            int dmg = Mathf.Max(1, Mathf.RoundToInt(
                baseDamage * PlayerStats.damageMultiplier));

            target.TakeDamage(dmg, dir);
            struck = true;

            // 记住最近的敌人方向，弧光就朝那边扫
            float d = Vector2.Distance(col.transform.position, transform.position);
            if (d < nearest) { nearest = d; lastDir = dir.sqrMagnitude > 0.001f ? dir : Vector2.right; }
        }

        if (struck)
        {
            nextSlashTime = Time.time + cooldown;
            Sfx.PlayVaried(Sfx.Id.Hit, 0.35f, 0.15f);

            // 横斩特效：朝最近的敌人扫一道弧光，玩家能看清斩击范围
            Fx.SlashArc(transform.position, lastDir, new Color(1f, 0.95f, 0.6f), radius * 1.1f);
            Fx.Shake(0.14f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.6f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, CurrentRadius);
    }
}
