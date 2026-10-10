using UnityEngine;

// Buff类型
public enum BuffType
{
    None,
    Fire,       // 灼烧：命中后敌人持续掉血
    Big,        // 变大：弹幕尺寸翻倍
    Fast,       // 加速：射速翻倍
    Pierce,     // 穿透：弹幕不消失，可穿透敌人
    Triple,     // 三连：额外发射2发弹幕
}

public class BulletBuff : MonoBehaviour
{
    [Header("当前Buff")]
    public BuffType currentBuff = BuffType.None;
    public float buffDuration = 10f;    // Buff持续时间（秒）

    private float buffTimer = 0f;
    private HanziSystem hanzi;

    void Start()
    {
        hanzi = GetComponent<HanziSystem>();
    }

    void Update()
    {
        // Buff倒计时
        if (currentBuff != BuffType.None)
        {
            buffTimer -= Time.deltaTime;
            if (buffTimer <= 0)
            {
                RemoveBuff();
            }
        }
    }

    public void ApplyBuff(BuffType buff)
    {
        currentBuff = buff;
        buffTimer = buffDuration;

        // Buff效果
        switch (buff)
        {
            case BuffType.Fast:
                // 射速加倍（修改 PlayerController 的 fireRate）
                PlayerController pc = GetComponent<PlayerController>();
                if (pc != null) pc.fireRate = 0.15f;
                break;
            case BuffType.Big:
                // 弹幕变大在子弹生成时处理（见下文）
                break;
            case BuffType.Fire:
                // 灼烧在子弹命中时处理（见下文）
                break;
            case BuffType.Triple:
                // 三连在子弹生成时处理
                break;
        }

        GameLog.Info("获得Buff: " + buff);
    }

    void RemoveBuff()
    {
        // 恢复默认
        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null) pc.fireRate = 0.3f;

        currentBuff = BuffType.None;
        GameLog.Info("Buff已过期");
    }

    // 给弹幕应用Buff效果（在子弹生成时调用）
    public void ApplyBuffToBullet(GameObject bullet)
    {
        Bullet b = bullet.GetComponent<Bullet>();
        if (b == null) return;

        switch (currentBuff)
        {
            case BuffType.Big:
                bullet.transform.localScale *= 2f;
                b.damage *= 2;
                break;
            case BuffType.Fire:
                // 弹幕变成红色
                SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = Color.red;
                b.hasFireBuff = true;
                b.fireDPS = 1.5f;
                b.fireDuration = 3f;
                break;
            case BuffType.Triple:
                // 在 HanziSystem 中处理多弹幕
                break;
        }
    }
}