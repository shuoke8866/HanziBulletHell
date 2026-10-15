using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 笔顺歧义时的选字界面。
///
/// 什么时候弹：手上恰好能拼出**好几个笔顺完全相同的字**时。
/// 汉字里本来就有这种字 —— 十/丁、人/八、工/士/土 ——
/// 光靠笔顺没法决定给哪个。
///
/// 原来按名字的 Unicode 顺序取第一个，结果 十、人、士、土 这四个字
/// 玩家**永远拿不到**。现在弹出来让玩家点一下，选过的会被记住，
/// 以后再凑齐同一组笔画就直接沿用，不再打扰。
///
///   1-3 / 鼠标点击 → 选定
///   6 秒不选       → 自动选第一个（不打断战斗节奏）
///
/// 注意**不暂停游戏** —— 选字是一瞬间的事，暂停反而打断手感。
/// </summary>
public class HanziPickerUI : MonoBehaviour
{
    [Header("外观")]
    public float cardW = 132f;
    public float cardH = 150f;
    public float gap = 14f;

    [Header("超时")]
    /// <summary>多少秒不选就自动选第一个（0 = 不超时，一直等）</summary>
    public float autoPickAfter = 6f;

    private GameObject panel;
    private HanziSystem owner;
    private readonly List<HanziDef> options = new List<HanziDef>();
    private readonly List<Image> cardFrames = new List<Image>();
    private readonly List<Image> cardGlyphs = new List<Image>();
    private readonly List<TextMeshProUGUI> cardLabels = new List<TextMeshProUGUI>();
    private readonly Dictionary<string, Sprite> glyphCache = new Dictionary<string, Sprite>();

    private bool open;
    private float openedAt;
    private int hover = -1;

    public static bool IsOpen { get; private set; }

    void OnEnable()
    {
        HanziSystem.ChoiceRequested += OnChoiceRequested;
    }

    void OnDisable()
    {
        HanziSystem.ChoiceRequested -= OnChoiceRequested;

        if (open)
        {
            open = false;
            IsOpen = false;
            HanziSystem.AwaitingChoice = false;
        }
    }

    void OnChoiceRequested(List<HanziDef> candidates)
    {
        if (candidates == null || candidates.Count < 2) return;

        owner = FindObjectOfType<HanziSystem>();

        options.Clear();
        for (int i = 0; i < candidates.Count && i < 3; i++)
        {
            if (candidates[i] != null) options.Add(candidates[i]);
        }
        if (options.Count < 2) return;

        open = true;
        IsOpen = true;
        openedAt = Time.unscaledTime;

        Build();
        panel.SetActive(true);

        Sfx.Play(Sfx.Id.Menu, 0.4f);
    }

    void Update()
    {
        if (!open) return;

        for (int i = 0; i < options.Count; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                Pick(i);
                return;
            }
        }

        UpdateHover();

        if (Input.GetMouseButtonDown(0) && hover >= 0)
        {
            Pick(hover);
            return;
        }

        if (autoPickAfter > 0f && Time.unscaledTime - openedAt > autoPickAfter)
        {
            GameToast.Show("超时 —— 自动选了第一个", new Color(0.85f, 0.85f, 0.85f));
            Pick(0);
        }
    }

    // ============================================================
    //  构建
    // ============================================================

    void Build()
    {
        if (panel == null)
        {
            Canvas canvas = FindObjectOfType<Canvas>();

            panel = new GameObject("HanziPickerPanel");
            if (canvas != null) panel.transform.SetParent(canvas.transform, false);

            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.12f, 0.95f);

            TextMeshProUGUI title = MakeText(panel.transform, "Title", 24f,
                TextAlignmentOptions.Center, new Color(1f, 0.95f, 0.6f));
            AnchorTop(title.rectTransform, 0f, -12f, 520f, 34f);
            title.text = "这几个字笔顺一样，你要哪个？";
        }

        // 按当前候选数重排
        int n = options.Count;
        float rowW = n * cardW + (n - 1) * gap;
        float panelW = Mathf.Max(520f, rowW + 60f);
        float panelH = cardH + 118f;

        RectTransform prt2 = panel.GetComponent<RectTransform>();
        prt2.sizeDelta = new Vector2(panelW, panelH);

        // 清掉上次的卡片
        for (int i = panel.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = panel.transform.GetChild(i);
            if (c.name.StartsWith("Card")) Destroy(c.gameObject);
        }
        cardFrames.Clear();
        cardGlyphs.Clear();
        cardLabels.Clear();

        float startX = -rowW * 0.5f + cardW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            HanziDef def = options[i];
            Color tint = StrokeUtil.ColorOf(
                def.strokes != null && def.strokes.Count > 0 ? def.strokes[0] : StrokeType.Heng);

            GameObject card = new GameObject("Card" + i);
            card.transform.SetParent(panel.transform, false);

            RectTransform crt = card.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(cardW, cardH);
            crt.anchoredPosition = new Vector2(startX + i * (cardW + gap), 2f);

            Image frame = card.AddComponent<Image>();
            frame.color = new Color(0.2f, 0.24f, 0.34f, 1f);
            frame.raycastTarget = false;
            cardFrames.Add(frame);

            GameObject gl = new GameObject("Glyph");
            gl.transform.SetParent(card.transform, false);

            RectTransform grt = gl.AddComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 0.5f);
            grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(cardW - 46f, cardH - 62f);
            grt.anchoredPosition = new Vector2(0f, 14f);

            Image gi = gl.AddComponent<Image>();
            gi.raycastTarget = false;
            gi.color = tint;

            Sprite sp;
            if (!glyphCache.TryGetValue(def.hanziName, out sp))
            {
                sp = HanziRenderer.CreateSpriteFor(def.hanziName, 128, 32f);
                glyphCache[def.hanziName] = sp;
            }
            gi.sprite = sp;
            gi.enabled = sp != null;
            cardGlyphs.Add(gi);

            TextMeshProUGUI lbl = MakeText(card.transform, "Label", 19f,
                TextAlignmentOptions.Center, Color.white);
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 8f);
            lrt.sizeDelta = new Vector2(0f, 46f);

            lbl.text = $"<size=24>[{i + 1}] {def.hanziName}</size>\n" +
                       $"<size=70%>{def.strokes.Count} 画</size>";
            cardLabels.Add(lbl);
        }

        TextMeshProUGUI hint = panel.transform.Find("Hint") != null
            ? panel.transform.Find("Hint").GetComponent<TextMeshProUGUI>()
            : MakeText(panel.transform, "Hint", 18f,
                TextAlignmentOptions.Center, new Color(0.7f, 0.75f, 0.85f));

        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 8f);
        hrt.sizeDelta = new Vector2(panelW - 30f, 26f);
        hint.text = "按 1-3 或点一下卡片。选过一次就会记住，以后自动沿用。";
    }

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

    // ============================================================
    //  操作
    // ============================================================

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
            cardFrames[i].color = i == hover
                ? new Color(0.32f, 0.42f, 0.58f, 1f)
                : new Color(0.2f, 0.24f, 0.34f, 1f);
        }
    }

    void Pick(int index)
    {
        if (index < 0 || index >= options.Count) return;

        HanziDef chosen = options[index];

        open = false;
        IsOpen = false;
        if (panel != null) panel.SetActive(false);

        Sfx.Play(Sfx.Id.Combine, 0.5f);
        GameToast.Show($"选定「{chosen.hanziName}」", new Color(0.7f, 1f, 0.8f));

        if (owner != null)
            owner.ResolveChoice(chosen);
        else
            HanziSystem.AwaitingChoice = false;
    }
}
