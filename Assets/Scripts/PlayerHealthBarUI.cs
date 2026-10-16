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
        // 场景切换后会重建，所以每次都检查
        if (instance != null) return;
        Create();
    }

    public static void Create()
    {
        if (instance != null) return;

        GameObject go = new GameObject("PlayerHealthBar");
        instance = go.AddComponent<PlayerHealthBarUI>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        Build();
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
        }

        transform.SetParent(canvas.transform, false);

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
        if (cached != null) return cached;

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
