using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("血量")]
    public int baseMaxHealth = 10;
    public int maxHealth = 10;
    public int currentHealth;

    [Header("无敌时间")]
    public float invincibleDuration = 0.5f;
    private float invincibleTimer = 0f;

    // 防止跨层重复应用加成
    private int appliedBonusHealth = 0;

    void Start()
    {
        // 恢复本轮的词条加成（切层后新实例）
        maxHealth = baseMaxHealth + PlayerStats.bonusMaxHealth;
        appliedBonusHealth = PlayerStats.bonusMaxHealth;

        // 保留跨场景的血量（否则每次切层都满血，肉鸽压力会消失）
        //
        // **只有 carried 严格大于 0 才用它**，并且最后再夹一次范围。
        //
        // 踩过的坑（玩家反馈"过完背景故事后血条消失"）：
        // 实测发现场景里 PlayerHealth.currentHealth 是 **0**，血条忠实地显示成空的
        // —— 所以不是"血条不见了"，是**血量真的是 0**。
        //
        // 根因是 GameData.carriedHealth 的 0 有**两种语义**混在一起：
        //     · "本局还没开始"   → 应该满血
        //     · "上一局打到 0 血" → 应该 0 血
        // 光是 `carried > 0 ? carried : maxHealth` 在某些时序下仍然会落到 0
        // （比如 maxHealth 那一刻还是序列化里的旧值、或 carried 被别处写成负数）。
        //
        // 现在：0 和负数一律当"没开始"，直接满血；最后 Clamp 兜底，
        // 保证**玩家任何情况下都不会以 0 血开局**。
        int carried = GameData.carriedHealth;

        currentHealth = (carried > 0) ? Mathf.Min(carried, maxHealth) : maxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 1, Mathf.Max(1, maxHealth));

        SaveHealthToGameData();

        GameLog.Info($"[血量] 开局 {currentHealth}/{maxHealth}" +
                     $"（carried={carried} 加成={PlayerStats.bonusMaxHealth}）");
    }

    void Update()
    {
        if (invincibleTimer > 0)
            invincibleTimer -= Time.deltaTime;
    }

    void SaveHealthToGameData()
    {
        GameData.carriedHealth = currentHealth;
    }

    public void TakeDamage(int damage)
    {
        if (invincibleTimer > 0) return;   // 无敌中

        // 护罩（来自"竖"笔画的特点）优先吸收：有层数就免掉这次伤害
        PlayerKit.EnsureAttached();
        if (PlayerShield.Instance != null && PlayerShield.Instance.AbsorbHit())
        {
            invincibleTimer = invincibleDuration * 0.5f;   // 仍给一点无敌，避免连续判定
            return;
        }

        currentHealth -= damage;
        invincibleTimer = invincibleDuration;
        SaveHealthToGameData();

        Sfx.Play(Sfx.Id.PlayerHurt, 0.45f);

        // 受伤闪红
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(1f, 0.3f, 0.3f);
            CancelInvoke("ResetColor");
            Invoke("ResetColor", 0.2f);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // 词条：最大生命 +N
    public void AddMaxHealth(int amount)
    {
        if (amount <= 0) return;

        maxHealth += amount;
        currentHealth += amount;          // 同时补满新增的部分
        appliedBonusHealth += amount;

        SaveHealthToGameData();
        GameLog.Info($"最大生命 +{amount} → {currentHealth}/{maxHealth}");
    }

    // 词条：回复生命
    public void Heal(int amount)
    {
        if (amount <= 0) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        SaveHealthToGameData();
        GameLog.Info($"回复 {amount} 点生命 → {currentHealth}/{maxHealth}");
    }

    void ResetColor()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.color = Color.white;
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        Sfx.Play(Sfx.Id.PlayerHurt, 1f);
        Fx.PlayerHit(transform.position);
        Fx.Shake(0.45f);

        GameLog.Info($"玩家死亡！本轮成绩：{GameData.RunSummary()}");

        // 先显示结算页（暂停），由玩家点击后再真正重开 ——
        // 否则直接 LoadScene 玩家看不到任何本轮成绩反馈。
        GameManager gm = GameManager.Instance;
        GameOverUI.Show(gm != null ? (System.Action)gm.RestartRun : FallbackRestart);
    }

    bool isDead;

    /// <summary>没有 GameManager 时的兜底重开路径（和 GameManager.RestartRun 保持一致）</summary>
    void FallbackRestart()
    {
        GameData.RespawnAtRealRun();     // 回第一层、保留起手笔画
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            GameManager.SceneForFloor(GameData.currentFloor));
    }
}
