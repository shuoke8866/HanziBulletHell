using UnityEngine;

/// <summary>
/// 数据驱动的敌人。所有属性/行为/弹幕形态都来自 EnemyDatabase 里的 EnemyDef，
/// 不再在这里硬编码类型分支——加新怪只要往 EnemyDatabase 里加定义。
/// </summary>
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("数据定义")]
    [Tooltip("EnemyDatabase 里的 id，例如 yi / shu / kou / ren / mu / tian / elite_wang")]
    public string defId = "yi";

    [Header("运行时属性（由定义初始化，可被 Room 缩放/覆盖）")]
    public int maxHealth = 3;
    public float moveSpeed = 2f;
    public float shootInterval = 2f;
    public int bulletDamage = 1;

    [Header("弹幕")]
    public GameObject enemyBulletPrefab;

    [Header("召唤")]
    public GameObject summonPrefab;      // 召唤用的小怪预制体

    // 由 Room 设置了血量覆盖，则 Start 时不再用定义默认值重写 maxHealth
    [HideInInspector] public bool healthOverridden = false;

    private EnemyDef def;
    private int currentHealth;
    private float nextShootTime;
    private float nextSummonTime;
    private float spiralAngle;
    private Transform player;
    private Room currentRoom;

    // 冲刺行为
    private enum ChargeState { Approach, Telegraph, Dash, Recover }
    private ChargeState chargeState = ChargeState.Approach;
    private float chargeTimer;
    private Vector2 dashDir;

    // 灼烧
    private bool isOnFire = false;
    private float fireTimer = 0f;
    private float fireDPS = 0f;
    private int fireAccumulated = 0;

    // 减速
    private bool isSlowed = false;
    private float slowTimer = 0f;
    private float slowMultiplier = 1f;

    public bool IsElite { get { return def != null && def.isElite; } }
    public string DisplayName { get { return def != null ? def.displayName : defId; } }

    // 身上显示的汉字（字形子物体）
    private HanziRenderer glyphRenderer;
    private Transform glyphRoot;

    void Start()
    {
        def = EnemyDatabase.Get(defId);

        // 字形必须先建（ApplyDefinition 会往里写颜色）
        EnsureGlyph();

        ApplyDefinition();

        currentHealth = maxHealth;
        player = PlayerRef.Transform;
        nextShootTime = Time.time + shootInterval + Random.Range(0f, 0.5f);
        nextSummonTime = Time.time + (def != null ? def.summonInterval : 8f);
    }

    // 建立/取得字形子物体。敌人本体不再画方块，改成显示自己的汉字。
    void EnsureGlyph()
    {
        if (glyphRenderer != null) return;

        GameObject go = new GameObject("Glyph");
        go.transform.SetParent(transform, false);
        // 稍微拉到镜头方向一点，保证盖在本体之上
        go.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * 0.8f;

        glyphRenderer = go.AddComponent<HanziRenderer>();
        glyphRenderer.textureSize = 64;
        glyphRenderer.pixelsPerUnit = 32f;

        glyphRoot = go.transform;
    }

    // 切换显示的字（换 defId / 被召唤出来时也会用）
    public void SetGlyph(string ch, Color tint)
    {
        EnsureGlyph();
        glyphRenderer.SetCharacter(ch);
        glyphRenderer.SetStrokeColor(tint);
        KeepGlyphUpright();
    }

    /// <summary>
    /// 本体为了"朝向玩家"会把 localScale.x 取反，这会把子物体里的汉字一起镜像。
    /// 这里反向补偿，保证汉字永远是正的、能认出来。
    /// </summary>
    void KeepGlyphUpright()
    {
        if (glyphRoot == null) return;

        float magnitude = Mathf.Abs(glyphRoot.localScale.x);
        if (magnitude < 0.0001f) magnitude = 1f;

        // 本体 x 为负时，子物体再翻一次 → 世界空间里净效果是正的
        float sign = transform.localScale.x < 0f ? -1f : 1f;

        glyphRoot.localScale = new Vector3(magnitude * sign, magnitude, 1f);
    }

    // 用定义填充运行时属性（保留已被 Room 覆盖的血量）
    void ApplyDefinition()
    {
        if (def == null) return;

        if (!healthOverridden)
            maxHealth = def.health;

        moveSpeed = def.moveSpeed;
        shootInterval = def.shootInterval;
        bulletDamage = def.bulletDamage;

        transform.localScale = def.bodyScale;

        // 本体方块不再显示：敌人看起来就是它代表的那个汉字
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.enabled = false;

        // 精英稍大一点，便于一眼区分
        if (glyphRoot != null)
            glyphRoot.localScale = Vector3.one * (def.isElite ? 1.0f : 0.8f);

        SetGlyph(def.glyph, def.bodyColor);
    }

    void Update()
    {
        if (player == null) return;

        UpdateStatusTimers();

        Vector2 toPlayer = (player.position - transform.position);
        float dist = toPlayer.magnitude;
        Vector2 dir = dist > 0.0001f ? toPlayer / dist : Vector2.right;

        MoveByBehavior(dir, dist);

        // 只水平翻转，不旋转
        Vector3 scale = transform.localScale;
        scale.x = dir.x >= 0 ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        transform.localScale = scale;

        // 关键：本体翻转时，汉字子物体要反向补偿，否则汉字会被镜像成"反字"
        KeepGlyphUpright();

        // 射击
        if (def != null && def.shotPattern != EnemyShotPattern.None && Time.time >= nextShootTime)
        {
            Shoot(dir);
            nextShootTime = Time.time + shootInterval;
        }

        // 召唤
        if (def != null && def.canSummon && Time.time >= nextSummonTime)
        {
            SummonMinions();
            nextSummonTime = Time.time + def.summonInterval;
        }

        TickBurning();
    }

    void UpdateStatusTimers()
    {
        if (isSlowed)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0)
            {
                isSlowed = false;
                slowMultiplier = 1f;
            }
        }
    }

    // ================= 移动行为 =================
    void MoveByBehavior(Vector2 dir, float dist)
    {
        float spd = moveSpeed * slowMultiplier;

        switch (def.behavior)
        {
            case EnemyBehavior.Chase:
                if (dist > 2f)
                    transform.Translate(dir * spd * Time.deltaTime, Space.World);
                break;

            case EnemyBehavior.ChaseShoot:
                // 保持中距离：太远靠近，太近后退
                if (dist > 4f)
                    transform.Translate(dir * spd * Time.deltaTime, Space.World);
                else if (dist < 2.5f)
                    transform.Translate(-dir * spd * Time.deltaTime, Space.World);
                break;

            case EnemyBehavior.Orbit:
            {
                // 环绕：保持半径 + 切向移动
                float desired = 5f;
                Vector2 tangent = new Vector2(-dir.y, dir.x);
                Vector2 radial = dir * Mathf.Clamp(dist - desired, -1f, 1f);
                Vector2 move = (tangent * 0.8f + radial).normalized;
                transform.Translate(move * spd * Time.deltaTime, Space.World);
                break;
            }

            case EnemyBehavior.Turret:
                // 固定不动
                break;

            case EnemyBehavior.Charge:
                UpdateCharge(dir, dist, spd);
                break;
        }
    }

    void UpdateCharge(Vector2 dir, float dist, float spd)
    {
        chargeTimer -= Time.deltaTime;

        switch (chargeState)
        {
            case ChargeState.Approach:
                if (dist > 3.5f)
                    transform.Translate(dir * spd * Time.deltaTime, Space.World);
                else
                {
                    chargeState = ChargeState.Telegraph;
                    chargeTimer = 0.4f;   // 蓄力预警
                    dashDir = dir;
                    SetTint(new Color(1f, 0.2f, 0.2f));
                }
                break;

            case ChargeState.Telegraph:
                // 蓄力时不移动，给玩家反应时间
                if (chargeTimer <= 0f)
                {
                    chargeState = ChargeState.Dash;
                    chargeTimer = 0.35f;
                    dashDir = ((Vector2)player.position - (Vector2)transform.position).normalized;
                }
                break;

            case ChargeState.Dash:
                transform.Translate(dashDir * spd * 2.6f * Time.deltaTime, Space.World);
                if (chargeTimer <= 0f)
                {
                    chargeState = ChargeState.Recover;
                    chargeTimer = 0.6f;
                }
                break;

            case ChargeState.Recover:
                if (chargeTimer <= 0f)
                {
                    chargeState = ChargeState.Approach;
                    if (def != null) SetTint(def.bodyColor);
                }
                break;
        }
    }

    void SetTint(Color c)
    {
        // 本体 SpriteRenderer 已关闭（敌人显示的是汉字），所以染色要作用在字形上
        if (glyphRenderer != null) glyphRenderer.SetStrokeColor(c);
    }

    // ================= 弹幕 =================
    void Shoot(Vector2 dir)
    {
        if (enemyBulletPrefab == null) return;

        switch (def.shotPattern)
        {
            case EnemyShotPattern.Aimed:
                Fire(dir);
                break;

            case EnemyShotPattern.DoubleAimed:
                Fire(Rotate(dir, 12f));
                Fire(Rotate(dir, -12f));
                break;

            case EnemyShotPattern.Fan:
                for (int i = -1; i <= 1; i++)
                    Fire(Rotate(dir, 22f * i));
                break;

            case EnemyShotPattern.Ring:
                for (int i = 0; i < 8; i++)
                    Fire(Quaternion.Euler(0, 0, 45f * i) * Vector2.right);
                break;

            case EnemyShotPattern.Spiral:
                // 每次旋转固定角度，形成螺旋
                for (int arm = 0; arm < 2; arm++)
                    Fire(Quaternion.Euler(0, 0, spiralAngle + 180f * arm) * Vector2.right);
                spiralAngle = Mathf.Repeat(spiralAngle + 27f, 360f);
                break;

            case EnemyShotPattern.Wall:
            {
                // 一排平行弹，垂直于朝向
                Vector2 perp = new Vector2(-dir.y, dir.x);
                for (int i = -2; i <= 2; i++)
                    FireAt((Vector2)transform.position + perp * (i * 0.6f), dir);
                break;
            }

            case EnemyShotPattern.Twin:
                Fire(Vector2.up);
                Fire(Vector2.down);
                break;

            case EnemyShotPattern.Burst:
                // 三连点射：用极短间隔连发（简化为一次三发微散）
                Fire(dir);
                Fire(Rotate(dir, 8f));
                Fire(Rotate(dir, -8f));
                break;
        }
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        return Quaternion.Euler(0, 0, degrees) * v;
    }

    void Fire(Vector2 dir)
    {
        FireAt(transform.position, dir);
    }

    void FireAt(Vector2 pos, Vector2 dir)
    {
        GameObject bullet = Instantiate(enemyBulletPrefab, pos, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetDirection(dir);
            b.isPlayerBullet = false;
            b.speed = def.bulletSpeed;
            b.damage = Mathf.Max(1, bulletDamage);

            SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = def.isElite ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.4f, 0f);
        }
    }

    void SummonMinions()
    {
        if (summonPrefab == null || def == null) return;

        for (int i = 0; i < 2; i++)
        {
            Vector3 offset = new Vector3(i == 0 ? 1.5f : -1.5f, 1f, 0f);
            GameObject minion = Instantiate(summonPrefab, transform.position + offset, Quaternion.identity);

            Enemy e = minion.GetComponent<Enemy>();
            if (e != null)
            {
                if (!string.IsNullOrEmpty(def.summonId)) e.defId = def.summonId;
                if (currentRoom != null) e.SetRoom(currentRoom);
            }
        }
        GameLog.Info($"[{DisplayName}] 召唤了 2 只小怪");
    }

    // ================= 伤害与状态 =================
    void TickBurning()
    {
        if (!isOnFire) return;

        fireTimer -= Time.deltaTime;
        fireAccumulated += Mathf.RoundToInt(fireDPS * 100f * Time.deltaTime);

        int fireDamage = fireAccumulated / 100;
        if (fireDamage > 0)
        {
            fireAccumulated -= fireDamage * 100;
            currentHealth -= fireDamage;
        }

        if (fireTimer <= 0)
        {
            isOnFire = false;
            fireAccumulated = 0;
        }
        if (currentHealth <= 0) Die();
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, Vector2.zero);
    }

    /// <summary>带命中方向的版本：用于火花朝向与屏幕震动的方向感</summary>
    public void TakeDamage(int damage, Vector2 hitDirection)
    {
        currentHealth -= damage;

        Fx.HitSpark(transform.position, hitDirection, FxTint);
        Fx.Shake(0.15f);

        Sfx.PlayVaried(Sfx.Id.Hit, 0.22f, 0.12f);
        if (currentHealth <= 0) Die();
    }

    // ---------- IDamageable ----------
    public Color FxTint { get { return def != null ? def.bodyColor : Color.white; } }
    public Vector3 FxPosition { get { return transform.position; } }

    public void ApplyFire(float dps, float duration)
    {
        isOnFire = true;
        fireTimer = duration;
        fireDPS = dps;
        fireAccumulated = 0;
    }

    public void ApplySlow(float multiplier, float duration)
    {
        isSlowed = true;
        slowMultiplier = multiplier;   // 0.5 = 减速50%
        slowTimer = duration;
    }

    // 接触伤害（冲刺怪/近战怪）
    void OnTriggerStay2D(Collider2D other)
    {
        if (def == null || def.meleeDamage <= 0f) return;
        if (!other.CompareTag("Player")) return;

        PlayerHealth ph = other.GetComponent<PlayerHealth>();
        if (ph != null) ph.TakeDamage(Mathf.RoundToInt(def.meleeDamage));
    }

    public void SetRoom(Room room)
    {
        currentRoom = room;
    }

    void Die()
    {
        Fx.DeathBurst(transform.position, FxTint);
        Fx.Shake(0.28f);
        Sfx.PlayVaried(Sfx.Id.EnemyDie, 0.30f, 0.10f);

        if (currentRoom != null)
            currentRoom.OnEnemyDied(gameObject);
        GameManager.Instance?.OnEnemyKilled(transform.position);
        Destroy(gameObject);
    }
}
