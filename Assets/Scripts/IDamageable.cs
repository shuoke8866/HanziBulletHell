using UnityEngine;

/// <summary>
/// 可受伤目标。
///
/// 引入原因：Bullet 原本用 GetComponent&lt;BossShi&gt; / GetComponent&lt;BossGuan&gt;
/// 逐个类型判断，结果新增的 BossHuang 被漏掉了 —— 最终 BOSS「皇」完全打不掉血量，
/// 第 3 层无法通关。用接口之后，新加敌人只要实现它就能被打中，不会再漏。
/// </summary>
public interface IDamageable
{
    /// <summary>受到伤害。hitDirection 用于火花朝向与震动方向感，可为 zero。</summary>
    void TakeDamage(int damage, Vector2 hitDirection);

    /// <summary>该目标的代表色（用于特效着色）</summary>
    Color FxTint { get; }

    /// <summary>受击位置</summary>
    Vector3 FxPosition { get; }
}
