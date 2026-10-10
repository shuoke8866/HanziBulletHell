using UnityEngine;
public class Bullet : MonoBehaviour
{
    public float speed = 10f;
    public float lifetime = 3f;
    public int damage = 1;
    public bool isPlayerBullet = true;
    // 穿透属性
    public bool pierce = false;
    public int pierceRemaining = 3;
    // 灼烧Buff
    public bool hasFireBuff = false;
    public float fireDPS = 1f;
    public float fireDuration = 3f;
    // 减速Buff
    public bool hasSlowBuff = false;
    public float slowMultiplier = 0.5f;
    public float slowDuration = 3f;
    private Vector2 moveDirection;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void SetDirection(Vector2 dir)
    {
        moveDirection = dir.normalized;
    }

    // 自定义弹幕属性（由 HanziSystem 调用）
    public void Setup(float spd, int dmg, float scale, bool canPierce)
    {
        speed = spd;
        damage = dmg;
        transform.localScale *= scale;
        pierce = canPierce;
        if (canPierce)
            pierceRemaining = 3;
    }

    void Update()
    {
        transform.Translate(moveDirection * speed * Time.deltaTime, Space.World);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // 玩家弹幕击中敌人
        if (isPlayerBullet && other.CompareTag("Enemy"))
        {
            bool hitTarget = false;

            // 先尝试普通敌人
            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                if (hasFireBuff)
                    enemy.ApplyFire(fireDPS, fireDuration);
                if (hasSlowBuff)
                    enemy.ApplySlow(slowMultiplier, slowDuration);
                hitTarget = true;
            }
            else
            {
                // BossShi
                BossShi bossShi = other.GetComponent<BossShi>();
                if (bossShi != null)
                {
                    bossShi.TakeDamage(damage);
                    hitTarget = true;
                }
                // BossGuan，和BossShi逻辑保持一致
                BossGuan bossGuan = other.GetComponent<BossGuan>();
                if (bossGuan != null)
                {
                    bossGuan.TakeDamage(damage);
                    hitTarget = true;
                }
            }

            // 命中目标才处理穿透
            if (hitTarget)
            {
                if (pierce && pierceRemaining > 0)
                {
                    pierceRemaining--;
                    return;
                }
                Destroy(gameObject);
            }
        }
        // 敌人弹幕击中玩家
        else if (!isPlayerBullet && other.CompareTag("Player"))
        {
            PlayerHealth ph = other.GetComponent<PlayerHealth>();
            if (ph != null)
            {
                ph.TakeDamage(damage);
            }
            Destroy(gameObject);
        }
        // 碰到墙壁消失
        else if (other.CompareTag("Wall"))
        {
            Destroy(gameObject);
        }
    }
}
