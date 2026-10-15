using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗中的常驻快捷换字栏（不暂停游戏）。
///
/// 屏幕底部显示 9 个按钮，每个按钮上**直接画出按下去会变成的那个汉字**
/// （矩形字形渲染成 Sprite，不吃字体，绝不会是方框）。
///   1-9 / 鼠标点击 → 直接切换到该汉字
///   E               → 打开完整背包（暂停式，可以翻页看全部候选字）
///   Q               → 切到最优解（最复杂的那个）
///
/// 候选来源是 HanziSystem.GetAllFormable()：仓库里能拼出的**所有**汉字，
/// 按笔画数从多到少排。所以第 1 格永远是"目前能做出的最复杂的字"。
/// 因为笔画池永久保留、切换无损，玩家可以随便点、随便换回来。
/// </summary>
public class QuickBarUI : MonoBehaviour
{
    [Header("外观")]
    public float slotSize = 62f;
    public float slotGap = 8f;
    public float bottomOffset = 18f;
    public float fontSize = 18f;

    /// <summary>快捷栏格子数（= 每页候选数，也是数字键 1~N 的范围）</summary>
    public const int SlotCount = 9;

    private StrokeInventory inventory;
    private HanziSystem hanzi;

    private readonly List<Image> frames = new List<Image>();
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
    private readonly List<Image> glyphPreviews = new List<Image>();
    private readonly Dictionary<string, Sprite> glyphCache = new Dictionary<string, Sprite>();

    /// <summary>当前显示的候选（每格一个，null 表示空格）</summary>
    private readonly List<HanziOption> shown = new List<HanziOption>();

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
        Canvas canvas = FindObjectOfType<Canvas>();

        GameObject root = new GameObject("QuickBar (Runtime)");
        if (canvas != null) root.transform.SetParent(canvas.transform, false);

        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottomOffset);

        float w = SlotCount * slotSize + (SlotCount - 1) * slotGap;
        rt.sizeDelta = new Vector2(w, slotSize + 26f);

        for (int i = 0; i < SlotCount; i++)
        {
            GameObject slot = new GameObject($"Hanzi{i + 1}");
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

            // 汉字预览占满格子（去掉边距），这是这一格的主角
            GameObject prev = new GameObject("GlyphPreview");
            prev.transform.SetParent(slot.transform, false);
            RectTransform prt = prev.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(4f, 14f);      // 底部留出编号的位置
            prt.offsetMax = new Vector2(-4f, -4f);
            Image pi = prev.AddComponent<Image>();
            pi.raycastTarget = false;
            pi.enabled = false;
            glyphPreviews.Add(pi);

            // 编号（1-9）压在小格底部
            TextMeshProUGUI lbl = MakeText(slot.transform, "Num", fontSize,
                TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.9f));
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 2f);
            lrt.sizeDelta = new Vector2(0f, 16f);
            labels.Add(lbl);
        }

        hint = MakeText(root.transform, "Hint", 15f,
            TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 0.9f));
        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, -2f);
        hrt.sizeDelta = new Vector2(720f, 22f);
        hint.text = "1-9 换字    Q 最强    E 背包（可翻页看全部）";
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
        if (BackpackUI.IsOpen || HanziSystem.AwaitingChoice) return;

        for (int i = 0; i < SlotCount; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                UseSlot(i);
                break;
            }
        }

        if (Input.GetKeyDown(KeyCode.Q)) UseBest();

        HandleMouseClick();
    }

    void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i] == null) continue;
            RectTransform rt = frames[i].rectTransform;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, Input.mousePosition, null, out local))
            {
                if (!rt.rect.Contains(local)) continue;
                UseSlot(i);
                return;
            }
        }
    }

    /// <summary>按数字键：切到第 index 格显示的那个汉字</summary>
    void UseSlot(int index)
    {
        if (hanzi == null) return;
        if (index < 0 || index >= shown.Count) return;

        HanziOption opt = shown[index];
        if (opt == null || opt.def == null)
        {
            GameToast.Show($"{index + 1} 号位是空的（仓库还拼不出这么多字）", new Color(0.8f, 0.8f, 0.8f));
            return;
        }

        if (hanzi.CurrentHanziDef != null && opt.def.hanziName == hanzi.currentHanziName)
        {
            GameToast.Show($"已经是「{opt.def.hanziName}」了", new Color(0.85f, 0.85f, 0.85f));
            return;
        }

        if (hanzi.SwitchTo(opt.def))
        {
            Sfx.Play(Sfx.Id.Combine, 0.6f);
            GameToast.Show($"切换到「{opt.def.hanziName}」", new Color(0.7f, 1f, 0.8f));
            ForceRefresh();
        }
        else
        {
            GameToast.Show($"「{opt.def.hanziName}」切换失败（笔画不足）", new Color(1f, 0.6f, 0.5f));
        }
    }

    /// <summary>Q：切到最复杂的那个（第一格）</summary>
    void UseBest()
    {
        if (hanzi == null) return;

        HanziDef best = hanzi.SwitchToBestAvailable();
        if (best != null)
        {
            Sfx.Play(Sfx.Id.Combine, 0.6f);
            GameToast.Show($"已切到最强：「{best.hanziName}」", new Color(1f, 0.9f, 0.5f));
            ForceRefresh();
        }
        else
        {
            GameToast.Show("仓库里还拼不出别的字", new Color(0.8f, 0.8f, 0.8f));
        }
    }

    void ForceRefresh()
    {
        lastSignature = "";
    }

    void Refresh()
    {
        if (hanzi == null) return;

        // 签名：当前汉字 + 仓库内容。变了才重算（否则每帧都要跑一遍匹配 + 取字形）
        string signature = hanzi.currentHanziName + "|";
        List<StrokeType> avail = hanzi.AvailableStrokes();
        for (int i = 0; i < avail.Count; i++) signature += (int)avail[i] + ",";

        if (signature == lastSignature) return;
        lastSignature = signature;

        // 取"能拼出的所有字"，按复杂度降序；快捷栏显示前 9 个
        shown.Clear();
        List<HanziOption> all = hanzi.GetAllFormable();
        for (int i = 0; i < SlotCount; i++)
            shown.Add(i < all.Count ? all[i] : null);

        for (int i = 0; i < SlotCount; i++)
        {
            HanziOption opt = shown[i];
            bool has = opt != null && opt.def != null;

            if (glyphPreviews[i] != null)
            {
                if (!has)
                {
                    glyphPreviews[i].enabled = false;
                }
                else
                {
                    string name = opt.def.hanziName;
                    Sprite sp;
                    if (!glyphCache.TryGetValue(name, out sp))
                    {
                        sp = HanziRenderer.CreateSpriteFor(name, 64, 32f);
                        glyphCache[name] = sp;
                    }
                    glyphPreviews[i].sprite = sp;
                    glyphPreviews[i].enabled = sp != null;

                    bool isCurrent = hanzi.currentHanziName == name;
                    glyphPreviews[i].color = isCurrent
                        ? new Color(0.55f, 1f, 0.65f)      // 当前字：绿
                        : Color.white;
                }
            }

            if (labels[i] != null)
                labels[i].text = has ? (i + 1).ToString() : "";

            if (frames[i] != null)
            {
                bool isCurrent = has && hanzi.currentHanziName == opt.def.hanziName;
                frames[i].color = has
                    ? (isCurrent ? new Color(0.28f, 0.5f, 0.32f, 0.95f)
                                 : new Color(0.9f, 0.8f, 0.3f, 0.9f))
                    : new Color(0.2f, 0.22f, 0.32f, 0.5f);
            }
        }

        if (hint != null)
        {
            int total = all.Count;
            int shownCount = 0;
            foreach (var o in shown) if (o != null) shownCount++;
            hint.text = total > shownCount
                ? $"1-9 换字    Q 最强    E 背包（还有 {total - shownCount} 个可换，翻页查看）"
                : "1-9 换字    Q 最强    E 背包（可翻页看全部）";
        }
    }

    void LateUpdate()
    {
        Refresh();
    }
}
