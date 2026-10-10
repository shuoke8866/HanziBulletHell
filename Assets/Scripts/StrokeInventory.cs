using UnityEngine;
using System.Collections.Generic;

public class StrokeInventory : MonoBehaviour
{
    [Header("背包设置")]
    public int maxSlots = 6;

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
        // 数字键1-6：选择背包中的笔画尝试合成
        // （背包面板打开时由 BackpackUI 接管按键，避免一次按键触发两套逻辑）
        if (BackpackUI.IsOpen) return;

        for (int i = 0; i < strokes.Count && i < maxSlots; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                TryCombineFromBackpack(i);
            }
        }
    }

    // 尝试把背包第 index 个笔画加入当前汉字
    public bool TryCombineFromBackpack(int index)
    {
        if (hanzi == null)
        {
            hanzi = GetComponent<HanziSystem>();
            if (hanzi == null) return false;
        }

        if (index < 0 || index >= strokes.Count) return false;

        StrokeType stroke = strokes[index];

        if (hanzi.TryAddStroke(stroke))
        {
            strokes.RemoveAt(index);
            SaveToGameData();
            GameLog.Info($"背包笔画 [{stroke}] 融合成功！当前汉字：「{hanzi.currentHanziName}」");
            return true;
        }

        GameLog.Info($"笔画 [{stroke}] 无法与当前汉字组合，留在背包");
        return false;
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

    // 添加笔画到背包
    public bool AddStroke(StrokeType s)
    {
        if (strokes.Count >= maxSlots)
        {
            GameLog.Info("背包已满！笔画留在原地。");
            return false;
        }
        strokes.Add(s);
        SaveToGameData();
        GameLog.Info($"笔画 [{s}] 进入背包。背包：{strokes.Count}/{maxSlots}");
        return true;
    }

    // 就地同步到 GameData，保持引用不变
    public void SaveToGameData()
    {
        if (GameData.inventoryStrokes == null)
            GameData.inventoryStrokes = new List<StrokeType>();

        GameData.inventoryStrokes.Clear();
        GameData.inventoryStrokes.AddRange(strokes);
    }
}