using UnityEngine;
using System.Collections.Generic;

public class StrokeInventory : MonoBehaviour
{
    [Header("背包设置")]
    public int maxSlots = GameBalance.InventorySlots;

    [Header("当前背包内容（只读，运行时查看）")]
    public List<StrokeType> strokes = new List<StrokeType>();

    private HanziSystem hanzi;
    private bool restorePending = false;

    void Awake()
    {
        // 切层时把上一层的背包内容恢复回来（真肉鸽的核心：笔画不能丢）
        if (GameData.inventoryStrokes != null && GameData.inventoryStrokes.Count > 0)
        {
            strokes.Clear();
            strokes.AddRange(GameData.inventoryStrokes);
            restorePending = true;
        }
    }

    void Start()
    {
        hanzi = GetComponent<HanziSystem>();

        if (restorePending)
        {
            GameLog.Info($"从 GameData 恢复背包，共 {strokes.Count} 个笔画");
            restorePending = false;
        }
    }

    void Update()
    {
        // 数字键 1-9：按仓库里的笔画切换汉字
        // （背包面板打开时由 BackpackUI 接管按键，避免一次按键触发两套逻辑）
        if (BackpackUI.IsOpen) return;

        int n = Mathf.Min(strokes.Count, 9);
        for (int i = 0; i < n; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                TryCombineFromBackpack(i);
            }
        }
    }

    /// <summary>
    /// 数字键：按下仓库里第 index 个笔画 → 切换到"能用上这一笔的最复杂汉字"。
    ///
    /// **这里以前是错的**：它调用 hanzi.TryAddStroke(stroke)，而那是**拾取**入口
    /// （会走 ApplyBestFor → 重新求解最优解）。于是每次按数字键都相当于"又捡了一笔"，
    /// 解算器只看池子、没有保持现状的概念，结果越按越简单，最后退回「一」；
    /// 同时那一笔还会被 RemoveAt 从背包里删掉，笔画就凭空消失了。
    ///
    /// 现在改成"切换语义"：不增不减笔画，只在已有仓库里挑一个用得上这一笔的字切过去。
    /// 笔画永久保留（GameData.allStrokes），所以随时能再切回来。
    /// </summary>
    public bool TryCombineFromBackpack(int index)
    {
        if (hanzi == null)
        {
            hanzi = GetComponent<HanziSystem>();
            if (hanzi == null) return false;
        }

        if (index < 0 || index >= strokes.Count) return false;

        StrokeType stroke = strokes[index];

        // 优先切到"能用到这一笔"的最复杂汉字
        HanziDef pick = hanzi.SwitchToBestUsing(stroke);

        // 没有能用到它的字 → 退回"可用笔画能拼出的最优解"
        if (pick == null)
            pick = hanzi.SwitchToBestAvailable();

        if (pick == null)
        {
            GameLog.Info($"仓库里的笔画拼不出任何新字（当前「{hanzi.currentHanziName}」）");
            return false;
        }

        GameLog.Info($"按「{StrokeUtil.NameOf(stroke)}」切换 → 当前汉字：「{hanzi.currentHanziName}」");
        return true;
    }

    // 直接移除指定槽位（歧义选择时由 UI 判定成功后调用）
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= strokes.Count) return;
        strokes.RemoveAt(index);
        SaveToGameData();
    }

    /// <summary>按值移除若干笔画（多选合成用）。找不到的会忽略。</summary>
    public void RemoveStrokes(List<StrokeType> toRemove)
    {
        if (toRemove == null || toRemove.Count == 0) return;

        foreach (StrokeType s in toRemove)
        {
            int i = strokes.IndexOf(s);
            if (i >= 0) strokes.RemoveAt(i);
        }
        SaveToGameData();
    }

    /// <summary>按下标批量移除（多选合成用）。内部会从大到小删除，避免下标错位。</summary>
    public void RemoveIndices(List<int> indices)
    {
        if (indices == null || indices.Count == 0) return;

        List<int> sorted = new List<int>(indices);
        sorted.Sort();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            int idx = sorted[i];
            if (idx >= 0 && idx < strokes.Count) strokes.RemoveAt(idx);
        }
        SaveToGameData();
    }

    /// <summary>
    /// 往背包（= 可用笔画仓库）里放一笔。
    ///
    /// **不再有容量限制**。原来的实现是"满 9 格就拒绝 / 切换汉字时把装不下的
    /// 直接丢弃"，而这正是"字越切越简单、最后只能拼出一"的根因：
    /// 池子 = 当前字 + 背包且总量有限，每切一次都要丢笔画。
    ///
    /// 现在背包只是"仓库的显示窗口"，笔画永久保留（见 GameData.allStrokes）。
    /// maxSlots 仅用于决定 UI 一屏显示几格。
    /// </summary>
    public bool AddStroke(StrokeType s)
    {
        strokes.Add(s);
        SaveToGameData();
        GameLog.Info($"笔画 [{StrokeUtil.NameOf(s)}] 存入仓库。仓库：{strokes.Count} 笔");
        return true;
    }

    /// <summary>仓库里现在有多少笔（不再受 maxSlots 限制）</summary>
    public int Count { get { return strokes.Count; } }

    // 就地同步到 GameData，保持引用不变
    public void SaveToGameData()
    {
        if (GameData.inventoryStrokes == null)
            GameData.inventoryStrokes = new List<StrokeType>();

        GameData.inventoryStrokes.Clear();
        GameData.inventoryStrokes.AddRange(strokes);
    }
}