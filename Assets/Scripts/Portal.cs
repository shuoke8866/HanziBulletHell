using TMPro;
using UnityEngine;

/// <summary>
/// 传送门：进入后前往下一层。
///
/// 触发方式刻意做了冗余 —— 之前出现过"传送门看得见但进不去"，玩家被卡在
/// 关卡之间。这类问题的成因可能很多（碰撞体不是触发器、缺 Rigidbody2D、
/// 生成时玩家已站在里面导致漏掉 Enter 事件、坐标落在走不到的位置），
/// 所以这里不再依赖单一触发路径：
///   1. 生成时自检碰撞体与 Rigidbody，缺失就自动补上并报错
///   2. OnTriggerEnter2D 正常触发
///   3. OnTriggerStay2D / 距离判定作为兜底，并支持按回车进入
///   4. 靠近时显示提示文字，玩家不会不知道这里能进
/// </summary>
public class Portal : MonoBehaviour
{
    [Header("视觉")]
    public SpriteRenderer portalSprite;
    public float rotateSpeed = 30f;

    [Header("兜底交互")]
    [Tooltip("玩家在范围内时，按这个键也能进入下一层")]
    public KeyCode fallbackKey = KeyCode.Return;

    [Tooltip("距离判定半径（用于兜底触发与按键交互）")]
    public float interactRadius = 1.5f;

    /// <summary>防止一帧内被触发两次导致层数 +2。场景切换时会由 OnDestroy 复位。</summary>
    static bool advancing;

    Transform player;
    TextMeshPro hintText;

    void Start()
    {
        if (portalSprite == null)
            portalSprite = GetComponent<SpriteRenderer>();

        SelfCheck();
        BuildHint();
    }

    void OnDestroy()
    {
        // 场景切换时复位，避免下一层的新传送门被旧状态挡住
        advancing = false;
    }

    /// <summary>自检：没有触发器碰撞体的话，传送门永远不可能被触发</summary>
    void SelfCheck()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = interactRadius;
            col = circle;
            Debug.LogError("[传送门] Portal 预制体上没有 Collider2D，已自动补一个圆形触发器。" +
                           "请回到预制体上正式补好碰撞体。");
        }

        if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogError("[传送门] Collider2D 的 isTrigger 为 false，已自动改为 true。" +
                           "请在预制体上勾选 Is Trigger。");
        }

        // 2D 触发要求参与双方至少一方有 Rigidbody2D。玩家本身有，
        // 但这里补一个 Kinematic 的，能让触发判定更可靠。
        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }

        GameLog.Info($"[传送门] 生成于 {transform.position}，自检完成");
    }

    /// <summary>在世界坐标里挂一个"按回车进入"的提示</summary>
    void BuildHint()
    {
        var go = new GameObject("PortalHint");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.2f, 0f);

        hintText = go.AddComponent<TextMeshPro>();
        hintText.text = "按 回车 进入下一层";
        hintText.fontSize = 3.2f;
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.color = new Color(1f, 0.95f, 0.6f);
        hintText.sortingOrder = 80;

        // 让它不要跟着传送门一起旋转
        var follow = go.AddComponent<FaceCamera>();
        follow.cancelParentRotation = true;
        follow.cancelParentScale = true;

        hintText.gameObject.SetActive(false);
    }

    void Update()
    {
        // 注意：旋转只作用在自身的 Sprite，不要转整个 transform，
        // 否则挂在上面的提示文字会跟着转。
        if (portalSprite != null)
            portalSprite.transform.Rotate(0, 0, rotateSpeed * Time.deltaTime);

        if (player == null)
        {
            player = PlayerRef.Transform;
            if (player == null) return;
        }

        float dist = Vector2.Distance(transform.position, player.position);
        bool near = dist <= interactRadius;

        if (hintText != null && hintText.gameObject.activeSelf != near)
            hintText.gameObject.SetActive(near);

        // 兜底：不依赖物理事件，直接按距离判断能否交互
        if (near && Input.GetKeyDown(fallbackKey))
        {
            GameLog.Info("[传送门] 通过按键兜底进入下一层");
            Advance();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        GameLog.Info("[传送门] 玩家进入触发范围");
        Advance();
    }

    void OnTriggerStay2D(Collider2D other)
    {
        // 传送门生成时玩家可能已经站在范围内，此时 Enter 事件不会再来，
        // 所以 Stay 里再给一次按键兜底的机会。
        if (!other.CompareTag("Player")) return;

        if (Input.GetKeyDown(fallbackKey))
        {
            GameLog.Info("[传送门] 通过接触兜底进入下一层");
            Advance();
        }
    }

    void Advance()
    {
        if (advancing) return;

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[传送门] 找不到 GameManager，无法前往下一层。");
            return;
        }

        advancing = true;
        GameLog.Info($"===== 传送门生效：从第 {GameData.currentFloor} 层前往下一层 =====");
        gm.GoToNextFloor();
    }
}

