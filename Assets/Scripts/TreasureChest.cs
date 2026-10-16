using UnityEngine;

public class TreasureChest : MonoBehaviour
{
    [Header("掉落笔画")]
    public StrokeType dropStrokeType = StrokeType.Heng;

    /// <summary>
    /// 开局随机化掉落笔画。**默认开** —— 玩家要求"每一个宝箱都是随机生成笔画"。
    ///
    /// 关掉就用 Inspector 里填的 dropStrokeType 固定值（单独调某个宝箱时有用）。
    /// </summary>
    public bool randomizeDrop = true;

    /// <summary>六种笔画</summary>
    static readonly StrokeType[] AllStrokes =
    {
        StrokeType.Heng, StrokeType.Shu, StrokeType.Pie,
        StrokeType.Na, StrokeType.Dian, StrokeType.Zhe
    };

    [Header("视觉")]
    public SpriteRenderer chestSprite;

    private bool isOpened = false;
    private bool playerNearby = false;

    void Start()
    {
        if (randomizeDrop)
            dropStrokeType = AllStrokes[Random.Range(0, AllStrokes.Length)];
    }

    // 给玩家一个提示（按 F 打开）
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && !isOpened)
        {
            playerNearby = true;
            GameLog.Info("接近宝箱！按 F 打开");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = false;
        }
    }

    void Update()
    {
        if (playerNearby && Input.GetKeyDown(KeyCode.F) && !isOpened)
        {
            OpenChest();
        }
    }

    public void OpenChest()
    {
        if (isOpened) return;
        isOpened = true;

        Sfx.Play(Sfx.Id.Boon, 0.45f);
        Fx.PickupPop(transform.position, new Color(1f, 0.9f, 0.5f));
        GameLog.Info("宝箱打开！获得笔画：" + dropStrokeType);

        // 玩家**头顶**飘一行字，告诉他捡到了什么。
        // 锚在玩家身上而不是宝箱上 —— 玩家的视线在自己角色上，不在箱子上。
        FloatingTip.Show(GameText.Toast.获得笔画(StrokeUtil.NameOf(dropStrokeType)),
                         StrokeUtil.ColorOf(dropStrokeType));

        // 玩家头顶飘一行字，告诉他捡到了什么。
        // 宝箱位置和玩家位置可能差一点，所以锚在**玩家**头上而不是宝箱上。
        FloatingTip.Show(GameText.Toast.获得笔画(StrokeUtil.NameOf(dropStrokeType)),
                         StrokeUtil.ColorOf(dropStrokeType));

        // 直接把笔画给玩家；合不上就进背包（和拾取逻辑一致，避免白白浪费）
        GameObject player = PlayerRef.GameObject;
        if (player != null)
        {
            HanziSystem hanzi = player.GetComponent<HanziSystem>();
            StrokeInventory inv = player.GetComponent<StrokeInventory>();

            if (hanzi != null && hanzi.TryAddStroke(dropStrokeType))
            {
                GameData.strokesCollected++;
                GameLog.Info("新笔画已应用！当前汉字：" + hanzi.currentHanziName);
            }
            else if (inv != null && inv.AddStroke(dropStrokeType))
            {
                GameData.strokesCollected++;
                GameLog.Info("笔画无法合成，已放入背包。");
            }
            else
            {
                Debug.LogWarning("合成失败且背包已满，笔画丢失。");
            }
        }

        // 视觉变化（变暗）
        if (chestSprite != null)
        {
            chestSprite.color = new Color(0.5f, 0.5f, 0.5f);
        }

        // 宝箱消失
        Destroy(gameObject, 0.3f);
    }
}