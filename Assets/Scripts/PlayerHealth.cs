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

        // 保留跨场景的血量比例（否则每次切层都满血，肉鸽压力会消失）
        int carried = GameData.carriedHealth;
        currentHealth = carried > 0 ? Mathf.Min(carried, maxHealth) : maxHealth;

        SaveHealthToGameData();
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
        Sfx.Play(Sfx.Id.PlayerHurt, 1f);
        GameLog.Info($"玩家死亡！本轮成绩：{GameData.RunSummary()}");

        // 真肉鸽：清空所有笔画、层数归 1，由 GameManager 负责重开
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.RestartRun();
        }
        else
        {
            // 兜底：没有 GameManager 也要保证进度被清掉、timeScale 被复位
            GameData.ResetAll();
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                GameManager.SceneForFloor(1));
        }
    }
}
