using UnityEngine;

public class BossShi : MonoBehaviour, IDamageable
{
    [Header("属性")]
    public int maxHealth = 400;
    public float moveSpeed = 3f;
    public float meleeRange = 2f;
    public float meleeDamage = 1f;
    public float meleeCooldown = 1.5f;
    public float shootInterval = 3f;

    [Header("弹幕")]
    public GameObject bulletPrefab;

    private int currentHealth;
    private float nextMeleeTime = 0f;
    private float nextShootTime = 0f;
    private Transform player;
    private Room currentRoom;
    private bool isDead = false;

    void Start()
    {
        currentHealth = maxHealth;
        player = PlayerRef.Transform;
        // BOSS 入场演出：强烈的镜头震动 + 金色爆发，配合 HP 条的"压迫感"
        Fx.Shake(0.85f);
        Fx.RoomClearBurst(transform.position);
        Sfx.Play(Sfx.Id.RoomClear, 0.8f);
        nextShootTime = Time.time + 1f; // 开局1秒后首次射击
    }

    void Update()
    {
        if (player == null || isDead) return;

        Vector2 dir = (player.position - transform.position).normalized;
        float dist = Vector2.Distance(transform.position, player.position);

        // 追踪玩家（保持距离在近战范围内）
        if (dist > meleeRange)
        {
            transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
        }

        // 只水平翻转，不旋转
        Vector3 scale = transform.localScale;
        scale.x = dir.x >= 0 ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        transform.localScale = scale;

        // 近战攻击
        if (dist <= meleeRange + 0.5f && Time.time >= nextMeleeTime)
        {
            MeleeAttack(dir);
            nextMeleeTime = Time.time + meleeCooldown;
        }

        // 远程扇形弹幕
        if (Time.time >= nextShootTime)
        {
            RangedAttack();
            nextShootTime = Time.time + shootInterval;
        }
    }

    void MeleeAttack(Vector2 dir)
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("BossShi 的 bulletPrefab 未设置！近战弹幕无法生成。");
            return;
        }

        GameLog.Info("BOSS「士」近战横斩！");

        // 向前方近距离挥砍
        for (int i = -1; i <= 1; i++)
        {
            Vector3 bulletPos = transform.position + (Vector3)(dir * 1.5f);
            float spreadAngle = 15f * i;
            Vector3 bulletDir = Quaternion.Euler(0, 0, spreadAngle) * (Vector3)dir;

            GameObject bullet = Instantiate(bulletPrefab, bulletPos, Quaternion.identity);
            Bullet b = bullet.GetComponent<Bullet>();

            if (b != null)
            {
                b.SetDirection(bulletDir);
                b.isPlayerBullet = false;
                b.speed = 6f;            // 近战弹幕飞得较慢
                b.lifetime = 1f;         // 寿命较短
                b.damage = (int)meleeDamage;

                // 红色弹幕（近战攻击的颜色）
                SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = Color.red;
            }
        }
    }

    void RangedAttack()
    {
        if (bulletPrefab == null) return;

        GameLog.Info("BOSS「士」扇形弹幕！");

        Vector2 dir = (player.position - transform.position).normalized;

        // 5发扇形弹幕
        for (int i = -2; i <= 2; i++)
        {
            float spreadAngle = 20f * i;
            Vector3 bulletDir = Quaternion.Euler(0, 0, spreadAngle) * (Vector3)dir;

            GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            Bullet b = bullet.GetComponent<Bullet>();

            if (b != null)
            {
                b.SetDirection(bulletDir);
                b.isPlayerBullet = false;
                b.speed = 8f;
                b.lifetime = 3f;

                SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(1f, 0.3f, 0.3f);
            }
        }
    }

    // ---------- IDamageable ----------
public Color FxTint { get { return new Color(0.7f, 0.7f, 0.75f); } }
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
        GameLog.Info($"Boss「士」受伤！剩余血量：{currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void SetRoom(Room room)
    {
        currentRoom = room;
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        GameLog.Info("BOSS「士」被击败！");

        // 通知房间
        if (currentRoom != null)
        {
            currentRoom.OnEnemyDied(gameObject);
        }

        // 通知 GameManager：Boss死亡
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.OnBossKilled(transform.position, currentRoom);
        }

        // 延迟销毁，让日志显示出来
        Destroy(gameObject, 0.5f);
    }
}