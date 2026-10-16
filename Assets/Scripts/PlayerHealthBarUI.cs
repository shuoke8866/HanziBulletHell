using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 玩家血条 —— 固定在屏幕左下角的 HUD。
///
/// ## 为什么玩家用 HUD 而不是头顶血条
///
/// 玩家永远在画面中心（相机跟着他），所以"HUD 血条"和"头顶血条"看到的效果一样，
/// 但 HUD 有两个好处：
///   · 不会挡在角色身上（子弹地狱里视野很宝贵）
///   · 位置固定，玩家不需要"找"自己的血条
///
/// 敌人则相反：一堆敌人同时在动，必须绑在各自头上才分得清谁快死了。
///
/// ## 自动创建
///
/// 不依赖场景里手摆 UI —— 和 GameToast 一样，运行时自己找 Canvas 建出来。
/// 这样加新关卡不用每个场景都摆一遍。
/// </summary>
public class PlayerHealthBarUI : MonoBehaviour
{
    // ---------- 布局 ----------
    const float BarWidth = 260f;
    const float BarHeight = 26f;
    const float Margin = 28f;

    /// <summary>
    /// 血条的渲染层级 —— **必须高于开场故事面板（500）**，
    /// 否则故事一显示血条就被盖住。独立成一个常量方便以后调。
    /// </summary>
    const int HealthBarSortingOrder = 1000;

    static PlayerHealthBarUI instance;

    Image fill;
    Image back;
    TextMeshProUGUI hpText;

    float shownRatio = 1f;      // 当前显示的比例（平滑追赶真实值）
    int lastCurrent = -1;
    int lastMax = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // **必须用 ReferenceEquals 判真 null，不能用 `!= null`。**
        //
        // 踩过的坑（玩家反馈"正常玩的时候血条没有，只有开场/直接开某层才有"）：
        // 血条挂在【场景里的】Canvas 下面（见 Build 里的 SetParent）。
        // DontDestroyOnLoad 只对自己生效，**不会**保护它的父物体 ——
        // 所以换场景时 Canvas 被销毁，血条跟着一起被销毁。
        //
        // 而这里原来写的是 `if (instance != null) return;`：
        // 被销毁的对象在 Unity 里是**假 null** —— `!= null` 依然返回 true，
        // 于是新场景里以为"已经有一个了"就跳过创建 → 血条永远不再出现。
        //
        // 这就是"第一个场景能看到、之后再也看不到"的完整原因。
        if (!ReferenceEquals(instance, null)) return;

        Create();
    }

    public static void Create()
    {
        if (!ReferenceEquals(instance, null)) return;

        GameObject go = new GameObject("PlayerHealthBar");
        instance = go.AddComponent<PlayerHealthBarUI>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        Build();
    }

    /// <summary>当前挂靠的 Canvas（换场景后旧的会失效，要重新找）</summary>
    Canvas hostCanvas;

    void LateUpdate()
    {
        // ---- 场景切换后重新挂靠 ----
        //
        // 血条本体是 DontDestroyOnLoad，但它**必须挂在某个 Canvas 下**才显示得出来。
        // 换场景时旧的 Canvas 被销毁 → 这里重新找一个并挂过去。
        //
        // 不做这一步的话：血条对象还活着（所以上面的 ReferenceEquals 判断
        // 认为不用重建），但它的父物体没了 → 屏幕上看不到 → 又是一个"假活着"的坑。
        if (hostCanvas == null || hostCanvas.transform == null)
        {
            Canvas found = FindObjectOfType<Canvas>();
            if (found != null)
            {
                hostCanvas = found;
                transform.SetParent(hostCanvas.transform, false);
            }
            else return;    // 还没有 Canvas，下一帧再试
        }
        else if (transform.parent != hostCanvas.transform)
        {
            // 被别的东西抢走了父级，抢回来
            transform.SetParent(hostCanvas.transform, false);
        }
    }

    void Build()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            // 没有 Canvas 就自己建一个（保证任何场景都能显示血条）
            var cgo = new GameObject("HealthBarCanvas");
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();

            // 层级要**高于开场故事面板**（它用 500 全屏遮罩）。
            // 否则故事一显示，血条就被压在下面看不见 ——
            // 玩家看到的就是"血量和背景故事挤在同一个 UI 里"。
            canvas.sortingOrder = HealthBarSortingOrder;
        }

        hostCanvas = canvas;
        transform.SetParent(canvas.transform, false);

        // 场景里的 Canvas 是 sortingOrder=0，盖不过开场故事的 500。
        // 给**血条自己**加一个 Canvas 组件并开 overrideSorting，
        // 这样只有血条这一小块被抬到最上层，不用动整个场景 Canvas 的层级。
        var own = gameObject.GetComponent<Canvas>();
        if (own == null) own = gameObject.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = HealthBarSortingOrder;

        var root = gameObject.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0f, 0f);
        root.sizeDelta = new Vector2(BarWidth, BarHeight);
        root.anchoredPosition = new Vector2(Margin, Margin);

        // 底：暗色边框（做出描边感）
        var border = NewImage("Border", transform, new Color(0f, 0f, 0f, 0.75f));
        Stretch(border.rectTransform, -3f);

        // 槽：空血时的底色
        back = NewImage("Back", transform, new Color(0.18f, 0.10f, 0.12f, 1f));
        Stretch(back.rectTransform, 0f);

        // 填充：从左往右
        fill = NewImage("Fill", transform, new Color(0.35f, 0.85f, 0.45f, 1f));
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 1f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;

        // 数字
        var textGo = new GameObject("HpText");
        textGo.transform.SetParent(transform, false);
        hpText = textGo.AddComponent<TextMeshProUGUI>();
        hpText.fontSize = 18f;
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.color = new Color(1f, 1f, 1f, 0.95f);
        hpText.fontStyle = FontStyles.Bold;
        Stretch(textGo.GetComponent<RectTransform>(), 0f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.sprite = SolidSprite.White;
        img.color = color;
        img.raycastTarget = false;      // 不要挡住鼠标事件

        return img;
    }

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
    }

    /// <summary>
    /// 找玩家身上的 PlayerHealth。
    ///
    /// 不用 PlayerRef.Health —— 那个成员不存在（PlayerRef 只缓存 Transform/GameObject）。
    /// 这里自己查一次并缓存，避免每帧 GetComponent。
    /// </summary>
    static PlayerHealth cached;

    static PlayerHealth PlayerHealthOf()
    {
        // 同样要防假 null：玩家换场景后是**新对象**，旧的 cached 已经是死引用。
        // 用 ReferenceEquals 判真 null，不然会一直返回那个死引用 → 血量不再刷新。
        if (!ReferenceEquals(cached, null))
        {
            GameObject old = PlayerRef.GameObject;
            if (old != null && ReferenceEquals(cached.gameObject, old)) return cached;
        }

        cached = null;

        GameObject p = PlayerRef.GameObject;
        if (p == null) return null;

        cached = p.GetComponent<PlayerHealth>();
        return cached;
    }

    void Update()
    {
        PlayerHealth ph = PlayerHealthOf();
        if (ph == null || fill == null) return;

        int cur = Mathf.Max(0, ph.currentHealth);
        int max = Mathf.Max(1, ph.maxHealth);

        float target = Mathf.Clamp01((float)cur / max);
        shownRatio = Mathf.MoveTowards(shownRatio, target, Time.unscaledDeltaTime * 2.2f);

        // 血条宽度 = 比例 × 总宽
        var fr = fill.rectTransform;
        fr.anchorMax = new Vector2(Mathf.Max(0.0001f, shownRatio), 1f);

        // 颜色随血量变：绿 → 黄 → 红
        if (target > 0.6f)
            fill.color = new Color(0.35f, 0.85f, 0.45f);
        else if (target > 0.3f)
            fill.color = new Color(0.95f, 0.78f, 0.30f);
        else
            fill.color = new Color(0.92f, 0.30f, 0.28f);

        // 低血闪烁提示
        if (target <= 0.25f)
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            Color c = fill.color;
            c.a = pulse;
            fill.color = c;
        }

        if (cur != lastCurrent || max != lastMax)
        {
            lastCurrent = cur;
            lastMax = max;

            if (hpText != null)
                hpText.text = $"{cur} / {max}";
        }
    }
}
