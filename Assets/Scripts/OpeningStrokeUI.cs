using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 正式开局的「选一支笔画」界面。
///
/// 教程层走到头（打完 BOSS 进传送门）之后弹出：
/// 六种基本笔画各一张卡，玩家选一支作为这一局的起点。
///
///   1-6 / 鼠标点击 → 选定，立刻开始第一层
///
/// 为什么要有这一步：
///   · 教程层为了让玩家认全笔画，会把六种都发一遍；
///     这些成果**不能带进实战**，否则开局就是半套装备。
///   · 所以实战从"只剩一支笔画"重新开始，让玩家自己决定走哪条成长线。
///   · 顺带承担一个教学作用：选之前会把每种笔画长什么样、
///     以它起手第一个字是什么，直接摆出来。
///
/// 只在 Level1 且 GameData.openingStrokeChosen == false 时出现。
/// </summary>
public class OpeningStrokeUI : MonoBehaviour
{
    [Header("外观")]
    public float cardW = 190f;
    // 卡片高度从 190 减到 150 —— **给下面的特征句腾出空间**。
    // 特征句挂在卡片下边缘外面，卡片不矮一点整块面板会超出屏幕。
    public float cardH = 150f;
    public float gap = 18f;

    private GameObject panel;
    private readonly List<Image> cardFrames = new List<Image>();
    private readonly List<Image> cardGlyphs = new List<Image>();
    private readonly List<TextMeshProUGUI> cardLabels = new List<TextMeshProUGUI>();

    /// <summary>每张卡下面的特征句</summary>
    private readonly List<TextMeshProUGUI> cardFeatures = new List<TextMeshProUGUI>();

    /// <summary>副标题（选完之后才填内容）</summary>
    private TextMeshProUGUI subText;
    private readonly Dictionary<string, Sprite> glyphCache = new Dictionary<string, Sprite>();

    private bool open;
    private int hover = -1;

    public static bool IsOpen { get; private set; }

    void Start()
    {
        // 只在正式第一层、且还没选过的时候弹
        if (GameData.inTutorial) return;
        if (GameData.openingStrokeChosen) return;
        if (GameData.currentFloor != 1) return;

        Open();
    }

    void Open()
    {
        open = true;
        IsOpen = true;

        Build();
        panel.SetActive(true);
        Time.timeScale = 0f;

        Sfx.Play(Sfx.Id.Menu, 0.5f);
    }

    void Update()
    {
        if (!open) return;

        for (int i = 0; i < GameData.StarterChoices.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                Choose(i);
                return;
            }
        }

        HandleClick();
    }

    void OnDisable()
    {
        if (open)
        {
            open = false;
            IsOpen = false;
            Time.timeScale = 1f;
        }
    }

    // ============================================================
    //  构建
    // ============================================================

    void Build()
    {
        Canvas canvas = FindObjectOfType<Canvas>();

        int n = GameData.StarterChoices.Length;
        float rowW = n * cardW + (n - 1) * gap;
        float panelW = rowW + 80f;
        // 面板高度 = 标题 + 副标题 + 卡片 + **特征句区** + 底部提示
        float panelH = cardH + 290f;

        panel = new GameObject("OpeningStrokePanel");
        if (canvas != null) panel.transform.SetParent(canvas.transform, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(panelW, panelH);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.05f, 0.12f, 0.97f);

        // ---- 标题 ----
        TextMeshProUGUI title = MakeText(panel.transform, "Title", 30f,
            TextAlignmentOptions.Center, new Color(1f, 0.95f, 0.6f));
        AnchorTop(title.rectTransform, 0f, -18f, panelW - 40f, 44f);
        title.text = GameText.Panel.开局标题();

        TextMeshProUGUI sub = MakeText(panel.transform, "Sub", 20f,
            TextAlignmentOptions.Center, new Color(0.75f, 0.82f, 0.95f));
        AnchorTop(sub.rectTransform, 0f, -62f, panelW - 40f, 30f);

        // **开局时先不显示这行字** —— 玩家要求它「选完后才出现」。
        //
        // 原来是 Build() 里就填上，等于一进来就在说话。
        // 而「哦～很大胆的选择呢」本来就是**对选择本身的回应**，
        // 出现在选择之前逻辑上就不对。
        sub.text = "";
        subText = sub;

        // ---- 六张卡 ----
        float startX = -rowW * 0.5f + cardW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            StrokeType st = GameData.StarterChoices[i];
            string startHanzi = GameData.HanziForSingleStroke(st);
            Color tint = StrokeUtil.ColorOf(st);

            GameObject card = new GameObject("Card" + i);
            card.transform.SetParent(panel.transform, false);

            RectTransform crt = card.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(cardW, cardH);
            crt.anchoredPosition = new Vector2(startX + i * (cardW + gap), 4f);

            Image frame = card.AddComponent<Image>();
            frame.color = new Color(0.2f, 0.24f, 0.34f, 1f);
            frame.raycastTarget = false;
            cardFrames.Add(frame);

            // 起手字（用矩形字形渲染，不吃字体）
            GameObject gl = new GameObject("Glyph");
            gl.transform.SetParent(card.transform, false);
            RectTransform grt = gl.AddComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 0.5f);
            grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(cardW - 70f, cardH - 80f);
            grt.anchoredPosition = new Vector2(0f, 14f);
            Image gi = gl.AddComponent<Image>();
            gi.raycastTarget = false;
            gi.color = tint;

            Sprite sp;
            if (!glyphCache.TryGetValue(startHanzi, out sp))
            {
                sp = HanziRenderer.CreateSpriteFor(startHanzi, 128, 32f);
                glyphCache[startHanzi] = sp;
            }
            gi.sprite = sp;
            gi.enabled = sp != null;
            cardGlyphs.Add(gi);

            // 编号 + 笔画名 + 起手字
            TextMeshProUGUI lbl = MakeText(card.transform, "Label", 20f,
                TextAlignmentOptions.Center, Color.white);
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 10f);
            lrt.sizeDelta = new Vector2(0f, 66f);

            lbl.text = $"<size=26>[{i + 1}] {StrokeUtil.NameOf(st)}</size>\n" +
                       $"<size=70%>起手字「{startHanzi}」</size>";
            cardLabels.Add(lbl);

            // ---- 这个笔画的特点（玩家提供的文案）----
            //
            // 放在卡片**下边缘外面**，而不是挤进卡里 ——
            // 卡里已经有编号 / 笔画名 / 起手字 / 字形四样东西，再塞会挤成一团。
            TextMeshProUGUI feat = MakeText(card.transform, "Feature", 17f,
                TextAlignmentOptions.Top, new Color(0.72f, 0.78f, 0.88f));
            RectTransform frt = feat.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 1f);      // 挂在卡片下边缘，往下长
            frt.anchoredPosition = new Vector2(0f, -6f);
            frt.sizeDelta = new Vector2(-16f, 68f); // 左右各留 8 内边距
            feat.enableWordWrapping = true;
            feat.text = GameText.Panel.笔画特点(st);
            cardFeatures.Add(feat);
        }

        // ---- 底部提示 ----
        TextMeshProUGUI hint = MakeText(panel.transform, "Hint", 20f,
            TextAlignmentOptions.Center, new Color(0.7f, 0.75f, 0.85f));
        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 14f);
        hrt.sizeDelta = new Vector2(panelW - 40f, 32f);
        hint.text = GameText.Hint.开局选笔画();

        // 用 tab 组把 hover 效果做出来
        CreateHoverProbe();
    }

    /// <summary>给卡片加一个透明的 hover 探测器（用 EventTrigger 太重，这里直接读鼠标位置）</summary>
    void CreateHoverProbe()
    {
        // 每帧 Update 里直接算 hover 就行，这里不放东西。
        // 保留这个方法是为了让 Build 的收尾意图清楚。
    }

    void UpdateHover()
    {
        int h = -1;

        for (int i = 0; i < cardFrames.Count; i++)
        {
            RectTransform rt = cardFrames[i].rectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, Input.mousePosition, null, out local)) continue;

            if (rt.rect.Contains(local)) { h = i; break; }
        }

        if (h == hover) return;
        hover = h;

        for (int i = 0; i < cardFrames.Count; i++)
        {
            bool sel = i == hover;
            cardFrames[i].color = sel
                ? new Color(0.32f, 0.42f, 0.58f, 1f)
                : new Color(0.2f, 0.24f, 0.34f, 1f);
        }
    }

    void HandleClick()
    {
        UpdateHover();

        if (!Input.GetMouseButtonDown(0)) return;
        if (hover >= 0) Choose(hover);
    }

    // ============================================================
    //  操作
    // ============================================================

    void Choose(int index)
    {
        // 玩家要求：这句话**选完之后才出现**
        if (subText != null) subText.text = GameText.Panel.开局说明();

        if (index < 0 || index >= GameData.StarterChoices.Length) return;

        StrokeType st = GameData.StarterChoices[index];

        Sfx.Play(Sfx.Id.Combine, 0.6f);

        open = false;
        IsOpen = false;
        if (panel != null) panel.SetActive(false);
        Time.timeScale = 1f;

        GameToast.Show($"起始笔画「{StrokeUtil.NameOf(st)}」—— 开局：{GameData.HanziForSingleStroke(st)}",
            StrokeUtil.ColorOf(st));

        // 真正落地：清掉教程的一切成果，用选中的笔画重新开局。
        // 放在下一帧做，避免和当前这帧的场景初始化抢顺序。
        StartCoroutine(ApplyNextFrame(st));
    }

    System.Collections.IEnumerator ApplyNextFrame(StrokeType st)
    {
        yield return null;

        GameData.BeginRealRun(st);

        // 让汉字系统按新的起手笔画重建
        HanziSystem hanzi = FindObjectOfType<HanziSystem>();
        if (hanzi != null)
        {
            hanzi.ResetToDefaultHanzi();
            hanzi.ApplyOpeningStarter(st);
        }

        StrokeInventory inv = FindObjectOfType<StrokeInventory>();
        if (inv != null) inv.SaveToGameData();

        // 重新加载当前层，让所有系统按新状态初始化
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            GameBalance.SceneForFloor(GameData.currentFloor));
    }

    // ============================================================
    //  工具
    // ============================================================

    static void AnchorTop(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    TextMeshProUGUI MakeText(Transform parent, string name, float size,
                             TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }
}
