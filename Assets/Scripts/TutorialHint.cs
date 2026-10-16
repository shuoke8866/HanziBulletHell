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
    /// <summary>
    /// 这组提示对应 <see cref="GameText.Tutorial"/> 里的哪一组。
    ///
    /// 选了分组就**每帧从 GameText 现取**，不读序列化的 lines ——
    /// 于是改 GameText.Tutorial 里的文案立刻生效，不用重新生成教程层。
    ///
    /// 为什么这么做：原来文案是生成场景时**烤进场景文件**的，
    /// 改了 GameText 却看不到变化，很容易误以为"工具没生效"。
    /// </summary>
    public enum TutorialGroup
    {
        None = 0,       // 用手填的 lines
        Start = 1,      // 起始房：认全六种笔画
        Collect = 2,    // 收集房：捡笔画与自动合成
        Swap = 3,       // 换字房：切换与背包
        Boss = 4        // 头目前
    }

    [Header("文案来源")]
    [Tooltip("选了分组就每帧从 GameText 现取，改文案立刻生效；None 才用手填的 lines")]
    public TutorialGroup group = TutorialGroup.None;

    [Header("手填提示（group = None 时用）")]
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

    /// <summary>当前该显示哪几条 —— 按分组现取，或用手填的</summary>
    List<string> CurrentLines
    {
        get
        {
            List<string> raw;
            switch (group)
            {
                case TutorialGroup.Start: raw = new List<string>(GameText.Tutorial.起始房()); break;
                case TutorialGroup.Collect: raw = new List<string>(GameText.Tutorial.收集房()); break;
                case TutorialGroup.Swap: raw = new List<string>(GameText.Tutorial.换字房()); break;
                case TutorialGroup.Boss: raw = new List<string>(GameText.Tutorial.头目前()); break;
                default: raw = lines; break;
            }

            // 丢掉空行。
            //
            // 为什么：改了文案之后，某一条可能是空字符串（".bak" 里就有一条 ""）。
            // 空的那条会**显示一个空的提示框停满一整个周期**，
            // 玩家看起来就是"字幕卡住了 / 有字幕没出来"。
            // 与其每次都去查文案里有没有空行，不如这里直接过滤掉。
            if (raw == null) return new List<string>();

            var clean = new List<string>();
            foreach (string s in raw)
                if (!string.IsNullOrWhiteSpace(s)) clean.Add(s);

            return clean;
        }
    }

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

        BeginShow();
    }

    /// <summary>
    /// 开局自检：玩家**已经站在提示范围内**时也要显示。
    ///
    /// ## 为什么必须有这个
    ///
    /// `OnTriggerEnter2D` 只在**跨越边界的那一瞬间**触发。
    /// 如果玩家出生时就已经在触发器里面（教程层起始房的提示牌正好覆盖出生点），
    /// 或者玩家是从别的地方传送/重生进来的，Unity **不会补发** Enter 事件 ——
    /// 结果就是"字幕永远不出现"。
    ///
    /// 这是物理引擎的通用行为（Box2D / PhysX 都一样），不是 bug，
    /// 但用法上必须自己兜住。
    ///
    /// 用 OverlapBox 主动查一次，等价于"补发一个 Enter"。
    /// </summary>
    void CheckPlayerAlreadyInside()
    {
        if (onceOnly && consumed) return;
        if (playerInside) return;

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Collider2D hit = Physics2D.OverlapBox(box.bounds.center, box.bounds.size,
                                              0f, LayerMask.GetMask("Default", "Player"));
        if (hit == null) return;

        if (!hit.CompareTag("Player")) return;

        BeginShow();
    }

    /// <summary>开始显示这组提示（Enter 和开局自检共用）</summary>
    void BeginShow()
    {
        playerInside = true;
        shownCount = 0;
        lineTimer = 0f;

        if (activeHints.Count == 0) EnsurePanel();
        if (!activeHints.Contains(this)) activeHints.Add(this);

        AdvanceLine();
    }

    void Start()
    {
        // 场景加载完先查一次 —— 出生就在范围里的提示牌靠这个才出得来
        StartCoroutine(CheckInsideNextFrame());
    }

    System.Collections.IEnumerator CheckInsideNextFrame()
    {
        // 等一帧：玩家和物理体都初始化完再查，否则 OverlapBox 可能什么都碰不到
        yield return null;
        CheckPlayerAlreadyInside();
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

        // 每次推进一条；推进不动了（已经全显示完）就收起。
        //
        // 这里不能写成 `if (shownCount < CurrentLines.Count) AdvanceLine(); else HidePanel();`
        // —— AdvanceLine 内部会把 shownCount 自增，所以显示最后一条之后
        // shownCount 已经等于 Count，那个判断会让**最后一条多停一整个周期**
        // 才收起（off-by-one）。
        // 直接调 AdvanceLine()，它自己处理"没有下一条了"的情况。
        AdvanceLine();
    }

    /// <summary>显示下一条；没有了就收起</summary>
    void AdvanceLine()
    {
        var src = CurrentLines;

        if (shownCount >= src.Count)
        {
            HidePanel();
            consumed = true;
            return;
        }

        EnsurePanel();

        sharedText.text = src[shownCount];
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
