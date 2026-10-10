using UnityEngine;

public class Door : MonoBehaviour
{
    [Header("组件")]
    public SpriteRenderer doorSprite;      // 门的图片（如果没有就留空）
    public Collider2D doorCollider;        // 门的碰撞体

    [Header("状态")]
    public bool isOpen = true;             // 当前是否打开

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
        isOpen = open;

        if (doorSprite != null)
            doorSprite.enabled = !open;     // 打开时隐藏图片，关闭时显示

        if (doorCollider != null)
            doorCollider.enabled = !open;   // 打开时禁用碰撞，关闭时启用
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