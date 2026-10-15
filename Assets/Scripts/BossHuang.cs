using UnityEngine;

/// <summary>
/// 第三层关底 BOSS「皇」。
///
/// 行为：
///   - 保持中远距离，缓慢逼近
///   - 交替释放：环形弹幕 / 扇形弹幕 / 螺旋弹幕
///   - 定期**召唤士兵「兵」**（横竖撇捺构成的字，会主动追击玩家）
///   - 血量低于 50% 后进入「暴怒」：招式更快、召唤更多
///   - 击败后掉落大量笔画并生成传送门
/// </summary>
public class BossHuang : MonoBehaviour, IDamageable
{
    [Header("属性")]
    public int maxHealth = 1100;
    public float moveSpeed = 1.6f;
    public float keepDistance = 6f;

    [Header("攻击间隔")]
    public float ringInterval = 4.5f;
    public float fanInterval = 3.0f;
    public float spiralInterval = 2.5f;
    public float summonInterval = 7f;

    [Header("弹幕")]
    public GameObject bulletPrefab;

    [Header("召唤")]
    public GameObject summonPrefab;          // 士兵「兵」的预制体
    public int summonCount = 2;
    public int maxMinions = 8;               // 场上士兵上限，防止卡死

    [Header("暴怒")]
    public float enrageHealthRatio = 0.5f;
    public float enrageSpeedUp = 0.65f;      // 间隔乘算（越小越快）

    private int currentHealth;
    private Transform player;
    private Room currentRoom;
    private bool isDead = false;
    private bool enraged = false;

    private float nextRingTime;
    private float nextFanTime;
    private float nextSpiralTime;
    private float nextSummonTime;

    private float spiralAngle;
    private int summonsSpawned = 0;

    public bool IsEnraged { get { return enraged; } }

    void Start()
    {
        currentHealth = maxHealth;
        player = PlayerRef.Transform;
        // BOSS 入场演出：强烈的镜头震动 + 金色爆发，配合 HP 条的"压迫感"
        Fx.Shake(0.85f);
        Fx.RoomClearBurst(transform.position);
        Sfx.Play(Sfx.Id.RoomClear, 0.8f);

        nextRingTime = Time.time + 2f;
        nextFanTime = Time.time + 3f;
        nextSpiralTime = Time.time + 4f;
        nextSummonTime = Time.time + 3f;

        GameLog.Info($"===== BOSS「皇」降临！（{maxHealth} HP）=====");
        GameToast.Show(GameText.Boss.皇降临(), new Color(1f, 0.85f, 0.3f));
    }

    void Update()
    {
        if (player == null || isDead) return;

        CheckEnrage();

        Vector2 dir = (player.position - transform.position).normalized;
        float dist = Vector2.Distance(transform.position, player.position);

        // 保持距离
        if (dist > keepDistance)
            transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
        else if (dist < keepDistance - 2f)
            transform.Translate(-dir * moveSpeed * Time.deltaTime, Space.World);

        // 只水平翻转
        Vector3 scale = transform.localScale;
        scale.x = dir.x >= 0 ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        transform.localScale = scale;

        if (Time.time >= nextRingTime) { RingAttack(); nextRingTime = Time.time + Scaled(ringInterval); }
        if (Time.time >= nextFanTime) { FanAttack(dir); nextFanTime = Time.time + Scaled(fanInterval); }
        if (Time.time >= nextSpiralTime) { SpiralAttack(); nextSpiralTime = Time.time + Scaled(spiralInterval); }
        if (Time.time >= nextSummonTime) { SummonSoldiers(); nextSummonTime = Time.time + Scaled(summonInterval); }
    }

    float Scaled(float interval)
    {
        return enraged ? interval * enrageSpeedUp : interval;
    }

    void CheckEnrage()
    {
        if (enraged) return;
        if (currentHealth > maxHealth * enrageHealthRatio) return;

        enraged = true;
        moveSpeed *= 1.4f;
        GameLog.Info("BOSS「皇」暴怒！招式加速、召唤加剧");
        GameToast.Show(GameText.Boss.皇暴怒(), new Color(1f, 0.35f, 0.25f));
    }

    // ---------------- 招式 ----------------

    // 环形：12 发
    void RingAttack()
    {
        if (bulletPrefab == null) return;

        int count = enraged ? 16 : 12;
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            SpawnBullet(transform.position,
                Quaternion.Euler(0, 0, step * i) * Vector2.right, 4.8f, new Color(1f, 0.85f, 0.3f));
    }

    // 扇形：朝玩家 7 发
    void FanAttack(Vector2 dir)
    {
        if (bulletPrefab == null) return;

        int half = enraged ? 4 : 3;
        for (int i = -half; i <= half; i++)
            SpawnBullet(transform.position,
                Quaternion.Euler(0, 0, 16f * i) * (Vector3)dir, 6f, new Color(1f, 0.5f, 0.2f));
    }

    // 螺旋：双旋臂
    void SpiralAttack()
    {
        if (bulletPrefab == null) return;

        for (int arm = 0; arm < 2; arm++)
            SpawnBullet(transform.position,
                Quaternion.Euler(0, 0, spiralAngle + 180f * arm) * Vector2.right,
                5.2f, new Color(0.6f, 0.8f, 1f));

        spiralAngle = Mathf.Repeat(spiralAngle + 33f, 360f);
    }

    // 召唤士兵「兵」
    void SummonSoldiers()
    {
        if (summonPrefab == null)
        {
            Debug.LogWarning("BossHuang: summonPrefab 未设置，无法召唤士兵。");
            return;
        }

        int alive = GameObject.FindGameObjectsWithTag("Enemy").Length;
        if (alive >= maxMinions)
        {
            GameLog.Info($"BOSS「皇」想召唤，但场上敌人已达上限（{alive}/{maxMinions}）");
            return;
        }

        int n = enraged ? summonCount + 1 : summonCount;

        for (int i = 0; i < n; i++)
        {
            // 围绕 BOSS 分散出现
            float ang = (360f / n) * i + summonsSpawned * 25f;
            Vector3 offset = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad), 0f) * 2.2f;

            GameObject minion = Instantiate(summonPrefab, transform.position + offset, Quaternion.identity);

            Enemy e = minion.GetComponent<Enemy>();
            if (e != null)
            {
                e.defId = "bing";
                if (currentRoom != null) e.SetRoom(currentRoom);
            }
        }

        summonsSpawned += n;
        GameLog.Info($"BOSS「皇」召唤了 {n} 名士兵「兵」（累计 {summonsSpawned}）");
        GameToast.Show(GameText.Boss.皇召唤(n), new Color(0.9f, 0.9f, 1f));
    }

    void SpawnBullet(Vector3 pos, Vector3 dir, float speed, Color color)
    {
        GameObject bullet = Instantiate(bulletPrefab, pos, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetDirection(dir);
            b.isPlayerBullet = false;
            b.speed = speed;
            b.damage = 1;
            SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = color;
        }
    }

    // ---------------- 伤害与死亡 ----------------

    // ---------- IDamageable ----------
public Color FxTint { get { return new Color(1f, 0.72f, 0.2f); } }
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

        // 减伤：暴怒前比较"硬"，鼓励玩家先清士兵
        int actual = Mathf.Max(1, Mathf.RoundToInt(damage * (enraged ? 1f : 0.75f)));

        currentHealth -= actual;
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

        GameLog.Info("BOSS「皇」被击败！");
        GameToast.Show(GameText.Boss.皇被击败(), new Color(1f, 0.95f, 0.5f));

        if (currentRoom != null)
            currentRoom.OnEnemyDied(gameObject);

        GameManager gm = GameManager.Instance;
        if (gm != null) gm.OnBossKilled(transform.position, currentRoom);

        Destroy(gameObject, 0.6f);
    }
}
