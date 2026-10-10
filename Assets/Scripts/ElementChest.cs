using UnityEngine;

public class ElementChest : MonoBehaviour
{
    [Header("奖励元素")]
    public ElementType rewardElement = ElementType.Fire;

    [Header("视觉")]
    public Color fireChestColor = new Color(1f, 0.3f, 0.2f);
    public Color waterChestColor = new Color(0.3f, 0.5f, 1f);

    private bool isOpened = false;

    void Start()
    {
        // 按元素类型设置宝箱颜色
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = rewardElement == ElementType.Fire ? fireChestColor : waterChestColor;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && !isOpened)
        {
            OpenChest();
        }
    }

    void OpenChest()
    {
        if (isOpened) return;
        isOpened = true;

        GameLog.Info($"特殊宝箱打开！获得元素：{rewardElement}");

        GameObject player = PlayerRef.GameObject;
        if (player != null)
        {
            ElementSystem elem = player.GetComponent<ElementSystem>();
            if (elem != null)
            {
                elem.SetElement(rewardElement);
            }
            else
            {
                Debug.LogError("❌ Player 上没有 ElementSystem 组件！");
            }
        }

        // 宝箱消失
        Destroy(gameObject, 0.3f);
    }
}