using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 开场标题页。
///
/// 为什么需要：作品集的第一个关卡如果直接把玩家丢进战斗，HR 不知道按什么键、
/// 也不知道这个游戏在玩什么，往往几秒就关掉了。标题页承担三件事：
///   1. 一句话讲清核心玩法（捡笔画 → 合成汉字）
///   2. 列出操作键位
///   3. 用真实的字形渲染展示"汉字即武器"这个卖点
///
/// 不新建场景：只在"本轮从第 1 层开始"时第 1 层弹一次覆盖层，
/// 按任意键或点按钮进入。这样不用改场景流程，也不影响中途重开。
/// </summary>
public class TitleScreen : MonoBehaviour
{
    /// <summary>本轮是否已经看过标题页（避免每次回第 1 层都弹）</summary>
    static bool seenThisSession;

    GameObject root;
    CanvasGroup group;
    bool showing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // 只在第 1 层、且本轮还没看过时显示
        if (seenThisSession) return;
        if (GameData.currentFloor != 1) return;

        var go = new GameObject("~TitleScreen");
        DontDestroyOnLoad(go);
        go.AddComponent<TitleScreen>();
    }

    void Start()
    {
        Build();
        showing = true;
        MarkShown();

        // 标题页期间不暂停游戏（Level1 起始房间没有敌人，停不停都一样），
        // 但清空 timeScale 以防上一次退出时残留
        Time.timeScale = 1f;
    }

    void Build()
    {
        Canvas canvas = UIBuild.GetOrCreateCanvas();

        RectTransform full = UIBuild.FullScreen(canvas.transform, "TitleScreen");
        root = full.gameObject;
        UIBuild.Background(full, new Color(0.03f, 0.04f, 0.09f, 0.97f));

        group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;

        // ---- 汉字展示：用真实字形渲染器画出来，直接体现技术卖点 ----
        float y = -120f;
        ShowGlyphRow(full, ref y, "一", "二", "三", "王", "主");
        // 说明：这些字全部由 HanziRenderer 在运行时逐像素生成，与游戏内一致

        // ---- 标题 ----
        var title = UIBuild.Text(full, "Title", "汉 字 弹 幕 地 狱",
            92f, TextAlignmentOptions.Center, new Color(1f, 0.96f, 0.7f));
        UIBuild.AnchorTop(title.rectTransform, -22f, 1400f, 120f);

        var sub = UIBuild.Text(full, "Sub",
            "捡笔画 · 拼汉字 · 越复杂的字火力越强",
            38f, TextAlignmentOptions.Center, new Color(0.75f, 0.88f, 1f));
        UIBuild.AnchorTop(sub.rectTransform, -138f, 1400f, 56f);

        // ---- 操作说明 ----
        string controls =
            "<color=#9fe8a0>WASD</color> 移动      " +
            "<color=#9fe8a0>方向键</color> 射击      " +
            "<color=#9fe8a0>F</color> 拾取 / 开箱\n" +
            "<color=#9fe8a0>1 - 6</color> 使用背包笔画      " +
            "<color=#9fe8a0>Q</color> 自动用第一个可用笔画\n" +
            "<color=#9fe8a0>E</color> 打开背包（暂停，可多选笔画合成）";

        var ctrl = UIBuild.Text(full, "Controls", controls,
            34f, TextAlignmentOptions.Center, Color.white);
        UIBuild.AnchorTop(ctrl.rectTransform, -600f, 1500f, 210f);
        ctrl.lineSpacing = 18f;

        // ---- 开始按钮 ----
        var hint = UIBuild.Text(full, "Hint",
            "按 <color=#ffd97a>任意键</color> 或点击 <color=#ffd97a>开始游戏</color>",
            36f, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.48f));
        UIBuild.AnchorBottom(hint.rectTransform, 210f, 1200f, 56f);

        MakeStartButton(full);

        var tip = UIBuild.Text(full, "Tip",
            "提示：房间清空后会掉落词条与笔画；死亡会清空全部笔画，从第 1 层重开（真肉鸽）",
            26f, TextAlignmentOptions.Center, new Color(0.62f, 0.66f, 0.78f));
        UIBuild.AnchorBottom(tip.rectTransform, 96f, 1500f, 44f);
    }

    /// <summary>横向展示几个汉字，并画出 一 → 二 → 三 → 王 → 主 的箭头关系</summary>
    void ShowGlyphRow(RectTransform parent, ref float y, params string[] glyphs)
    {
        const float size = 130f;
        const float gap = 116f;

        float totalW = glyphs.Length * size + (glyphs.Length - 1) * gap;
        float startX = -totalW * 0.5f + size * 0.5f;

        for (int i = 0; i < glyphs.Length; i++)
        {
            Sprite sp = HanziRenderer.CreateSpriteFor(glyphs[i], 128);
            var img = UIBuild.GlyphImage(parent, $"Glyph_{glyphs[i]}", sp, size,
                new Color(1f, 0.95f, 0.75f));
            img.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            img.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            img.rectTransform.pivot = new Vector2(0.5f, 1f);
            img.rectTransform.anchoredPosition = new Vector2(startX + i * (size + gap), y);
        }

        // 字形之间的箭头
        for (int i = 0; i < glyphs.Length - 1; i++)
        {
            var arrow = UIBuild.Text(parent, $"Arrow{i}", "→",
                52f, TextAlignmentOptions.Center, new Color(0.55f, 0.8f, 0.6f));
            arrow.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            arrow.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            arrow.rectTransform.pivot = new Vector2(0.5f, 1f);
            arrow.rectTransform.anchoredPosition =
                new Vector2(startX + i * (size + gap) + size * 0.5f + gap * 0.5f, y + 40f);
            arrow.rectTransform.sizeDelta = new Vector2(gap, 60f);
        }

        y -= size + 40f;
    }

    void MakeStartButton(RectTransform parent)
    {
        var go = new GameObject("StartButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 290f);
        rt.sizeDelta = new Vector2(420f, 96f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.42f, 0.30f, 0.95f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(Dismiss);

        var label = UIBuild.Text(go.transform, "Label", "开 始 游 戏",
            40f, TextAlignmentOptions.Center, new Color(0.92f, 1f, 0.92f));
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (!showing) return;

        // 防抖：刚显示的短暂时间内忽略输入。
        // 否则玩家死亡重开后可能还按着方向键，标题页会在同一瞬间被关掉。
        if (Time.unscaledTime - shownAt < 0.35f) return;

        // 按任意键开始。排除鼠标滚轮这类无意义的按键
        if (Input.anyKeyDown &&
            !Input.GetMouseButtonDown(1) &&
            !Input.GetMouseButtonDown(2))
        {
            Dismiss();
        }
    }

    float shownAt;

    void MarkShown()
    {
        shownAt = Time.unscaledTime;
    }

    void Dismiss()
    {
        if (!showing) return;
        showing = false;
        seenThisSession = true;

        Sfx.Play(Sfx.Id.Menu, 0.6f);
        Fx.Shake(0.08f);

        if (root != null) Destroy(root);
        Destroy(gameObject);
    }

    /// <summary>重开一轮时允许再次显示标题页</summary>
    public static void ResetForNewRun()
    {
        seenThisSession = false;
    }
}
