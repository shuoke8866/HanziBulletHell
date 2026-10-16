using UnityEngine;

/// <summary>
/// 一扇门。
///
/// ## 门的「暂缓打开」机制
///
/// 玩家要求：「在小球说完对应房间的话之前不能开对应房间的门」。
///
/// 做法：门自己想开（<see cref="Open"/>）时，如果 <see cref="HoldOpen"/> 条件成立，
/// 就**不真开**，只记下 <see cref="pendingOpen"/> = true；
/// 之后每帧检查，条件一旦不成立就立刻开。
///
/// 这样 Room 那边**一行都不用改** —— 它照旧在清房时调用 Open()，
/// 门自己决定什么时候真的开。把「能不能开」的判断收在门这里，
/// 而不是散在 Room / 教程 / 关卡脚本里。
/// </summary>
public class Door : MonoBehaviour
{
    [Header("组件")]
    public SpriteRenderer doorSprite;      // 门的图片（如果没有就留空）
    public Collider2D doorCollider;        // 门的碰撞体

    [Header("状态")]
    public bool isOpen = true;             // 当前是否打开

    /// <summary>
    /// 外部挂上的「先别开」条件。返回 true 表示现在还不能开。
    ///
    /// 用委托而不是直接引用 TutorialCoach：
    /// 门不需要知道自己被谁拦着 —— 以后加别的拦截条件（剧情 / 机关 / 战斗）
    /// 也不用改这个类。
    /// </summary>
    public System.Func<bool> HoldOpen;

    /// <summary>有人请求开门，但被 HoldOpen 拦住了</summary>
    bool pendingOpen;

    void Start()
    {
        // 自动获取组件
        if (doorSprite == null)
            doorSprite = GetComponent<SpriteRenderer>();
        if (doorCollider == null)
            doorCollider = GetComponent<Collider2D>();

        SetDoorState(isOpen);
    }

    /// <summary>
    /// 设置门的状态
    /// </summary>
    public void SetDoorState(bool open)
    {
        // 想开但被拦着 → 记下来，等条件解除
        if (open && HoldOpen != null && HoldOpen())
        {
            pendingOpen = true;
            open = false;       // 先保持关着
        }
        else
        {
            pendingOpen = false;
        }

        isOpen = open;

        if (doorSprite != null)
            doorSprite.enabled = !open;     // 打开时隐藏图片，关闭时显示

        if (doorCollider != null)
            doorCollider.enabled = !open;   // 打开时禁用碰撞，关闭时启用
    }

    void Update()
    {
        if (!pendingOpen) return;

        // 拦截条件解除了吗？
        if (HoldOpen != null && HoldOpen()) return;

        pendingOpen = false;
        SetDoorState(true);
    }

    /// <summary>
    /// 开门
    /// </summary>
    public void Open()
    {
        SetDoorState(true);
    }

    /// <summary>
    /// 关门
    /// </summary>
    public void Close()
    {
        SetDoorState(false);
    }
}
