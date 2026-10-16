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
        if (!other.CompareTag("Player") || isOpened) return;

        playerNearby = true;
        GameLog.Info("接近宝箱！按 F 打开");

        // 在**宝箱上方**飘一行"按 F 打开宝箱"（玩家要求）。
        // 锚在宝箱上而不是玩家头上 —— 提示指向的是"这个箱子"。
        tip = FloatingTip.ShowAt(GameText.Toast.开宝箱(), PromptColor, transform, 1.2f);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playerNearby = false;

        // 走开就把提示收掉（不然玩家跑到别的房间还会看到"按 F"）
        FloatingTip.Clear(tip);
    }

    /// <summary>当前显示的靠近提示（走开或开箱时清掉）</summary>
    private FloatingTip.Handle tip;

    /// <summary>提示颜色：暖黄，和宝箱的金色呼应</summary>
    static readonly Color PromptColor = new Color(1f, 0.92f, 0.55f);

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

        FloatingTip.Clear(tip);      // 收掉"按 F"的提示
        tip = null;

        GameLog.Info("宝箱打开！获得笔画：" + dropStrokeType);

        // 玩家**头顶**飘一行字，告诉他捡到了什么。
        // 锚在玩家身上而不是宝箱上 —— 玩家的视线在自己角色上，不在箱子上。
        // 连续开箱时 Show 会立刻替换上一条（玩家要求）。
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