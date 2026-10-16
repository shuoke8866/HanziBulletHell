using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// BOSS 血条 —— 固定在屏幕**顶部中央**的长条 + 名字 + 数值。
///
/// ## 重做原因
///
/// 旧版有三个问题叠在一起（玩家反馈"BOSS 血条不显现"）：
///   1. 用 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 创建 ——
///      这个特性**只在游戏启动时跑一次**，换场景不再触发，
///      所以从第 2 关开始就没有 BOSS 血条了
///   2. `DontDestroyOnLoad` 但挂在场景 Canvas 下 —— 换场景时 Canvas 没了，
///      血条也看不见
///   3. `SetVisible` 关掉的是**自己所在的 GameObject** ——
///      一关掉 `Update()` 就不跑了，永远发现不了 BOSS（自锁）
///
/// 这一版：
///   · 用 `SceneManager.sceneLoaded` 创建（每次换场景都重建）
///   · **不用 DontDestroyOnLoad** —— 随场景生灭
///   · 自建 Canvas（`sortingOrder = 1000`，高于开场故事）
///   · 隐藏时只关**子物体容器**，自己始终启用（不然 Update 停了）
///   · 找 BOSS 时传 `includeInactive = true` —— 有些 BOSS 预制体
///     在场景里是停用的，只在进房时由 Room 启用
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    // ---------- 布局 ----------
    const float BarWidth = 620f;
    const float BarHeight = 22f;
    const float TopMargin = 26f;

    /// <summary>层级高于开场故事（500）</summary>
    const int SortingOrder = 1000;

    RectTransform contentRoot;      // 真正显示/隐藏的那一层
    Image fill;
    TextMeshProUGUI nameText;
    TextMeshProUGUI hpText;

    float shownRatio = 1f;
    Component currentBoss;
    bool visible;
    float lastTextUpdate;

    // ============================================================
    //  创建
    // ============================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Object.FindObjectOfType<BossHealthBarUI>() != null) return;

        var go = new GameObject("BossHealthBarUI");
        go.AddComponent<BossHealthBarUI>();
    }

    void Start()
    {
        Build();
        SetVisible(false);
    }

    void Build()
    {
        // ---- 自己的 Canvas ----
        var canvasGo = new GameObject("BossBarCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // ---- 内容容器：显示/隐藏只切它，不切自己 ----
        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(canvasGo.transform, false);

        contentRoot = contentGo.AddComponent<RectTransform>();
        contentRoot.anchorMin = new Vector2(0.5f, 1f);
        contentRoot.anchorMax = new Vector2(0.5f, 1f);
        contentRoot.pivot = new Vector2(0.5f, 1f);
        contentRoot.sizeDelta = new Vector2(BarWidth + 12f, BarHeight + 46f);
        contentRoot.anchoredPosition = new Vector2(0f, -TopMargin);

        // ---- 名字（血条上方） ----
        var nameGo = new GameObject("BossName");
        nameGo.transform.SetParent(contentGo.transform, false);

        nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.fontSize = 28f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = new Color(1f, 0.92f, 0.7f);
        nameText.fontStyle = FontStyles.Bold;

        RectTransform nr = nameGo.GetComponent<RectTransform>();
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0.5f, 1f);
        nr.sizeDelta = new Vector2(0f, 34f);
        nr.anchoredPosition = Vector2.zero;

        // ---- 血条本体 ----
        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(contentGo.transform, false);

        RectTransform barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = Vector2.zero;

        Image border = NewImage("Border", barGo.transform, new Color(0f, 0f, 0f, 0.85f));
        Stretch(border.rectTransform, -3f);

        Image back = NewImage("Back", barGo.transform, new Color(0.22f, 0.08f, 0.10f, 1f));
        Stretch(back.rectTransform, 0f);

        fill = NewImage("Fill", barGo.transform, new Color(0.88f, 0.26f, 0.22f, 1f));
        RectTransform fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 1f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;

        var hpGo = new GameObject("BossHp");
        hpGo.transform.SetParent(barGo.transform, false);

        hpText = hpGo.AddComponent<TextMeshProUGUI>();
        hpText.fontSize = 16f;
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.color = Color.white;
        hpText.fontStyle = FontStyles.Bold;
        Stretch(hpGo.GetComponent<RectTransform>(), 0f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = SolidSprite.White;
        img.color = color;
        img.raycastTarget = false;

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
    /// 显示/隐藏 —— **只切子物体容器，不动自己**。
    ///
    /// 旧版这里是 `rootRect.gameObject.SetActive(v)`，而 rootRect 就是
    /// 这个组件自己所在的 GameObject。Start 里一调 SetVisible(false)，
    /// 整个对象被禁用 → `Update()` 不再执行 → **永远发现不了 BOSS**。
    /// 用"禁用自己"来隐藏，就把"重新显示自己"的能力一起禁掉了。
    /// </summary>
    void SetVisible(bool v)
    {
        if (contentRoot == null || visible == v) return;
        visible = v;
        contentRoot.gameObject.SetActive(v);
    }

    // ============================================================
    //  每帧
    // ============================================================

    void Update()
    {
        if (fill == null || contentRoot == null) return;

        if (currentBoss == null || !IsAlive(currentBoss))
        {
            currentBoss = FindBoss();
            shownRatio = 1f;
        }

        if (currentBoss == null) { SetVisible(false); return; }

        IDamageable dmg = currentBoss as IDamageable;
        if (dmg == null || dmg.MaxHealth <= 0) { SetVisible(false); return; }

        int max = dmg.MaxHealth;
        int cur = Mathf.Clamp(dmg.CurrentHealth, 0, max);

        SetVisible(true);

        float target = (float)cur / max;
        shownRatio = Mathf.MoveTowards(shownRatio, target,
                                       Time.unscaledDeltaTime * Mathf.Max(0.3f, shownRatio));

        RectTransform fr = fill.rectTransform;
        fr.anchorMax = new Vector2(Mathf.Max(0.0001f, shownRatio), 1f);

        // 名字
        string bossName = DisplayNameOf(currentBoss);
        if (nameText != null && nameText.text != bossName) nameText.text = bossName;

        // 数值不用每帧刷（TMP 重排文本有开销）
        if (Time.unscaledTime - lastTextUpdate > 0.1f)
        {
            lastTextUpdate = Time.unscaledTime;
            if (hpText != null) hpText.text = $"{cur} / {max}";
        }
    }

    static string DisplayNameOf(Component c)
    {
        Enemy e = c as Enemy;
        if (e != null) return e.DisplayName;

        if (c is BossShi) return "士";
        if (c is BossGuan) return "官";
        if (c is BossHuang) return "皇";

        return c.GetType().Name;
    }

    /// <summary>
    /// 找场上活着的 BOSS。
    ///
    /// `FindObjectsOfType(type, true)` 的第二个参数 = **包含未激活的对象**。
    /// 有些 BOSS 预制体在场景里是停用的（只在进房时由 Room 启用），
    /// 不传 true 就永远找不到它们 —— 这也是旧版"BOSS 血条不显现"的原因之一。
    /// </summary>
    static Component FindBoss()
    {
        foreach (BossHuang b in Object.FindObjectsOfType<BossHuang>(true))
            if (b.MaxHealth > 0 && b.CurrentHealth > 0) return b;

        foreach (BossGuan b in Object.FindObjectsOfType<BossGuan>(true))
            if (b.MaxHealth > 0 && b.CurrentHealth > 0) return b;

        foreach (BossShi b in Object.FindObjectsOfType<BossShi>(true))
            if (b.MaxHealth > 0 && b.CurrentHealth > 0) return b;

        return null;
    }

    static bool IsAlive(Component c)
    {
        if (c == null) return false;

        IDamageable d = c as IDamageable;
        if (d == null) return false;

        return d.CurrentHealth > 0 && d.MaxHealth > 0;
    }
}
