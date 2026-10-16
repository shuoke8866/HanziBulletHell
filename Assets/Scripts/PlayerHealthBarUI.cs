using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家血条 —— **左上角**的常驻 HUD。
///
/// ## 为什么重写成这样
///
/// 之前那版用了 `DontDestroyOnLoad` + 运行时自建对象 + 找别人 Canvas，问题不断：
///   · 血条挂在【场景里的】Canvas 下，但 DontDestroyOnLoad 只保护自己
///     → 换场景时 Canvas 没了，血条也看不见了
///   · 判断"有没有"用了 `!= null`，被销毁的对象是**假 null**，
///     判断永远为真 → 换场景后不再重建
///   · 开场故事用 sortingOrder=500 全屏遮罩，把血条（0）压在下面
///
/// 玩家建议"重新做一个血条 UI 显示在左上方，免得怎么改都没用" —— 采纳。
///
/// ## 这一版的设计原则：**能不用技巧就不用**
///
///   1. **不用 DontDestroyOnLoad** —— 血条就是每个场景里的一个普通 UI 对象。
///      场景切换时它随旧场景销毁、随新场景重建，生命周期简单到不会出错。
///      做法：`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 每次场景加载后
///      检查"当前场景里有没有血条"，没有就建一个。**不跨场景保留任何状态。**
///
///   2. **自己建独立 Canvas**，不用 `FindObjectOfType<Canvas>()` 去猜。
///      猜错了会挂到开场故事的 Canvas 下面。自己的 Canvas 层级设 1000，
///      高于开场故事（500），永远不会被盖住。
///
///   3. **判断"有没有"用场景查找**，不用静态引用 —— 静态引用是假 null 陷阱的来源。
///
/// ## 位置
///
/// 屏幕**左上角**，锚点 (0, 1)。
/// </summary>
public class PlayerHealthBarUI : MonoBehaviour
{
    // ---------- 布局 ----------
    const float BarWidth = 280f;
    const float BarHeight = 28f;
    const float MarginX = 26f;
    const float MarginY = 22f;

    /// <summary>层级高于开场故事（500），永不被盖住</summary>
    const int SortingOrder = 1000;

    Image fill;
    TextMeshProUGUI hpText;

    float shownRatio = 1f;
    int lastCurrent = -1;
    int lastMax = -1;

    // ============================================================
    //  创建：每个场景加载后检查一次
    // ============================================================

    // ============================================================
    //  创建：监听"场景加载完成"事件
    // ============================================================
    //
    // **不能用 [RuntimeInitializeOnLoadMethod]！**
    //
    // 玩家反馈"血条是否能出现取决于你在哪一层开始游戏，传送到下一层就没了" ——
    // 根因就是这个特性**只在游戏启动时执行一次**，之后换场景不会再触发。
    // 所以第一层有血条、传送后就没有了。
    //
    // SceneManager.sceneLoaded 是每次场景加载完成都会触发的事件，
    // 这才是"每个场景都要有一个 HUD"的正确做法。
    //
    // 加 [RuntimeInitializeOnLoadMethod] 只是为了**注册这个事件**，
    // 注册本身只需要一次。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 每个场景都建一个自己的血条。
        // 用场景查找判断"有没有"（不用静态引用 —— 那是假 null 陷阱的来源）。
        if (Object.FindObjectOfType<PlayerHealthBarUI>() != null) return;

        var go = new GameObject("PlayerHealthBarUI");
        go.AddComponent<PlayerHealthBarUI>();

        // 注意：**没有 DontDestroyOnLoad** —— 血条随场景生灭，不跨场景保留状态。
    }

    void Start()
    {
        Build();
    }

    void Build()
    {
        // ---- 自己的 Canvas（不去猜别人的） ----
        var canvasGo = new GameObject("HealthBarCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;     // 高于开场故事

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // ---- 血条容器：锚在**左上角** ----
        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(canvasGo.transform, false);

        RectTransform barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);        // 左上
        barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 1f);            // 以左上角为基准
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = new Vector2(MarginX, -MarginY);

        // 描边（比血条大一圈的黑底）
        Image border = NewImage("Border", barGo.transform, new Color(0f, 0f, 0f, 0.8f));
        Stretch(border.rectTransform, -3f);

        // 槽底
        Image back = NewImage("Back", barGo.transform, new Color(0.16f, 0.10f, 0.12f, 1f));
        Stretch(back.rectTransform, 0f);

        // 填充：从左往右长
        fill = NewImage("Fill", barGo.transform, new Color(0.35f, 0.85f, 0.45f, 1f));
        RectTransform fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 1f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;

        // 数字（叠在血条上）
        var numGo = new GameObject("HpText");
        numGo.transform.SetParent(barGo.transform, false);
        hpText = numGo.AddComponent<TextMeshProUGUI>();
        hpText.fontSize = 17f;
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.color = Color.white;
        hpText.fontStyle = FontStyles.Bold;
        Stretch(numGo.GetComponent<RectTransform>(), 0f);

        // 血条下方加个"生命"标签，让它一眼看出是什么
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(canvasGo.transform, false);
        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = "生命";
        label.fontSize = 17f;
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color(1f, 1f, 1f, 0.72f);

        RectTransform lr = labelGo.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(0f, 1f);
        lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 1f);
        lr.sizeDelta = new Vector2(80f, 22f);
        lr.anchoredPosition = new Vector2(MarginX, -MarginY - BarHeight - 4f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = SolidSprite.White;
        img.color = color;
        img.raycastTarget = false;      // 不挡鼠标

        return img;
    }

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
    }

    // ============================================================
    //  每帧刷新
    // ============================================================

    void Update()
    {
        if (fill == null) return;

        // 每帧从玩家身上取一次 —— 玩家换场景后是**新对象**，
        // 所以**不做任何缓存**，避免"缓存指向已销毁对象"这类问题。
        GameObject player = PlayerRef.GameObject;
        if (player == null) return;

        PlayerHealth ph = player.GetComponent<PlayerHealth>();
        if (ph == null) return;

        int max = Mathf.Max(1, ph.maxHealth);
        int cur = Mathf.Clamp(ph.currentHealth, 0, max);

        float target = (float)cur / max;
        shownRatio = Mathf.MoveTowards(shownRatio, target, Time.unscaledDeltaTime * 2.2f);

        // 条宽 = 比例
        RectTransform fr = fill.rectTransform;
        fr.anchorMax = new Vector2(Mathf.Max(0.0001f, shownRatio), 1f);

        // 颜色：绿 → 黄 → 红
        if (target > 0.6f) fill.color = new Color(0.35f, 0.85f, 0.45f);
        else if (target > 0.3f) fill.color = new Color(0.95f, 0.78f, 0.30f);
        else fill.color = new Color(0.92f, 0.30f, 0.28f);

        // 残血闪烁
        if (target <= 0.25f)
        {
            Color c = fill.color;
            c.a = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            fill.color = c;
        }

        if (cur != lastCurrent || max != lastMax)
        {
            lastCurrent = cur;
            lastMax = max;

            if (hpText != null) hpText.text = $"{cur} / {max}";
        }
    }
}
