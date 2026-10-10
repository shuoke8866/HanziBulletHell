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
        }
    }

    // 当前实际射速（给 UI 展示用）
    public float EffectiveFireRate
    {
        get { return fireRate; }
    }
}
