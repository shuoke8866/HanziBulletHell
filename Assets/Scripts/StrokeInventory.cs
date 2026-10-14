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
        // 这里**不再接管任何按键**。
        //
        // 换字的两条入口已经各有归属：
        //   底部常驻快捷栏 QuickBarUI —— 数字键 1-9 换成对应汉字
        //   背包（E 打开）BackpackUI —— 数字键 1-9 选字、A/D 翻页
        //
        // 以前这个类也监听 1-9 去"用第 N 个笔画合成"，和上面两处抢按键，
        // 而且它调的是拾取入口（会重排最优解 + 把笔画从仓库删掉），
        // 正是"按几次数字键字就退回「一」"的元凶。现在整段删掉。
    }

    /// <summary>
    // 说明：这个类以前有个 TryCombineFromBackpack(index)，按下数字键就"用第 N 个笔画合成"。
    // 它调用的是拾取入口，会重排最优解并把笔画从仓库删掉 —— 正是
    // "按几次数字键、字就退回「一」"的元凶。现在换字全部由
    // QuickBarUI（快捷栏）和 BackpackUI（背包）负责，这里的方法已删除。
    //
    // 想按"某一笔"来找字的话，用 HanziSystem.SwitchToBestUsing(stroke)。

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