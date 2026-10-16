using UnityEngine;

public class BossGuan : MonoBehaviour, IDamageable
{
    [Header("属性")]
    public int maxHealth = 700;

    /// <summary>当前血量（BOSS 血条用）</summary>
    public int CurrentHealth { get { return currentHealth; } }

    /// <summary>最大血量（BOSS 血条用）</summary>
    public int MaxHealth { get { return maxHealth; } }
    public float moveSpeed = 2.5f;
    public float keepDistance = 5f;

    [Header("攻击")]
    public float fanInterval = 2.5f;     // 扇形弹幕间隔
    public float ringInterval = 5f;      // 环形弹幕间隔
    public float summonInterval = 8f;    // 召唤间隔

    [Header("弹幕")]
    public GameObject bulletPrefab;

    [Header("召唤")]
    public GameObject summonPrefab;      // 召唤的小怪预制体（Enemy_Yi）

    private int currentHealth;
    private Transform player;
    private Room currentRoom;
    private bool isDead = false;
    private float nextFanTime;
    private float nextRingTime;
    private float nextSummonTime;

    /// <summary>
    /// 这个 BOSS 显示的汉字。统一走 BossGlyph 渲染，
    /// 不再依赖预制体里手拼的图形（那是复制预制体留下的残渣，
    /// 而且贴图引用早就失效了）。
    /// </summary>
    public string glyphChar = "官";

    void Start()
    {
        // 字形：和普通敌人同一套渲染，保证显示的就是这个字
        if (GetComponent<BossGlyph>() == null)
        {
            var bg = gameObject.AddComponent<BossGlyph>();
            bg.character = glyphChar;
        }

        currentHealth = maxHealth;
        player = PlayerRef.Transform;
        // BOSS 入场演出：强烈的镜头震动 + 金色爆发，配合 HP 条的"压迫感"
        Fx.Shake(0.42f);
        Fx.RoomClearBurst(transform.position);
        Sfx.Play(Sfx.Id.RoomClear, 0.8f);
        nextFanTime = Time.time + 1f;
        nextRingTime = Time.time + 2f;
        nextSummonTime = Time.time + 4f;
    }

    void Update()
    {
        if (player == null || isDead) return;

        Vector2 dir = (player.position - transform.position).normalized;
        float dist = Vector2.Distance(transform.position, player.position);

        // 保持距离移动
        if (dist > keepDistance)
            transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
        else if (dist < keepDistance - 2f)
            transform.Translate(-dir * moveSpeed * Time.deltaTime, Space.World);

        // 只水平翻转
        Vector3 scale = transform.localScale;
        scale.x = dir.x >= 0 ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        transform.localScale = scale;

        // 扇形弹幕
        if (Time.time >= nextFanTime)
        {
            FanAttack();
            nextFanTime = Time.time + fanInterval;
        }

        // 环形弹幕
        if (Time.time >= nextRingTime)
        {
            RingAttack();
            nextRingTime = Time.time + ringInterval;
        }

        // 召唤小怪
        if (Time.time >= nextSummonTime)
        {
            SummonMinions();
            nextSummonTime = Time.time + summonInterval;
        }
    }

    void FanAttack()
    {
        GameLog.Info("BOSS「官」扇形弹幕！");
        Vector2 dir = (player.position - transform.position).normalized;

        // 优化：角度从20°扩大到35°，弹速从8降到5.5
        for (int i = -2; i <= 2; i++)
        {
            Vector3 bulletDir = Quaternion.Euler(0, 0, 35f * i) * (Vector3)dir;
            SpawnBullet(transform.position, bulletDir, 5.5f, new Color(1f, 0.3f, 0.3f));
        }
    }

    void RingAttack()
    {
        GameLog.Info("BOSS「官」环形弹幕！");
        // 优化：12发减为8发，弹速6降到4.5，给玩家留出躲闪空隙
        for (int i = 0; i < 8; i++)
        {
            Vector3 bulletDir = Quaternion.Euler(0, 0, 45f * i) * Vector2.right;
            SpawnBullet(transform.position, bulletDir, 4.5f, new Color(1f, 0.6f, 0.2f));
        }
    }

    void SummonMinions()
    {
        GameLog.Info("BOSS「官」召唤小怪！");
        if (summonPrefab == null) return;

        Vector3 spawnPos1 = transform.position + new Vector3(2, 1, 0);
        Vector3 spawnPos2 = transform.position + new Vector3(-2, 1, 0);

        GameObject minion1 = Instantiate(summonPrefab, spawnPos1, Quaternion.identity);
        GameObject minion2 = Instantiate(summonPrefab, spawnPos2, Quaternion.identity);

        // 让召唤的小怪属于当前房间
        Enemy e1 = minion1.GetComponent<Enemy>();
        if (e1 != null && currentRoom != null) e1.SetRoom(currentRoom);

        Enemy e2 = minion2.GetComponent<Enemy>();
        if (e2 != null && currentRoom != null) e2.SetRoom(currentRoom);
    }

    void SpawnBullet(Vector3 pos, Vector3 dir, float speed, Color color)
    {
        if (bulletPrefab == null) return;
        GameObject bullet = Instantiate(bulletPrefab, pos, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetDirection(dir);
            b.isPlayerBullet = false;
            b.speed = speed;
            SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = color;
        }
    }

    // ---------- IDamageable ----------
public Color FxTint { get { return new Color(0.45f, 0.75f, 1f); } }
public Vector3 FxPosition { get { return transform.position; } }

public void TakeDamage(int damage) { TakeDamage(damage, Vector2.zero); }

public void TakeDamage(int damage, Vector2 hitDirection)
{
    Fx.HitSpark(transform.position, hitDirection, FxTint);
    Fx.Shake(0.12f);
    ApplyDamageInternal(damage);
}

void ApplyDamageInternal(int damage)
    {
        if (isDead) return;
        currentHealth -= damage;
        GameLog.Info($"Boss「官」受伤！剩余血量：{currentHealth}");
        if (currentHealth <= 0) Die();
    }

    public void SetRoom(Room room)
    {
        currentRoom = room;
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;
        GameLog.Info("BOSS「官」被击败！");

        if (currentRoom != null)
            currentRoom.OnEnemyDied(gameObject);

        GameManager gm = GameManager.Instance;
        if (gm != null) gm.OnBossKilled(transform.position, currentRoom);

        Destroy(gameObject, 0.5f);
    }
}