using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗中的常驻快捷栏（不暂停游戏）。
/// 屏幕底部显示背包 6 格的色块，玩家不用开背包就能看到手上有什么、按一下就能用。
///   1-6 / 鼠标点击 → 使用对应格（唯一解直接合成，多个候选则开背包让你选）
///   Q             → 自动合成第一个能用的笔画（最省事的操作）
///   E             → 打开完整背包（暂停式，用来挑目标汉字）
/// 只有 6 个格子，避免变成"整理物品栏"的负担。
/// </summary>
public class QuickBarUI : MonoBehaviour
{
    [Header("外观")]
    public float slotSize = 56f;
    public float slotGap = 8f;
    public float bottomOffset = 18f;
    public float fontSize = 20f;

    private StrokeInventory inventory;
    private HanziSystem hanzi;

    private readonly List<Image> frames = new List<Image>();
    private readonly List<Image> blocks = new List<Image>();
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
    private readonly List<Image> glyphPreviews = new List<Image>();
    private readonly Dictionary<string, Sprite> glyphCache = new Dictionary<string, Sprite>();
    private string lastSignature = "";
    private TextMeshProUGUI hint;

    void Start()
    {
        inventory = FindObjectOfType<StrokeInventory>();
        hanzi = FindObjectOfType<HanziSystem>();
        Build();
    }

    void Build()
    {
        if (inventory == null) return;

        Canvas canvas = FindObjectOfType<Canvas>();

        GameObject root = new GameObject("QuickBar (Runtime)");
        if (canvas != null) root.transform.SetParent(canvas.transform, false);

        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottomOffset);

        int n = inventory.maxSlots;
        float w = n * slotSize + (n - 1) * slotGap;
        rt.sizeDelta = new Vector2(w, slotSize + 26f);

        for (int i = 0; i < n; i++)
        {
            GameObject slot = new GameObject($"Q{i + 1}");
            slot.transform.SetParent(root.transform, false);
            RectTransform srt = slot.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(slotSize, slotSize);
            srt.anchoredPosition = new Vector2(i * (slotSize + slotGap), 0f);

            Image frame = slot.AddComponent<Image>();
            frame.color = new Color(0.2f, 0.22f, 0.32f, 0.85f);
            frames.Add(frame);

            GameObject block = new GameObject("Block");
            block.transform.SetParent(slot.transform, false);
            RectTransform brt = block.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(4f, 4f);
            brt.offsetMax = new Vector2(-4f, -4f);
            Image bi = block.AddComponent<Image>();
            bi.raycastTarget = false;
            blocks.Add(bi);

            TextMeshProUGUI lbl = MakeText(slot.transform, "Small", fontSize,
                TextAlignmentOptions.Center, Color.white);
            Stretch(lbl.rectTransform, 2f);
            labels.Add(lbl);

            // 汉字预览：直接用矩形字形渲染成 Sprite，完全不吃字体，绝不会是方框
            GameObject prev = new GameObject("GlyphPreview");
            prev.transform.SetParent(slot.transform, false);
            RectTransform prt = prev.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 6f);
            prt.sizeDelta = new Vector2(slotSize - 22f, slotSize - 22f);
            Image pi = prev.AddComponent<Image>();
            pi.raycastTarget = false;
            pi.enabled = false;
            glyphPreviews.Add(pi);
        }

        hint = MakeText(root.transform, "Hint", 16f,
            TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 0.9f));
        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, -2f);
        hrt.sizeDelta = new Vector2(560f, 22f);
        hint.text = "1-6 用笔画    Q 自动合成    E 打开背包";
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

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    void Update()
    {
        if (inventory == null)
        {
            inventory = FindObjectOfType<StrokeInventory>();
            if (inventory == null) return;
            Build();
        }
        if (hanzi == null) hanzi = FindObjectOfType<HanziSystem>();

        // 背包打开时交给 BackpackUI 处理，避免一次按键触发两套
        if (BackpackUI.IsOpen) return;

        for (int i = 0; i < 6; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                UseSlot(i);
                break;
            }
        }

        if (Input.GetKeyDown(KeyCode.Q)) UseFirstUsable();

        HandleMouseClick();
        Refresh();
    }

    void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i] == null) continue;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    frames[i].rectTransform, Input.mousePosition, null, out local))
            {
                if (frames[i].rectTransform.rect.Contains(local))
                {
                    UseSlot(i);
                    return;
                }
            }
        }
    }

    void UseSlot(int index)
    {
        if (inventory == null || hanzi == null) return;
        if (index < 0 || index >= inventory.strokes.Count) return;

        StrokeType stroke = inventory.strokes[index];

        // 多个成长/拼字方向 → 让玩家做选择（打开背包的选择面板）
        int optionCount = hanzi.GetGrowthCandidates(stroke).Count;
        if (optionCount == 0) optionCount = hanzi.GetDirectCandidates(stroke).Count;

        if (optionCount > 1)
        {
            GameToast.Show($"「{StrokeUtil.NameOf(stroke)}」有多个去向，按 E 打开背包选择", new Color(1f, 0.9f, 0.5f));
            return;
        }

        if (!inventory.TryCombineFromBackpack(index))
        {
            // 失败要给出明确原因，而不是静默无反应
            if (!hanzi.StrokeIsUseful(stroke))
                GameToast.Show($"「{StrokeUtil.NameOf(stroke)}」现在用不上，先留着", new Color(1f, 0.6f, 0.5f));
        }
    }

    // Q：自动用掉第一个能用的笔画（最省心的操作）
    void UseFirstUsable()
    {
        if (inventory == null || hanzi == null) return;

        for (int i = 0; i < inventory.strokes.Count; i++)
        {
            StrokeType st = inventory.strokes[i];
            if (!hanzi.StrokeIsUseful(st)) continue;   // 用不上的跳过

            if (inventory.TryCombineFromBackpack(i))
            {
                GameToast.Show($"已用「{StrokeUtil.NameOf(st)}」", StrokeUtil.ColorOf(st));
                return;
            }
        }

        GameToast.Show("没有能立刻使用的笔画", new Color(0.8f, 0.8f, 0.8f));
    }

    void Refresh()
    {
        // 字数/当前汉字没变时跳过重算（否则每帧都会跑一堆匹配 + 取字形）
        string signature = (hanzi != null ? hanzi.currentHanziName : "") + "#" + inventory.strokes.Count;
        for (int i = 0; i < inventory.strokes.Count; i++)
            signature += "," + (int)inventory.strokes[i];

        bool dirty = signature != lastSignature;
        if (!dirty) return;
        lastSignature = signature;

        for (int i = 0; i < frames.Count; i++)
        {
            bool has = i < inventory.strokes.Count;

            if (blocks[i] != null)
            {
                blocks[i].enabled = has;
                if (has) blocks[i].color = StrokeUtil.ColorOf(inventory.strokes[i]);
            }

            // 优先看能不能让当前汉字"长大"，其次看能不能直接拼出别的字
            List<HanziDef> growth = has && hanzi != null
                ? hanzi.GetGrowthCandidates(inventory.strokes[i])
                : new List<HanziDef>();
            List<HanziDef> exact = (has && hanzi != null && growth.Count == 0)
                ? hanzi.GetDirectCandidates(inventory.strokes[i])
                : new List<HanziDef>();

            List<HanziDef> cands = growth.Count > 0 ? growth : exact;
            bool usable = cands.Count > 0;
            bool ambiguous = cands.Count > 1;

            // 汉字预览（矩形字形，不吃字体）
            if (glyphPreviews[i] != null)
            {
                string preview = usable ? cands[0].hanziName : null;
                if (string.IsNullOrEmpty(preview))
                {
                    glyphPreviews[i].enabled = false;
                }
                else
                {
                    Sprite sp;
                    if (!glyphCache.TryGetValue(preview, out sp))
                    {
                        sp = HanziRenderer.CreateSpriteFor(preview, 64, 32f);
                        glyphCache[preview] = sp;
                    }
                    glyphPreviews[i].sprite = sp;
                    glyphPreviews[i].enabled = sp != null;
                    // 会变成哪个字，用对应笔画的颜色显示
                    glyphPreviews[i].color = StrokeUtil.ColorOf(inventory.strokes[i]);
                }
            }

            if (labels[i] != null)
            {
                if (!has) labels[i].text = "";
                else if (!usable) labels[i].text = $"{i + 1}·闲置";
                else if (ambiguous) labels[i].text = $"{i + 1}·选";
                else labels[i].text = $"{i + 1}";
            }

            if (frames[i] != null)
            {
                frames[i].color = usable
                    ? new Color(0.9f, 0.8f, 0.3f, 0.9f)
                    : new Color(0.2f, 0.22f, 0.32f, 0.7f);
            }
        }
    }
}
