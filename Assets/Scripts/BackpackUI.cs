using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包（按 E 打开）。**只做一件事：选字 + 翻页。**
///
/// 这是重写版。旧版把"笔画格 + 勾选笔画 + 回车合成 + 歧义选择面板 + 词条 + 详情"
/// 全塞在一个面板里，结果是：
///   - 1280×720 下上下堆叠超出屏幕 400 多像素，根本显示不全
///   - 数字键既要选笔画又要选字，语义混乱
///   - 玩家分不清"哪些格子能点、点了会发生什么"
///
/// 新设计：
///   一屏 9 个字（3×3），每格**直接画出那个汉字**并标出编号 [1]-[9]
///   数字键 1-9    → 切换到对应汉字
///   A / D（或 ←/→）→ 翻页
///   鼠标点击格子  → 切换到该汉字
///   E / Esc       → 关闭
///
/// 候选来源：HanziSystem.GetAllFormable()（仓库里能拼出的所有字，按笔画数降序）。
/// 因为笔画池永久保留、切换无损，玩家可以随便翻、随便点、随时切回来。
///
/// 注意：**底部的常驻快捷换字栏是 QuickBarUI，不是这个类**。
/// 那个不暂停游戏、显示前 9 个候选；这个按 E 打开、可以翻页看全部。
/// </summary>
public class BackpackUI : MonoBehaviour
{
    [Header("玩家引用（可留空，会自动查找）")]
    public StrokeInventory inventory;
    public HanziSystem hanzi;

    [Header("布局")]
    public float panelWidth = 780f;
    public float panelHeight = 640f;
    public float cardSize = 194f;
    public float cardGap = 14f;

    /// <summary>每页候选数（3×3，对应数字键 1~9）</summary>
    public const int PerPage = 9;

    // ---------------- 运行时 UI ----------------
    private GameObject panel;
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI pageText;
    private TextMeshProUGUI hintText;
    private readonly List<Image> cardFrames = new List<Image>();
    private readonly List<Image> cardGlyphs = new List<Image>();
    private readonly List<TextMeshProUGUI> cardLabels = new List<TextMeshProUGUI>();

    // ---------------- 状态 ----------------
    private bool open;
    private int page;
    private readonly List<HanziOption> pageOptions = new List<HanziOption>();
    private readonly Dictionary<string, Sprite> glyphCache = new Dictionary<string, Sprite>();

    /// <summary>背包是否打开（其他系统用它避让按键）</summary>
    public static bool IsOpen { get; private set; }

    // ============================================================
    //  生命周期
    // ============================================================

    void Awake()
    {
        if (inventory == null) inventory = GetComponent<StrokeInventory>();
        if (hanzi == null) hanzi = GetComponent<HanziSystem>();
    }

    void Update()
    {
        // 开关（Esc 也能关）
        if (Input.GetKeyDown(KeyCode.E) ||
            (open && Input.GetKeyDown(KeyCode.Escape)))
        {
            Toggle();
            return;
        }

        if (!open) return;

        // 数字键 1-9 → 切换到本页第 N 个字
        for (int i = 0; i < PerPage; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SwitchTo(i);
                return;
            }
        }

        // A / D 翻页（左右方向键同样有效，手不用挪太远）
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            ChangePage(-1);
            return;
        }
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            ChangePage(1);
            return;
        }

        HandleClick();
    }

    void Toggle()
    {
        open = !open;
        IsOpen = open;

        if (open)
        {
            if (panel == null) Build();
            page = 0;
            panel.SetActive(true);
            Time.timeScale = 0f;          // 暂停游戏
            Sfx.Play(Sfx.Id.Menu, 0.5f);
            Refresh();
        }
        else
        {
            if (panel != null) panel.SetActive(false);
            Time.timeScale = 1f;          // 关闭必须恢复，否则整个游戏像卡死
            Sfx.Play(Sfx.Id.Menu, 0.4f);
        }
    }

    /// <summary>
    /// 强制关闭（游戏结束时 GameOverUI 会调用）。
    /// 注意这里**不恢复 timeScale** —— 结算界面自己会把 timeScale 设为 0，
    /// 如果这里先恢复成 1，结算界面就冻不住了。
    /// </summary>
    public void Close()
    {
        if (!open) return;

        open = false;
        IsOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    void OnDisable()
    {
        // 兜底：场景卸载/组件禁用时恢复 timeScale。
        // 否则切层或死亡重开后新场景会以 timeScale=0 开始，游戏看起来完全卡住。
        if (open)
        {
            open = false;
            IsOpen = false;
            Time.timeScale = 1f;
        }
    }

    // ============================================================
    //  构建 UI
    // ============================================================

    void Build()
    {
        Canvas canvas = FindObjectOfType<Canvas>();

        panel = new GameObject("BackpackPanel (Runtime)");
        if (canvas != null) panel.transform.SetParent(canvas.transform, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.05f, 0.12f, 0.97f);

        // ---- 顶部标题 ----
        headerText = MakeText(panel.transform, "Header", 26f,
            TextAlignmentOptions.Center, new Color(1f, 0.95f, 0.6f));
        AnchorTop(headerText.rectTransform, 0f, -14f, panelWidth - 40f, 40f);

        // ---- 3×3 汉字格 ----
        float gridW = 3f * cardSize + 2f * cardGap;
        float gridH = 3f * cardSize + 2f * cardGap;
        const float gridTop = 62f;

        for (int i = 0; i < PerPage; i++)
        {
            int col = i % 3;
            int row = i / 3;

            GameObject card = new GameObject($"Card{i + 1}");
            card.transform.SetParent(panel.transform, false);

            RectTransform crt = card.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(cardSize, cardSize);
            crt.anchoredPosition = new Vector2(
                -gridW * 0.5f + cardSize * 0.5f + col * (cardSize + cardGap),
                -(gridTop + row * (cardSize + cardGap)));

            Image frame = card.AddComponent<Image>();
            frame.color = new Color(0.22f, 0.26f, 0.36f, 1f);
            frame.raycastTarget = false;
            cardFrames.Add(frame);

            // 汉字图形：直接渲染矩形字形（HanziRenderer），不吃字体，绝不会是方框
            GameObject gl = new GameObject("Glyph");
            gl.transform.SetParent(card.transform, false);
            RectTransform grt = gl.AddComponent<RectTransform>();
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(30f, 16f);     // 左侧留出编号
            grt.offsetMax = new Vector2(-12f, -12f);
            Image gi = gl.AddComponent<Image>();
            gi.raycastTarget = false;
            gi.enabled = false;
            cardGlyphs.Add(gi);

            // 编号 + 笔画数，压在左下角
            TextMeshProUGUI lbl = MakeText(card.transform, "Label", 19f,
                TextAlignmentOptions.BottomLeft, new Color(0.85f, 0.9f, 1f));
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(9f, 7f);
            lrt.offsetMax = new Vector2(-9f, -9f);
            cardLabels.Add(lbl);
        }

        // ---- 页码 ----
        pageText = MakeText(panel.transform, "Page", 21f,
            TextAlignmentOptions.Center, new Color(0.75f, 0.85f, 1f));
        AnchorTop(pageText.rectTransform, 0f, -(gridTop + gridH + 10f), panelWidth - 60f, 30f);

        // ---- 底部提示 ----
        hintText = MakeText(panel.transform, "Hint", 19f,
            TextAlignmentOptions.Center, new Color(0.7f, 0.75f, 0.85f));
        RectTransform hrt = hintText.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 12f);
        hrt.sizeDelta = new Vector2(panelWidth - 40f, 26f);
        hintText.text = GameText.Hint.背包();

        panel.SetActive(false);
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
    //  刷新
    // ============================================================

    void Refresh()
    {
        if (hanzi == null) return;

        List<HanziOption> all = hanzi.GetAllFormable();

        int pageCount = Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)PerPage));
        page = Mathf.Clamp(page, 0, pageCount - 1);

        pageOptions.Clear();
        int start = page * PerPage;
        for (int i = start; i < all.Count && pageOptions.Count < PerPage; i++)
            pageOptions.Add(all[i]);

        // ---- 标题：当前汉字 + 仓库现状 ----
        if (headerText != null)
        {
            // 显示**收集到的全部笔画**，不是"扣掉当前字之后的空闲笔画"。
            //
            // 原来用 AvailableStrokes()，于是当前「木」占掉横竖撇捺后，
            // 标题显示"可用笔画 空、可换 0 个字" ——
            // 明明手上有四笔却什么都换不了，看着就像坏了。
            // 当前字占的那几笔本来就在手上，换个字照样能用。
            headerText.text =
                GameText.Panel.背包当前汉字(hanzi.currentHanziName) + "　　" +
                GameText.Panel.背包可用笔画(DescribeStrokes(hanzi.SynthesisStrokes())) + "　　" +
                GameText.Panel.背包可换数量(all.Count);
        }

        // ---- 九宫格 ----
        for (int i = 0; i < PerPage; i++)
        {
            Image frame = cardFrames[i];
            Image glyph = cardGlyphs[i];
            TextMeshProUGUI lbl = cardLabels[i];

            if (i >= pageOptions.Count)
            {
                // 空格子：压暗，让人一眼看出"这里没东西"
                if (frame != null) frame.color = new Color(0.11f, 0.13f, 0.19f, 0.7f);
                if (glyph != null) glyph.enabled = false;
                if (lbl != null) lbl.text = "";
                continue;
            }

            HanziOption opt = pageOptions[i];
            string name = opt.def.hanziName;

            bool isCurrent = name == hanzi.currentHanziName;
            bool wasFormed = hanzi.FormedHanziNames.Contains(name);

            if (frame != null)
            {
                frame.color = isCurrent
                    ? new Color(0.28f, 0.5f, 0.32f, 1f)      // 当前字：绿底
                    : new Color(0.22f, 0.26f, 0.36f, 1f);
            }

            if (glyph != null)
            {
                Sprite sp;
                if (!glyphCache.TryGetValue(name, out sp))
                {
                    sp = HanziRenderer.CreateSpriteFor(name, 128, 32f);
                    glyphCache[name] = sp;
                }
                glyph.sprite = sp;
                glyph.enabled = sp != null;
                glyph.color = isCurrent ? new Color(0.65f, 1f, 0.72f) : Color.white;
            }

            if (lbl != null)
            {
                string mark = isCurrent ? "●" : (wasFormed ? "○" : " ");
                lbl.text = $"{mark}[{i + 1}]  {opt.totalStrokes}画";
            }
        }

        // ---- 页码 ----
        if (pageText != null)
        {
            pageText.text = all.Count == 0
                ? GameText.Panel.背包空仓()
                : GameText.Panel.背包页码(page + 1, pageCount, all.Count);
        }
    }

    /// <summary>把笔画列表写成"横×3 竖×2"这样的可读文本</summary>
    static string DescribeStrokes(List<StrokeType> list)
    {
        if (list == null || list.Count == 0) return "空";

        string s = "";
        var seen = new List<StrokeType>();

        foreach (StrokeType t in list)
        {
            if (seen.Contains(t)) continue;
            seen.Add(t);

            int n = 0;
            foreach (StrokeType x in list) if (x == t) n++;

            if (s.Length > 0) s += " ";
            s += $"{StrokeUtil.NameOf(t)}×{n}";
        }

        return s;
    }

    // ============================================================
    //  操作
    // ============================================================

    void ChangePage(int dir)
    {
        if (hanzi == null) return;

        int total = hanzi.GetAllFormable().Count;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)PerPage));

        int before = page;
        page = Mathf.Clamp(page + dir, 0, pageCount - 1);

        if (page != before)
        {
            Sfx.Play(Sfx.Id.Menu, 0.4f);
            Refresh();
        }
        else
        {
            Sfx.Play(Sfx.Id.Error, 0.25f);
            GameToast.Show(dir > 0 ? "已经是最后一页" : "已经是第一页",
                new Color(0.85f, 0.85f, 0.85f));
        }
    }

    void SwitchTo(int index)
    {
        if (hanzi == null) return;

        if (index < 0 || index >= pageOptions.Count)
        {
            Sfx.Play(Sfx.Id.Error, 0.25f);
            GameToast.Show(GameText.Toast.空号位(index + 1, pageOptions.Count),
                new Color(0.85f, 0.85f, 0.85f));
            return;
        }

        HanziDef target = pageOptions[index].def;
        if (target == null) return;

        if (target.hanziName == hanzi.currentHanziName)
        {
            Sfx.Play(Sfx.Id.Error, 0.25f);
            GameToast.Show(GameText.Toast.已是该字(target.hanziName), new Color(0.85f, 0.85f, 0.85f));
            return;
        }

        if (hanzi.SwitchTo(target))
        {
            Sfx.Play(Sfx.Id.Combine, 0.6f);
            GameToast.Show(GameText.Toast.切换汉字(target.hanziName), new Color(0.7f, 1f, 0.8f));
            Refresh();
        }
        else
        {
            Sfx.Play(Sfx.Id.Error, 0.3f);
            GameToast.Show(GameText.Toast.切换失败(target.hanziName),
                new Color(1f, 0.6f, 0.5f));
        }
    }

    void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        for (int i = 0; i < cardFrames.Count; i++)
        {
            if (cardFrames[i] == null) continue;

            RectTransform rt = cardFrames[i].rectTransform;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, Input.mousePosition, null, out local))
            {
                if (!rt.rect.Contains(local)) continue;
                SwitchTo(i);
                return;
            }
        }
    }
}
