using System.Collections.Generic;
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

    /// <summary>
    /// 子弹尺寸倍率 —— 由 <see cref="EnemyDef.shotScale"/> 在 ApplyDefinition 里写入。
    /// 原来这个字段根本不存在，所以敌人定义器里改"弹幕大小"没有任何作用。
    /// </summary>
    [HideInInspector] public float bulletScale = 1f;

    [Header("召唤")]
    public GameObject summonPrefab;      // 召唤用的小怪预制体

    // 由 Room 设置了血量覆盖，则 Start 时不再用定义默认值重写 maxHealth
    [HideInInspector] public bool healthOverridden = false;

    /// <summary>
    /// 当前敌人对应的数据库定义。
    /// 公开出来是因为 Room.SetupEnemy 要先知道 defId 对应的血量再算层数倍率 ——
    /// 而那时 Enemy.Start() 还没跑、私有字段还是 null。
    /// </summary>
    public EnemyDef def;
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

    /// <summary>
    /// 确保 def 已经按当前 defId 从数据库读出来了。
    ///
    /// 为什么需要它：Room 在 Instantiate 之后**立刻**就要算血量，
    /// 而那时 Unity 还没调用过 Start()，def 仍是 null。
    /// 手动调一次，让"数据库 → def"这一步提前发生。
    /// 重复调用是安全的（只是重新查一次字典）。
    /// </summary>
    public void EnsureDefinition()
    {
        def = EnemyDatabase.Get(defId);
    }

    void Start()
    {
        EnsureDefinition();

        // 字形必须先建（ApplyDefinition 会往里写颜色）
        EnsureGlyph();

        ApplyDefinition();

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

        // 用**敌人自己的字库** —— 和玩家那套完全独立，
        // 所以改敌人的字绝不会连带改掉玩家操控的汉字。
        //
        // 敌人字库是空的时候（还没画）回退到玩家字库，这样游戏照常能玩，
        // 你画一个敌人字形就自动生效一个。
        string useSet = HanziRenderer.SetEnemy;
        if (!HanziRenderer.IsAllowed(ch, HanziRenderer.SetEnemy))
            useSet = HanziRenderer.SetPlayer;

        glyphRenderer.glyphSet = useSet;

        // 破损风格来自 EnemyDef —— 残缺 / 错位 / 倾斜都是**生成贴图时**应用的，
        // 所以调参数立刻见效，不需要为每个敌人手画一遍字形。
        glyphRenderer.SetCharacter(ch, new HanziRenderer.GlyphStyle
        {
            corruption = def != null ? Mathf.Clamp01(def.glyphCorruption) : 0f,
            jitter = def != null ? Mathf.Clamp01(def.glyphJitter) : 0f,
            spin = def != null ? def.glyphSpin : 0f,

            // 种子按 defId + 字 算 —— 同种敌人永远掉同样的笔画。
            //
            // 为什么不用 Random：那样每局、每个关卡掉的笔画都不同，
            // "那个缺一横的王"就不存在了，玩家记不住、也没有辨识度。
            // 现在它是**确定**的：看到那只残缺的王，下次还是同一只。
            seed = StableSeed((def != null ? def.id : defId) + "|" + ch)
        });

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
    /// <summary>
    /// 数据库血量 → 实际血量（乘上全局小怪血量倍率）。
    ///
    /// 所有生成路径都应该用它，不要在别处直接写 def.health ——
    /// 否则"召唤出来的怪"和"房间刷出来的怪"会不一样。
    /// </summary>
    public static int ScaledHealth(int databaseHealth)
    {
        return Mathf.Max(1, Mathf.RoundToInt(databaseHealth * GameBalance.EnemyHealthMultiplier));
    }

    /// <summary>
    /// 稳定种子：同样的字符串永远得到同样的整数。
    ///
    /// 不能用 string.GetHashCode() —— .NET 出于安全考虑让字符串哈希
    /// **每次启动进程都不一样**（哈希随机化），那样"固定的残缺外观"
    /// 换个进程就又变了，白做。所以自己算一个。
    ///
    /// 用 FNV-1a：短、够散、无依赖。
    /// </summary>
    public static int StableSeed(string s)
    {
        if (string.IsNullOrEmpty(s)) return 12345;

        unchecked
        {
            const uint prime = 16777619u;
            uint hash = 2166136261u;

            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= prime;
            }

            // 折成int并保证是正数（System.Random 对负种子也能用，但正数更好读）
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// 把数据库定义套用到运行时属性上。
    ///
    /// <paramref name="keepHealth"/> = true 时**不动血量** ——
    /// 房间刷怪路径已经按"数据库值 × 层数倍率"算好血量了，
    /// 这里再写一次会把层数倍率抹掉。其余属性照常套用。
    ///
    /// 为什么要有这个开关：原来是 `if (!healthOverridden)` 挡住，
    /// 但那样 Room 就必须在 Start() 之前算好血量，而 Start() 之前
    /// `def` 还是 null（数据库读不到），于是只能退而读预制体上序列化的旧值
    /// —— 这就是"改了数据库血量没反应"的根因。
    /// </summary>
    public void ApplyDefinition(bool keepHealth = false)
    {
        if (def == null) return;

        if (!keepHealth && !healthOverridden)
            maxHealth = ScaledHealth(def.health);

        moveSpeed = def.moveSpeed;
        shootInterval = def.shootInterval;
        bulletDamage = def.bulletDamage;

        // 弹幕尺寸 —— 原来这里漏了，所以 EnemyDef.shotScale 改了没反应
        bulletScale = def.shotScale;

        transform.localScale = def.bodyScale;

        // 本体方块不再显示：敌人的"脸"就是它代表的那个汉字
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.enabled = false;

        // 底形（可选）：垫在汉字背后的一层程序生成形状。
        // 有了它，同样写「一」的两种敌人也能一眼分开。
        EnsureBodyShape(def);

        // 精英稍大一点，便于一眼区分
        if (glyphRoot != null)
            glyphRoot.localScale = Vector3.one * (def.isElite ? 1.0f : 0.8f);

        SetGlyph(def.glyph, def.bodyColor);

        // 血量可能刚被改过（比如房间覆盖），同步一次当前值
        currentHealth = maxHealth;
    }

    // ---------- 底形 ----------
    private Transform shapeRoot;
    private SpriteRenderer shapeRenderer;

    void EnsureBodyShape(EnemyDef d)
    {
        Sprite sprite = ProcTex.ForBodyShape(d.bodyShape);

        if (sprite == null)
        {
            // None：有就关掉，没有就不建
            if (shapeRoot != null) shapeRoot.gameObject.SetActive(false);
            return;
        }

        if (shapeRoot == null)
        {
            var go = new GameObject("BodyShape");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.1f);   // 垫在汉字后面
            shapeRenderer = go.AddComponent<SpriteRenderer>();
            shapeRenderer.sortingOrder = -1;
            shapeRoot = go.transform;
        }

        shapeRoot.gameObject.SetActive(true);
        shapeRenderer.sprite = sprite;
        shapeRenderer.color = d.shapeColor;
        shapeRoot.localScale = Vector3.one * Mathf.Max(0.1f, d.shapeScale);
    }

    /// <summary>底形缓慢自转（shapeSpin &gt; 0 时）</summary>
    void UpdateShapeSpin()
    {
        if (shapeRoot == null || def == null) return;
        if (Mathf.Abs(def.shapeSpin) < 0.01f) return;

        shapeRoot.Rotate(0f, 0f, def.shapeSpin * Time.deltaTime);
    }

    void Update()
    {
        if (player == null) return;

        UpdateStatusTimers();
        UpdateShapeSpin();

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

        // 勾了"用玩家形态原语"就走那条路，否则还是老的 7 种固定形态
        if (def.usePlayerShapes)
        {
            ShootWithPlayerShapes(dir);
            return;
        }

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
        SpawnEnemyBullet(pos, dir, bulletScale);
    }

    /// <summary>
    /// 生成一发敌人子弹。
    ///
    /// 原来这里直接写 `b.speed = ...; b.damage = ...;` 而**不调 b.Setup()** ——
    /// 于是 `transform.localScale` 从来没被设过，敌人定义器里改"弹幕大小"
    /// 完全没有效果（改了没反应）。现在统一走 Setup()。
    /// </summary>
    void SpawnEnemyBullet(Vector2 pos, Vector2 dir, float scale)
    {
        if (enemyBulletPrefab == null) return;

        GameObject bullet = Instantiate(enemyBulletPrefab, pos, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b == null) return;

        b.SetDirection(dir);
        b.isPlayerBullet = false;
        b.speed = def != null ? def.bulletSpeed : b.speed;
        b.damage = Mathf.Max(1, bulletDamage);

        // 尺寸必须走 Setup（内部是 localScale *= scale），不能直接赋值
        b.Setup(b.speed, b.damage, Mathf.Max(0.2f, scale), false);

        SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.color = def != null && def.isElite
                ? new Color(1f, 0.25f, 0.25f)
                : new Color(1f, 0.4f, 0f);
    }

    /// <summary>
    /// 用玩家那套形态原语发射一轮（<see cref="EnemyDef.usePlayerShapes"/> 为 true 时）。
    ///
    /// 和玩家共用 BulletShapes —— 加新形态时两边同时受益，不用维护两份实现。
    /// </summary>
    void ShootWithPlayerShapes(Vector2 aimDir)
    {
        var tags = new List<string>();
        if (def.shapeTags != null)
        {
            foreach (string t in def.shapeTags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (BulletShapes.IsModifier(t)) continue;
                tags.Add(t);
            }
        }

        int total = Mathf.Max(1, def.shotCount);
        Vector2 origin = transform.position;

        if (tags.Count == 0)
        {
            SpawnEnemyBullet(origin, aimDir, bulletScale);
            return;
        }

        int[] quota = BulletShapes.SplitQuota(total, tags.Count);

        for (int s = 0; s < tags.Count; s++)
        {
            int n = quota[s];
            if (n <= 0) continue;

            var shots = BulletShapes.Compute(tags[s], n, origin, aimDir,
                                             def.bulletSpeed, bulletScale);
            foreach (BulletShapes.Shot sh in shots)
                SpawnEnemyBullet(sh.position, sh.direction, sh.scale);
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
