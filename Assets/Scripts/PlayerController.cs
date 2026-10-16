using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    public float baseMoveSpeed = 6f;
    public float moveSpeed = 6f;

    [Header("弹幕")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float fireRate = 0.3f;          // 由 HanziSystem 按当前汉字设置
    private float nextFireTime = 0f;

    private Rigidbody2D rb;
    private bool facingRight = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        RefreshStats();
    }

    // 词条变化后重新计算受 PlayerStats 影响的属性
    public void RefreshStats()
    {
        moveSpeed = baseMoveSpeed * PlayerStats.moveSpeedMultiplier;
    }

    void Update()
    {
        Move();          // A/D/W/S 移动
        UpdateFacing();  // 水平翻转（转向）
        Shoot();         // 方向键射击
    }

    void Move()
    {
        float moveX = 0f, moveY = 0f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        rb.velocity = new Vector2(moveX, moveY).normalized * moveSpeed;
    }

    void UpdateFacing()
    {
        // A = 朝左，D = 朝右（只水平翻转，不旋转）
        if (Input.GetKey(KeyCode.A)) facingRight = false;
        if (Input.GetKey(KeyCode.D)) facingRight = true;

        Vector3 s = transform.localScale;
        s.x = facingRight ? Mathf.Abs(s.x) : -Mathf.Abs(s.x);
        transform.localScale = s;
    }

    void Shoot()
    {
        if (Time.time < nextFireTime) return;

        HanziSystem hanzi = GetComponent<HanziSystem>();
        if (hanzi == null) return;

        Vector2 dir = Vector2.zero;

        if (Input.GetKey(KeyCode.UpArrow)) dir = Vector2.up;
        if (Input.GetKey(KeyCode.DownArrow)) dir = Vector2.down;
        if (Input.GetKey(KeyCode.LeftArrow)) { dir = Vector2.left; facingRight = false; }
        if (Input.GetKey(KeyCode.RightArrow)) { dir = Vector2.right; facingRight = true; }

        if (dir != Vector2.zero)
        {
            hanzi.FireBulletPattern(firePoint != null ? firePoint.position : transform.position, dir);
            nextFireTime = Time.time + fireRate;

            // 射击音效：轻音量 + 音高抖动，密集连射时不会吵
            Sfx.PlayVaried(Sfx.Id.Shoot, 0.35f, 0.10f);
            // 极轻的镜头回馈：连射时会形成节奏感，但量很小以免累加成晕眩
            // 原来每开一枪都震一下 —— 射速 0.26 秒时相当于画面一直在抖，
        // 玩家反馈"抖动太强烈"主要就是它。开枪的打击感靠枪口火花和音效已经够了，
        // 不需要每发都晃屏幕。留一点点（0.012）保留手感但不明显。
        Fx.Shake(0.012f);

            // 含「竖」→ 每次射击获得一层护罩
            if (StrokeTraits.Instance != null && StrokeTraits.Instance.ShieldOnFire)
            {
                PlayerKit.EnsureAttached();
                if (PlayerShield.Instance != null) PlayerShield.Instance.GainLayer();
            }
        }
    }

    // 当前实际射速（给 UI 展示用）
    public float EffectiveFireRate
    {
        get { return fireRate; }
    }
}
