using UnityEngine;

public class StrokePickup : MonoBehaviour
{
    public StrokeType strokeType = StrokeType.Heng;

    private Collider2D playerInRange;
    private bool collected = false;

    void Start()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();

        switch (strokeType)
        {
            case StrokeType.Heng:
                transform.localScale = new Vector3(0.8f, 0.3f, 1f);
                transform.rotation = Quaternion.identity;
                if (sr != null) sr.color = Color.green;
                break;

            case StrokeType.Shu:
                transform.localScale = new Vector3(0.3f, 0.8f, 1f);
                transform.rotation = Quaternion.identity;
                if (sr != null) sr.color = Color.cyan;
                break;

            case StrokeType.Pie:
                transform.localScale = new Vector3(0.8f, 0.3f, 1f);
                transform.rotation = Quaternion.Euler(0, 0, 45f);
                if (sr != null) sr.color = new Color(0.7f, 0.3f, 1f);
                break;

            case StrokeType.Na:
                transform.localScale = new Vector3(0.8f, 0.3f, 1f);
                transform.rotation = Quaternion.Euler(0, 0, -45f);
                if (sr != null) sr.color = new Color(1f, 0.5f, 0f);
                break;

            case StrokeType.Dian:
                transform.localScale = new Vector3(0.35f, 0.35f, 1f);
                transform.rotation = Quaternion.identity;
                if (sr != null) sr.color = Color.yellow;
                break;

            case StrokeType.Zhe:
                transform.localScale = new Vector3(0.6f, 0.6f, 1f);
                transform.rotation = Quaternion.Euler(0, 0, 45f);
                if (sr != null) sr.color = new Color(1f, 0.2f, 0.8f);
                break;
        }
    }

    // 碰上去自动拾取（肉鸽节奏更顺），也可以靠近后按 F
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = other;
            TryCollect(other);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") && playerInRange == other)
            playerInRange = null;
    }

    void Update()
    {
        // 自动拾取失败（比如背包满了）时，允许在范围内按 F 再试一次
        if (playerInRange != null && Input.GetKeyDown(KeyCode.F))
        {
            TryCollect(playerInRange);
        }
    }

    void TryCollect(Collider2D other)
    {
        if (other == null || collected) return;

        HanziSystem hanzi = other.GetComponent<HanziSystem>();
        StrokeInventory inv = other.GetComponent<StrokeInventory>();

        // 先尝试直接合成
        if (hanzi != null && hanzi.TryAddStroke(strokeType))
        {
            collected = true;
            GameData.strokesCollected++;
            Sfx.Play(Sfx.Id.Combine, 0.5f);
            // 合成演出放在玩家身上：笔画已被消耗，放在笔画原位置会显得脱节
            Fx.SynthesisBurst(other.transform.position, StrokeUtil.ColorOf(strokeType));
            Fx.Shake(0.10f);
            Destroy(gameObject);
        }
        // 合成失败 → 进背包
        else if (inv != null && inv.AddStroke(strokeType))
        {
            collected = true;
            GameData.strokesCollected++;
            Sfx.PlayVaried(Sfx.Id.Pickup, 0.45f);
            Fx.PickupPop(transform.position, StrokeUtil.ColorOf(strokeType));
            GameToast.Show(GameText.Toast.拾取存背包(StrokeUtil.NameOf(strokeType)), StrokeUtil.ColorOf(strokeType));
            Destroy(gameObject);
        }
        // 背包也满了 → 不拾取，笔画留在原地（可按 F 重试）
        else if (inv != null)
        {
            Sfx.Play(Sfx.Id.Error, 0.35f);
            GameToast.Show(GameText.Toast.背包已满(StrokeUtil.NameOf(strokeType)), new Color(1f, 0.6f, 0.5f));
        }
    }
}