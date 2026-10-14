using UnityEngine;

/// <summary>
/// 门上的引用计数。
///
/// 背景：场景里一扇门通常被两个相邻房间的 doors 列表同时引用
/// （比如「宝箱房 ↔ 战斗房」共用的那扇门）。
///
/// 旧逻辑是"只要还有任何一个房间占着，门就关着"：
///
///     if (claims > 0) door.Close(); else door.Open();
///
/// 这个规则有两个致命问题：
///   1. **宝箱房 / 起始房是 IsNonCombat，永远不会 Claim 门**，
///      所以一扇"宝箱房 ↔ 战斗房"共用的门完全由战斗房决定开关。
///      玩家清完宝箱房、走进相邻战斗房之后，如果中途离开，
///      门就卡在关闭状态 —— 这正是"第三层宝箱房无法开门"的表现。
///   2. 两个战斗房共用一扇门时，两边都要清完才计数归零；
///      只要有一边没清干净（比如敌人刷到了房间外），门永久关闭。
///
/// 现在的规则：**看"有没有房间正在战斗中"，而不是"有没有人 Claim"**。
///   已经开打且没清完的房间 → 关门
///   其余情况（没人打、或都打完了）→ 开门
///
/// 另外加了一个每帧兜底重算：房间清空会走 Release()，但"另一个房间先打完了"
/// 这类情况不会触发 Release，靠每帧重算保证门不会卡在关闭状态。
/// </summary>
public class DoorProbe : MonoBehaviour
{
    private int claims = 0;

    /// <summary>是否还有房间声明这扇门需要关着</summary>
    public bool IsClaimed { get { return claims > 0; } }

    /// <summary>当前是否处于"因战斗而关闭"的状态（供 UI / 诊断用）</summary>
    public bool IsLockedByCombat { get { return AnyRoomFighting(); } }

    public void Claim()
    {
        claims++;
        Apply();
    }

    public void Release()
    {
        claims = Mathf.Max(0, claims - 1);
        Apply();
    }

    /// <summary>强制清零（非战斗房进入时用，保证宝箱房一侧永远通畅）</summary>
    public void ForceReleaseAll()
    {
        claims = 0;
        Apply();
    }

    /// <summary>有没有房间正在战斗且没清完</summary>
    static bool AnyRoomFighting()
    {
        Room[] rooms = FindObjectsOfType<Room>();
        foreach (Room r in rooms)
        {
            if (r == null) continue;
            if (r.IsNonCombatRoom) continue;          // 起始房/宝箱房不锁门
            if (r.IsFighting) return true;
        }
        return false;
    }

    void Apply()
    {
        Door door = GetComponent<Door>();
        if (door == null) return;

        door.SetDoorState(!AnyRoomFighting());
    }

    /// <summary>
    /// 每帧兜底重算。
    ///
    /// 为什么需要：房间清理完会调用 Release()，但"另一个房间先打完了"
    /// 这类情况不会走到 Apply()，门就会卡在关闭状态、把玩家关死。
    /// 每帧重算一次，成本极低（房间数量个位数），但能彻底避免卡门。
    /// </summary>
    void Update()
    {
        Door door = GetComponent<Door>();
        if (door == null) return;

        bool shouldBeOpen = !AnyRoomFighting();
        if (door.isOpen != shouldBeOpen)
            door.SetDoorState(shouldBeOpen);
    }
}
