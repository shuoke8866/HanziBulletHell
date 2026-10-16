#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 实测两个玩家反馈的问题：
///   1. 教程层的敌人血量和其他层不一样
///   2. BOSS 房出现两个传送门
///
/// 命令行：-executeMethod Level0FixProbe.Run
/// </summary>
public static class Level0FixProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 教程层两个问题的实测 ==========");
        sb.AppendLine();

        // ============================================================
        //  问题 1：血量
        // ============================================================
        sb.AppendLine("--- 1) 各层小怪血量对比 ---");
        sb.AppendLine($"      全局倍率 = {GameBalance.EnemyHealthMultiplier}");
        sb.AppendLine();

        EnemyDef yi = EnemyDatabase.Get("yi");
        int floor1 = Mathf.RoundToInt(yi.health * GameBalance.EnemyHealthMultiplier);

        sb.AppendLine($"      横线怪(yi) 数据库 = {yi.health}");
        sb.AppendLine($"          第一层（数据库 × 全局倍率）  = {floor1}");
        sb.AppendLine();

        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);

        sb.AppendLine("      教程层各房间实际血量（打开场景读序列化值）：");
        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            string note;
            if (r.roomType != RoomType.Combat && r.roomType != RoomType.Elite)
                note = "（非战斗房）";
            else if (r.enemyHealthOverride > 0)
                note = $"覆盖 = {r.enemyHealthOverride}";
            else
                note = $"按数据库 × 倍率 = {floor1}";

            sb.AppendLine($"          {Pad(r.name, 14)} {note}");
        }
        sb.AppendLine();

        // 判定
        Room c1 = null, c2 = null;
        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            if (r.name.Contains("Collect")) c1 = r;
            if (r.name.Contains("Swap")) c2 = r;
        }

        sb.AppendLine("      判定：");
        sb.AppendLine(Describe(c1, floor1, "收集房"));
        sb.AppendLine(Describe(c2, floor1, "换字房"));
        sb.AppendLine();

        // ============================================================
        //  问题 2：传送门
        // ============================================================
        sb.AppendLine("--- 2) 传送门 ---");

        Room bossRoom = null;
        foreach (Room r in Object.FindObjectsOfType<Room>())
            if (r.roomType == RoomType.Boss) bossRoom = r;

        if (bossRoom == null) sb.AppendLine("      ❌ 找不到 BOSS 房");
        else
        {
            sb.AppendLine($"      BOSS 房 = {bossRoom.name}");
            sb.AppendLine($"      房间预放的传送门 = " +
                          $"{(bossRoom.portal != null ? bossRoom.portal.name : "(无)")}");

            if (bossRoom.portal != null)
            {
                sb.AppendLine($"          场景里的初始状态 = " +
                              $"{(bossRoom.portal.activeSelf ? "★ 显示中（有问题！）" : "隐藏（正确，清房后才亮）")}");

                // 统计场上所有 Portal 组件（含隐藏的）
                Portal[] all = Object.FindObjectsOfType<Portal>(true);
                sb.AppendLine($"      场景里的 Portal 组件总数 = {all.Length}");

                foreach (Portal p in all)
                    sb.AppendLine($"          · {p.gameObject.name}  " +
                                  $"active={p.gameObject.activeInHierarchy}  " +
                                  $"pos={p.transform.position}");
            }

            sb.AppendLine();
            sb.AppendLine("      规则检查：BOSS 死亡时会不会再生成一个？");
            GameManager gm = Object.FindObjectOfType<GameManager>();
            if (gm == null) sb.AppendLine("          ❌ 找不到 GameManager");
            else
            {
                bool willSkip = bossRoom.portal != null;
                sb.AppendLine($"          GameManager.portalPrefab = " +
                              $"{(gm.portalPrefab != null ? gm.portalPrefab.name : "(空)")}");
                sb.AppendLine(willSkip
                    ? "          ✓ 房间已预放 → GameManager 会跳过生成，总共只有 1 个"
                    : "          ✗ 房间没预放 → GameManager 会生成 1 个");
            }
        }

        File.WriteAllText("F:/HanziBulletHell/_level0fix.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[教程层实测]\n" + sb.ToString());
    }

    static string Describe(Room r, int floor1Health, string label)
    {
        if (r == null) return $"          {label}：找不到房间";

        int hp = r.enemyHealthOverride > 0 ? r.enemyHealthOverride : floor1Health;

        // 判定标准是"**至少**和第一层一样"，不是"必须更弱"。
        //
        // 一开始这里写的是 hp < floor1Health ? "✓" : "✗" ——
        // 那是错的：玩家明确要求教程层"至少和第一层一样"，
        // 因为教程层起始房就发六种笔画，玩家战力比刚进第一层强。
        // 工具的判断条件写错会给出假警报，比没有工具还糟。
        string verdict;
        if (hp == floor1Health) verdict = $"✓ 和第一层一致（{hp}）";
        else if (hp > floor1Health) verdict = $"✓ 比第一层更硬（{hp} > {floor1Health}）";
        else verdict = $"✗ **比第一层弱**（{hp} < {floor1Health}）—— 不符合要求";

        return $"          {label}：{hp} 血 —— {verdict}";
    }

    static string Pad(string s, int n)
    {
        while (s.Length < n) s += " ";
        return s;
    }
}
#endif
