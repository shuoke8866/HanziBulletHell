using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// BOSS 血条 —— 固定在屏幕顶部中央的一条长血条 + 名字。
///
/// ## 为什么和 BOSS 头顶血条分开做
///
/// BOSS 体型大、位置在场上乱跑，头顶血条会跟着晃，读起来很累。
/// 惯例做法是**固定在屏幕顶部**，玩家不用追着看。
///
/// ## 自动发现 BOSS
///
/// 不要求场景里配置 —— 每帧检查场上有没有活着的 BOSS（实现了 IDamageable
/// 且 MaxHealth > 0 的敌人脚本），有就显示、没有就隐藏。
/// 这样加新 BOSS 不用回来改这个文件。
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    const float BarWidth = 620f;
    const float BarHeight = 22f;
    const float TopMargin = 26f;

    static BossHealthBarUI instance;

    Image fill;
    Image back;
    TextMeshProUGUI nameText;
    TextMeshProUGUI hpText;
    RectTransform rootRect;
    /// <summary>真正要隐藏的那一层（所有可见的子物体都挂在这下面）</summary>
    RectTransform contentRoot;

    float shownRatio = 1f;
    Component currentBoss;      // 缓存当前正在显示血条的 BOSS
    float lastTextUpdate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // 和 PlayerHealthBarUI 一样：血条挂在场景里的 Canvas 下，
        // 换场景时会被一起销毁。被销毁的对象是**假 null**，`!= null` 仍返回 true，
        // 于是新场景里以为"已经有了"就跳过创建 → 血条永远不再出现。
        // 必须用 ReferenceEquals 判真 null。
        if (!ReferenceEquals(instance, null)) return;
        Create();
    }

    public static void Create()
    {
        if (!ReferenceEquals(instance, null)) return;

        GameObject go = new GameObject("BossHealthBar");
        instance = go.AddComponent<BossHealthBarUI>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        Build();
        SetVisible(false);
    }

    /// <summary>当前挂靠的 Canvas（换场景后旧的会失效，要重新找）</summary>
    Canvas hostCanvas;

    void LateUpdate()
    {
        // 血条本体是 DontDestroyOnLoad，但必须挂在某个 Canvas 下才显示得出来。
        // 换场景时旧的 Canvas 被销毁 → 重新找一个挂过去。
        if (hostCanvas == null || hostCanvas.transform == null)
        {
            Canvas found = FindObjectOfType<Canvas>();
            if (found != null)
            {
                hostCanvas = found;
                transform.SetParent(hostCanvas.transform, false);
            }
            else return;
        }
        else if (transform.parent != hostCanvas.transform)
        {
            transform.SetParent(hostCanvas.transform, false);
        }
    }

    void Build()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var cgo = new GameObject("BossBarCanvas");
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
        }

        hostCanvas = canvas;
        transform.SetParent(canvas.transform, false);
        rootRect = gameObject.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.sizeDelta = new Vector2(BarWidth + 12f, BarHeight + 46f);
        rootRect.anchoredPosition = new Vector2(0f, -TopMargin);

        // 「内容」容器 —— 显示/隐藏只切这一层，不动自己
        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(transform, false);
        contentRoot = contentGo.AddComponent<RectTransform>();
        contentRoot.anchorMin = Vector2.zero;
        contentRoot.anchorMax = Vector2.one;
        contentRoot.offsetMin = Vector2.zero;
        contentRoot.offsetMax = Vector2.zero;

        // 名字（血条上方）
        var nameGo = new GameObject("BossName");
        nameGo.transform.SetParent(contentGo.transform, false);
        nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.fontSize = 26f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = new Color(1f, 0.92f, 0.7f);
        nameText.fontStyle = FontStyles.Bold;
        var nr = nameGo.GetComponent<RectTransform>();
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0.5f, 1f);
        nr.sizeDelta = new Vector2(0f, 32f);
        nr.anchoredPosition = new Vector2(0f, 0f);

        // 血条容器
        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(contentGo.transform, false);
        var barRect = barGo.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        barRect.anchoredPosition = new Vector2(0f, 0f);

        var border = NewImage("Border", barGo.transform, new Color(0f, 0f, 0f, 0.8f));
        Stretch(border.rectTransform, -3f);

        back = NewImage("Back", barGo.transform, new Color(0.22f, 0.08f, 0.10f, 1f));
        Stretch(back.rectTransform, 0f);

        fill = NewImage("Fill", barGo.transform, new Color(0.9f, 0.25f, 0.22f));
        var fr = fill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 1f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;

        // 血量数字（叠在血条中间）
        var hpGo = new GameObject("BossHp");
        hpGo.transform.SetParent(barGo.transform, false);
        hpText = hpGo.AddComponent<TextMeshProUGUI>();
        hpText.fontSize = 15f;
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.color = new Color(1f, 1f, 1f, 0.92f);
        Stretch(hpGo.GetComponent<RectTransform>(), 0f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
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
    /// 显示/隐藏血条 —— **只隐藏子物体，不要禁用自己的 GameObject**。
    ///
    /// 踩过的坑（玩家反馈"BOSS 血条看不到"）：
    /// 原来这里写的是 `rootRect.gameObject.SetActive(v)`，
    /// 而 rootRect 就是**这个组件自己所在的 GameObject**。
    /// Start() 里调 SetVisible(false) → 整个对象被禁用 →
    /// **Update() 也就不再执行** → 永远发现不了 BOSS，血条永远不出现。
    ///
    /// 这是自锁：用"禁用自己"来隐藏，就把"重新显示自己"的能力一起禁掉了。
    ///
    /// 正确做法：自己始终保持启用（好让 Update 一直跑），只切换子物体。
    /// </summary>
    void SetVisible(bool v)
    {
        if (contentRoot == null) return;
        if (contentRoot.gameObject.activeSelf != v)
            contentRoot.gameObject.SetActive(v);
    }

    void Update()
    {
        if (fill == null) return;

        // 找活着的 BOSS —— 静态缓存一份，找不到时才重新扫
        if (currentBoss == null || !IsAlive(currentBoss))
        {
            currentBoss = FindBoss();
            shownRatio = 1f;
        }

        if (currentBoss == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        IDamageable dmg = currentBoss as IDamageable;
        int max = dmg.MaxHealth;
        int cur = Mathf.Clamp(dmg.CurrentHealth, 0, max);
        if (max <= 0) { SetVisible(false); return; }

        float target = (float)cur / max;
        shownRatio = Mathf.MoveTowards(shownRatio, target,
                                       Time.unscaledDeltaTime * Mathf.Max(0.25f, shownRatio));

        var fr = fill.rectTransform;
        fr.anchorMax = new Vector2(Mathf.Max(0.0001f, shownRatio), 1f);

        // 名字
        Enemy enemy = currentBoss as Enemy;
        string bossName = enemy != null ? enemy.DisplayName : currentBoss.GetType().Name;

        if (nameText != null && nameText.text != bossName)
            nameText.text = bossName;

        // 数字不需要每帧刷新（TextMeshPro 重排文本有开销）
        if (Time.unscaledTime - lastTextUpdate > 0.1f)
        {
            lastTextUpdate = Time.unscaledTime;
            if (hpText != null) hpText.text = $"{cur} / {max}";
        }
    }

    /// <summary>场上还活着的 BOSS（实现了 IDamageable 且血量有效的）</summary>
    static Component FindBoss()
    {
        // 三个 BOSS 脚本 + Enemy（精英也算"BOSS"待遇的话可以放开）
        foreach (BossHuang b in Object.FindObjectsOfType<BossHuang>())
            if (b.MaxHealth > 0 && b.CurrentHealth > 0) return b;

        foreach (BossGuan b in Object.FindObjectsOfType<BossGuan>())
            if (b.MaxHealth > 0 && b.CurrentHealth > 0) return b;

        foreach (BossShi b in Object.FindObjectsOfType<BossShi>())
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
