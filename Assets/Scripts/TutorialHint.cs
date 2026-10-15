using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 新手教程的提示牌。
///
/// 用法：挂在一个带 BoxCollider2D(IsTrigger) 的空物体上，填好 lines。
/// 玩家走进去就弹出提示，走出去就收起；每条提示可以设停留时长，
/// 时间到了自动淡出，不挡视线。
///
/// 设计取舍：
///   - 提示出现在**屏幕顶部**而不是角色头顶 —— 弹幕游戏里角色周围永远最乱，
///     文字放头顶必定被弹幕糊住。
///   - 用淡入淡出而不是硬切，避免玩家正在躲弹幕时被突然出现的文字吓一跳。
///   - 不暂停游戏。教程层本身很安全，不需要打断节奏。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class TutorialHint : MonoBehaviour
{
    [Header("提示内容（一行一条，依次显示）")]
    [TextArea(2, 4)]
    public List<string> lines = new List<string>();

    [Header("每条停留时长（秒），0 = 一直显示到玩家离开")]
    public float lineDuration = 6f;

    [Header("外观")]
    public float fontSize = 26f;
    public Color textColor = new Color(1f, 0.97f, 0.88f, 1f);
    public Color boxColor = new Color(0.05f, 0.06f, 0.14f, 0.86f);

    [Header("触发")]
    /// <summary>要不要在玩家第一次进入后就不再重复触发（教学关一般 true）</summary>
    public bool onceOnly = true;

    // ---------- 运行时 ----------
    static GameObject sharedPanel;          // 整个教程共用一个文字面板，避免叠一堆
    static TextMeshProUGUI sharedText;
    static readonly List<TutorialHint> activeHints = new List<TutorialHint>();

    bool playerInside;
    bool consumed;
    int shownCount;
    float lineTimer;

    void Reset()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(10f, 8f);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (onceOnly && consumed) return;

        playerInside = true;
        shownCount = 0;
        lineTimer = 0f;

        if (activeHints.Count == 0) EnsurePanel();
        if (!activeHints.Contains(this)) activeHints.Add(this);

        AdvanceLine();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        playerInside = false;
        activeHints.Remove(this);

        if (activeHints.Count == 0) HidePanel();
    }

    void Update()
    {
        // 只有"当前排在最前面"的提示牌负责计时和推进
        if (activeHints.Count == 0 || activeHints[0] != this) return;
        if (!playerInside) return;

        if (lineDuration <= 0f) return;

        lineTimer += Time.unscaledDeltaTime;
        if (lineTimer < lineDuration) return;

        lineTimer = 0f;

        if (shownCount < lines.Count)
            AdvanceLine();
        else
            HidePanel();
    }

    /// <summary>显示下一条；没有了就收起</summary>
    void AdvanceLine()
    {
        if (shownCount >= lines.Count)
        {
            HidePanel();
            consumed = true;
            return;
        }

        EnsurePanel();

        sharedText.text = lines[shownCount];
        sharedText.color = textColor;
        sharedText.fontSize = fontSize;
        sharedPanel.SetActive(true);

        var img = sharedPanel.GetComponent<Image>();
        if (img != null) img.color = boxColor;

        shownCount++;
        lineTimer = 0f;
    }

    static void EnsurePanel()
    {
        if (sharedPanel != null) return;

        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[教程] 场景里没有 Canvas，提示无法显示");
            return;
        }

        sharedPanel = new GameObject("TutorialHint (Runtime)");
        sharedPanel.transform.SetParent(canvas.transform, false);

        RectTransform rt = sharedPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -18f);
        rt.sizeDelta = new Vector2(860f, 96f);

        Image bg = sharedPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.06f, 0.14f, 0.86f);
        bg.raycastTarget = false;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(sharedPanel.transform, false);

        RectTransform trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(22f, 12f);
        trt.offsetMax = new Vector2(-22f, -12f);

        sharedText = textGo.AddComponent<TextMeshProUGUI>();
        sharedText.fontSize = 26f;
        sharedText.alignment = TextAlignmentOptions.Center;
        sharedText.color = Color.white;
        sharedText.raycastTarget = false;

        sharedPanel.SetActive(false);
    }

    static void HidePanel()
    {
        if (sharedPanel != null) sharedPanel.SetActive(false);
    }

    /// <summary>场景切换时清掉静态引用 —— 旧场景的 Canvas 已经被销毁了</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        sharedPanel = null;
        sharedText = null;
        activeHints.Clear();
    }

    void OnDrawGizmos()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.35f);
        Gizmos.DrawWireCube((Vector2)transform.position + box.offset, box.size);
    }
}
