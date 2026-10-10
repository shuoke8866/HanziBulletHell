using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 暂停式「整理背包」界面。
/// 布局：层数/战绩 → 6 个笔画槽（色块+名字+候选汉字） → 词条 → 操作提示
/// 交互：1-6 或 鼠标点击选择笔画；多个候选汉字时弹出选择列表；
///       Esc / E 关闭；Tab 切换选中槽。
/// 全部 UI 由代码生成（色块 + 文字），不依赖任何美术资源。
/// </summary>
public class BackpackUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    [Header("UI引用（可留空，留空则运行时自动生成）")]
    public GameObject backpackPanel;
    public TextMeshProUGUI infoText;

    [Header("玩家引用（可留空，会自动查找）")]
    public StrokeInventory inventory;
    public HanziSystem hanzi;

    [Header("样式")]
    public float slotSize = 150f;
    public float slotGap = 14f;
    public int columns = 3;

    private bool isOpen = false;
    private bool usingRuntimePanel = false;
    private GameObject runtimePanel;

    // 运行时生成的控件
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI boonText;
    private TextMeshProUGUI hintText;
    private TextMeshProUGUI detailText;
    private readonly List<Image> slotFrames = new List<Image>();
    private readonly List<Image> slotBlocks = new List<Image>();
    private readonly List<TextMeshProUGUI> slotLabels = new List<TextMeshProUGUI>();

    // 歧义/多结果选择状态
    private bool choosing = false;
    private int choosingSlot = -1;
    private List<HanziDef> choiceOptions = new List<HanziDef>();
    private GameObject choicePanel;

    private int selectedSlot = 0;

    // 多选：勾选的槽位下标
    private readonly List<int> selectedIndices = new List<int>();

    void Start()
    {
        if (inventory == null) inventory = FindObjectOfType<StrokeInventory>();
        if (hanzi == null) hanzi = FindObjectOfType<HanziSystem>();

        if (backpackPanel != null && infoText != null)
        {
            // 场景里已经配好了 UI：沿用，但不再自己生成
            backpackPanel.SetActive(false);
        }
        else
        {
            usingRuntimePanel = true;
            BuildRuntimeUI();
            runtimePanel.SetActive(false);
        }
    }

    // ================= 运行时 UI 生成 =================
    void BuildRuntimeUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();

        int maxSlots = inventory != null ? inventory.maxSlots : 6;
        int rows = Mathf.CeilToInt(maxSlots / (float)columns);

        float gridW = columns * slotSize + (columns - 1) * slotGap;
        float gridH = rows * slotSize + (rows - 1) * slotGap;
        float panelW = Mathf.Max(760f, gridW + 80f);
        float panelH = gridH + 340f;

        runtimePanel = new GameObject("BackpackPanel (Runtime)");
        if (canvas != null) runtimePanel.transform.SetParent(canvas.transform, false);

        RectTransform rt = runtimePanel.AddComponent<RectTransform>();
        // 居中锚点 + 中心 pivot，保证面板真正在屏幕中央（之前用 pivot.y=1 会整体偏下）
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(panelW, panelH);

        Image bg = runtimePanel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.05f, 0.12f, 0.96f);

        // ---- 标题 ----
        headerText = MakeText(runtimePanel.transform, "Header", 30f,
            TextAlignmentOptions.Center, new Color(1f, 0.95f, 0.6f));
        SetTopAnchored(headerText.rectTransform, 0f, -16f, panelW - 40f, 46f);

        // ---- 笔画槽容器（水平居中） ----
        GameObject grid = new GameObject("Slots");
        grid.transform.SetParent(runtimePanel.transform, false);
        RectTransform grt = grid.AddComponent<RectTransform>();
        grt.anchorMin = new Vector2(0.5f, 1f);
        grt.anchorMax = new Vector2(0.5f, 1f);
        grt.pivot = new Vector2(0.5f, 1f);
        grt.anchoredPosition = new Vector2(0f, -70f);
        grt.sizeDelta = new Vector2(gridW, gridH);

        for (int i = 0; i < maxSlots; i++)
        {
            int col = i % columns;
            int row = i / columns;

            GameObject slot = new GameObject($"Slot{i + 1}");
            slot.transform.SetParent(grid.transform, false);
            RectTransform srt = slot.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(slotSize, slotSize);
            srt.anchoredPosition = new Vector2(
                col * (slotSize + slotGap),
                -row * (slotSize + slotGap));

            Image frame = slot.AddComponent<Image>();
            frame.color = new Color(0.25f, 0.28f, 0.4f, 1f);
            slotFrames.Add(frame);

            // 内部色块（表示笔画本身）+ 一个居中的大字，方便一眼认出
            GameObject block = new GameObject("Block");
            block.transform.SetParent(slot.transform, false);
            RectTransform brt = block.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(6f, 6f);
            brt.offsetMax = new Vector2(-6f, -6f);
            Image bi = block.AddComponent<Image>();
            bi.raycastTarget = false;
            slotBlocks.Add(bi);

            TextMeshProUGUI lbl = MakeText(slot.transform, "Label", 19f,
                TextAlignmentOptions.Center, Color.white);
            Stretch(lbl.rectTransform, 8f);
            slotLabels.Add(lbl);
        }

        // ---- 详情（槽位下方） ----
        float detailTop = 70f + gridH + 14f;
        detailText = MakeText(runtimePanel.transform, "Detail", 22f,
            TextAlignmentOptions.TopLeft, new Color(0.85f, 0.9f, 1f));
        SetTopAnchored(detailText.rectTransform, 0f, -detailTop, panelW - 80f, 92f);

        // ---- 词条 ----
        boonText = MakeText(runtimePanel.transform, "Boons", 20f,
            TextAlignmentOptions.TopLeft, new Color(0.7f, 1f, 0.8f));
        SetTopAnchored(boonText.rectTransform, 0f, -(detailTop + 96f), panelW - 80f, 46f);

        // ---- 操作提示（底部） ----
        hintText = MakeText(runtimePanel.transform, "Hint", 19f,
            TextAlignmentOptions.Center, new Color(0.7f, 0.75f, 0.85f));
        RectTransform hrt = hintText.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 14f);
        hrt.sizeDelta = new Vector2(panelW - 40f, 26f);
        hintText.text = "1-6 / 左键 勾选笔画    回车 或 右键 合成    C 清空选择    E/Esc 关闭";
    }

    // 顶部锚定（pivot 在上边中点），offsetY 传负数
    static void SetTopAnchored(RectTransform rt, float offsetX, float offsetY, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(offsetX, offsetY);
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

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    // ================= 输入 =================
    void Update()
    {
        if (!isOpen)
        {
            if (Input.GetKeyDown(KeyCode.E)) Toggle();
            return;
        }

        // 结果选择中：只处理选择
        if (choosing)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                CancelChoice();
                Toggle();
                return;
            }
            HandleChoiceInput();
            return;
        }

        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
        {
            Toggle();
            return;
        }

        // ---- 勾选 / 取消勾选 ----
        for (int i = 0; i < 6; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                ToggleSelect(i);
                return;
            }
        }

        // ---- 合成 ----
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
            || Input.GetKeyDown(KeyCode.Space))
        {
            TryCombineSelected();
            return;
        }

        // ---- 全部取消 ----
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.C))
        {
            ClearSelection();
            return;
        }

        // ---- Tab / 方向键移动高亮 ----
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.RightArrow))
            MoveHighlight(1);
        if (Input.GetKeyDown(KeyCode.LeftArrow))
            MoveHighlight(-1);

        // ---- 鼠标：左键勾选，右键直接合成 ----
        HandleMouseClick();

        // ---- 高亮槽位按 F 也能勾选 ----
        if (Input.GetKeyDown(KeyCode.F)) ToggleSelect(selectedSlot);

        RefreshUI();
    }

    void MoveHighlight(int dir)
    {
        int max = inventory != null ? Mathf.Max(1, Mathf.Min(inventory.maxSlots, inventory.strokes.Count)) : 1;
        selectedSlot = (selectedSlot + dir + max) % max;
    }

    void ToggleSelect(int index)
    {
        if (inventory == null) return;
        if (index < 0 || index >= inventory.strokes.Count) return;

        selectedSlot = index;

        if (selectedIndices.Contains(index)) selectedIndices.Remove(index);
        else selectedIndices.Add(index);
    }

    void ClearSelection()
    {
        selectedIndices.Clear();
    }

    List<StrokeType> SelectedStrokes()
    {
        List<StrokeType> list = new List<StrokeType>();
        foreach (int i in selectedIndices)
        {
            if (i >= 0 && i < inventory.strokes.Count) list.Add(inventory.strokes[i]);
        }
        return list;
    }

    void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
        bool rightClick = Input.GetMouseButtonDown(1);

        for (int i = 0; i < slotFrames.Count; i++)
        {
            if (slotFrames[i] == null) continue;
            RectTransform rt = slotFrames[i].rectTransform;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, Input.mousePosition, null, out local))
            {
                if (!rt.rect.Contains(local)) continue;

                selectedSlot = i;
                if (rightClick)
                {
                    // 右键：直接合成（单选就够）
                    TryCombineSelected();
                }
                else
                {
                    ToggleSelect(i);
                }
                return;
            }
        }
    }

    // 用勾选的笔画试合成
    void TryCombineSelected()
    {
        if (inventory == null || hanzi == null) return;

        List<StrokeType> sel = SelectedStrokes();
        if (sel.Count == 0)
        {
            GameToast.Show("先按 1-6 勾选要用的笔画", new Color(1f, 0.9f, 0.5f));
            return;
        }

        List<HanziDef> cands = hanzi.GetCombineCandidates(sel);
        if (cands.Count == 0)
        {
            GameToast.Show($"这 {sel.Count} 笔凑不出汉字", new Color(1f, 0.6f, 0.5f));
            return;
        }

        // 多个结果 → 让玩家选
        if (cands.Count > 1)
        {
            OpenChoice(selectedSlot, cands);
            return;
        }

        ApplySelection(cands[0], sel);
    }

    // 套用结果并消耗掉用掉的笔画
    void ApplySelection(HanziDef target, List<StrokeType> sel)
    {
        int needFromSelection = Mathf.Max(0, target.strokes.Count - hanzi.currentStrokes.Count);
        needFromSelection = Mathf.Min(needFromSelection, sel.Count);

        // 要消耗哪几个勾选槽位：按勾选顺序取前 needFromSelection 个
        List<int> consume = new List<int>();
        List<int> sortedSel = new List<int>(selectedIndices);
        sortedSel.Sort();
        for (int i = 0; i < needFromSelection && i < sortedSel.Count; i++)
            consume.Add(sortedSel[i]);

        // 如果是"勾选笔画自己成字"，currentStrokes 会被整体替换，消耗全部勾选
        if (target.strokes.Count == sel.Count) consume = new List<int>(sortedSel);

        inventory.RemoveIndices(consume);

        hanzi.ApplyHanziDef(target);
        GameToast.Show($"合成「{target.hanziName}」", new Color(1f, 0.95f, 0.6f));

        ClearSelection();
        selectedSlot = 0;
        RefreshUI();
    }

    void HandleChoiceInput()
    {
        for (int i = 0; i < choiceOptions.Count && i < 6; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                ResolveChoice(i);
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            CancelChoice();
    }

    // ================= 开关 =================
    void Toggle()
    {
        isOpen = !isOpen;
        IsOpen = isOpen;

        if (backpackPanel != null) backpackPanel.SetActive(isOpen);
        if (usingRuntimePanel && runtimePanel != null) runtimePanel.SetActive(isOpen);

        Time.timeScale = isOpen ? 0f : 1f;

        if (isOpen)
        {
            selectedSlot = 0;
            RefreshUI();
        }
        else
        {
            CancelChoice();
        }
    }

    void OnDisable()
    {
        if (isOpen)
        {
            isOpen = false;
            IsOpen = false;
            Time.timeScale = 1f;
        }
    }

    // ================= 使用笔画 =================
    // 单个笔画的快捷使用（双击/右键走多选路径）
    void TryUseSlot(int index)
    {
        if (inventory == null || hanzi == null) return;
        if (index < 0 || index >= inventory.strokes.Count) return;

        if (!selectedIndices.Contains(index)) selectedIndices.Add(index);
        TryCombineSelected();
    }

    // ================= 歧义选择弹窗 =================
    void OpenChoice(int slotIndex, List<HanziDef> options)
    {
        choosing = true;
        choosingSlot = slotIndex;
        choiceOptions = options;

        if (choicePanel != null) { choicePanel.SetActive(true); RefreshChoicePanel(); return; }

        // 生成选择面板
        choicePanel = new GameObject("ChoicePanel (Runtime)");
        choicePanel.transform.SetParent(runtimePanel.transform, false);
        RectTransform crt = choicePanel.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(520f, 300f);

        Image cbg = choicePanel.AddComponent<Image>();
        cbg.color = new Color(0.02f, 0.02f, 0.08f, 0.98f);

        RefreshChoicePanel();
    }

    void RefreshChoicePanel()
    {
        if (choicePanel == null) return;

        // 清掉旧内容（保留自身）
        for (int i = choicePanel.transform.childCount - 1; i >= 0; i--)
            Destroy(choicePanel.transform.GetChild(i).gameObject);

        TextMeshProUGUI title = MakeText(choicePanel.transform, "Title", 26f,
            TextAlignmentOptions.Center, new Color(1f, 0.9f, 0.5f));
        SetTopAnchored(title.rectTransform, 0f, -14f, 480f, 40f);
        title.text = "选择要组成的汉字";

        for (int i = 0; i < choiceOptions.Count && i < 6; i++)
        {
            HanziDef d = choiceOptions[i];

            GameObject row = new GameObject($"Option{i + 1}");
            row.transform.SetParent(choicePanel.transform, false);
            RectTransform rrt = row.AddComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.5f, 1f);
            rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(460f, 56f);
            rrt.anchoredPosition = new Vector2(0f, -60f - i * 62f);

            Image rbg = row.AddComponent<Image>();
            rbg.color = new Color(0.15f, 0.18f, 0.3f, 1f);

            TextMeshProUGUI t = MakeText(row.transform, "T", 22f,
                TextAlignmentOptions.Left, Color.white);
            Stretch(t.rectTransform, 12f);
            t.text = $"[{i + 1}]  {d.hanziName}    {d.description}";
        }

        TextMeshProUGUI hint = MakeText(choicePanel.transform, "Hint", 18f,
            TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 0.9f));
        RectTransform hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f);
        hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 10f);
        hrt.sizeDelta = new Vector2(480f, 30f);
        hint.text = "按数字键选择    Esc 取消（笔画留在背包）";
    }

    void ResolveChoice(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= choiceOptions.Count) return;

        HanziDef chosen = choiceOptions[optionIndex];

        // 记住偏好，以后再凑齐同一组笔画就自动变这个字
        hanzi.PinPreference(chosen);

        List<StrokeType> sel = SelectedStrokes();
        CancelChoice();

        // 按同一套规则消耗笔画
        ApplySelection(chosen, sel);
    }

    void CancelChoice()
    {
        choosing = false;
        choosingSlot = -1;
        choiceOptions.Clear();
        if (choicePanel != null) choicePanel.SetActive(false);
    }

    // ================= 刷新显示 =================
    void RefreshUI()
    {
        if (inventory == null || hanzi == null) return;

        if (headerText != null)
            headerText.text = $"第 {GameData.currentFloor} 层   ——   当前汉字「{hanzi.currentHanziName}」";

        // 槽位
        int maxSlots = inventory.maxSlots;
        for (int i = 0; i < slotFrames.Count; i++)
        {
            bool has = i < inventory.strokes.Count;
            bool isHover = i == selectedSlot;
            bool isChecked = selectedIndices.Contains(i);

            if (slotFrames[i] != null)
            {
                if (isChecked) slotFrames[i].color = new Color(0.35f, 1f, 0.45f, 1f);       // 已勾选：绿
                else if (isHover) slotFrames[i].color = new Color(1f, 0.85f, 0.3f, 1f);      // 高亮：黄
                else slotFrames[i].color = new Color(0.25f, 0.28f, 0.4f, 1f);                // 普通
            }

            if (slotBlocks[i] != null)
            {
                if (has)
                {
                    slotBlocks[i].enabled = true;
                    slotBlocks[i].color = StrokeUtil.ColorOf(inventory.strokes[i]);
                }
                else
                {
                    slotBlocks[i].enabled = false;
                }
            }

            if (slotLabels[i] != null)
            {
                if (has)
                {
                    StrokeType st = inventory.strokes[i];

                    // 已勾选时显示"这几笔当前能合成什么"，否则显示单笔的去向
                    string arrow;
                    if (isChecked)
                    {
                        arrow = "√ 已选";
                    }
                    else
                    {
                        List<HanziDef> growth = hanzi.GetGrowthCandidates(st);
                        List<HanziDef> exact = hanzi.GetDirectCandidates(st);

                        if (growth.Count == 1) arrow = $"长大→{growth[0].hanziName}";
                        else if (growth.Count > 1) arrow = $"长大→选({growth.Count})";
                        else if (exact.Count == 1) arrow = $"拼成→{exact[0].hanziName}";
                        else if (exact.Count > 1) arrow = $"拼成→选({exact.Count})";
                        else arrow = "存着";
                    }

                    string idx = i < 9 ? $"{i + 1} " : "";
                    // 用富文本给笔面上色，和世界里的掉落物、快捷栏颜色一致
                    slotLabels[i].text = $"{idx}{StrokeUtil.RichName(st)}\n{arrow}";
                }
                else
                {
                    slotLabels[i].text = i < 9 ? $"[{i + 1}] 空" : "空";
                }
            }
        }

        // 详情
        if (detailText != null)
        {
            string strokes = "";
            foreach (StrokeType s in hanzi.currentStrokes) strokes += StrokeName(s) + " ";

            int dmg = hanzi.CurrentDamageValue;

            // 勾选实时预览：这几笔能合成什么
            string preview;
            List<StrokeType> sel = SelectedStrokes();
            if (sel.Count == 0)
            {
                preview = "（按 1-6 勾选笔画，回车合成）";
            }
            else
            {
                string selNames = "";
                foreach (StrokeType s in sel) selNames += StrokeName(s) + " ";
                preview = $"已选 {sel.Count} 笔：{selNames.Trim()}   {hanzi.PreviewCombine(sel)}";
            }

            detailText.text =
                $"当前汉字：{hanzi.currentHanziName}（{strokes.Trim()}）\n" +
                $"{preview}\n" +
                $"单发伤害 {dmg}    射速加成 x{PlayerStats.fireRateMultiplier:0.00}    移速 x{PlayerStats.moveSpeedMultiplier:0.00}";
        }

        if (boonText != null)
            boonText.text = $"【本轮词条】{PlayerStats.BoonsSummary()}";

        if (infoText != null)
            infoText.text = BuildPlainText();
    }

    // 兼容旧的场景内置文本（Level2 的 BackpackText）
    string BuildPlainText()
    {
        string text = $"第 {GameData.currentFloor} 层 ——「{hanzi.currentHanziName}」\n";
        for (int i = 0; i < inventory.strokes.Count; i++)
        {
            text += $"[{i + 1}] {StrokeUtil.NameOf(inventory.strokes[i])}\n";
        }
        text += $"词条：{PlayerStats.BoonsSummary()}\n";
        text += "E 关闭 | F 开宝箱";
        return text;
    }

    // 保留旧接口，统一转发到 StrokeUtil（避免两套颜色定义不一致）
    public static Color StrokeColor(StrokeType s)
    {
        return StrokeUtil.ColorOf(s);
    }

    string StrokeName(StrokeType s)
    {
        return StrokeUtil.NameOf(s);
    }
}
