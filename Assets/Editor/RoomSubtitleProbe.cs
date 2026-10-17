#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 验证「按房间播字幕」是否对得上：
///   1. 教学层每个房间的名字和范围
///   2. 玩家出生点在哪个房间里（第一条字幕就靠这个触发）
///   3. GameText.教学步骤() 里每条字幕分配给了哪个房间
///
/// 命令行：-executeMethod RoomSubtitleProbe.Run
/// </summary>
public static class RoomSubtitleProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 按房间播字幕 验证 ==========");
        sb.AppendLine();

        // ---- 1) 教学层的房间 + 玩家出生点 ----
        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);
        GameData.currentFloor = 0;
        GameData.inTutorial = true;

        sb.AppendLine("--- Level0（教程层）的房间 ---");

        var rooms = new List<Room>(Object.FindObjectsOfType<Room>());
        rooms.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        Vector3? playerPos = null;
        foreach (Transform t in Object.FindObjectsOfType<Transform>())
            if (t.CompareTag("Player")) { playerPos = t.position; break; }
        if (playerPos == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) playerPos = p.transform.position;
        }

        sb.AppendLine($"  玩家出生点 = {(playerPos.HasValue ? playerPos.Value.ToString("0.##") : "未找到")}");
        sb.AppendLine();

        foreach (Room r in rooms)
        {
            var box = r.GetComponent<Collider2D>();
            string range = box != null
                ? $"x[{box.bounds.min.x:0.#},{box.bounds.max.x:0.#}] y[{box.bounds.min.y:0.#},{box.bounds.max.y:0.#}]"
                : "?";

            bool inside = box != null && playerPos.HasValue && box.bounds.Contains(playerPos.Value);

            sb.AppendLine($"  {r.name,-16} {range}  类型={r.roomType}" +
                          (inside ? "   ★ **玩家出生在这里**" : ""));
        }

        // ---- 2) 教学步骤的房间分配 ----
        sb.AppendLine();
        sb.AppendLine("--- GameText.教学步骤() 的房间分配 ---");

        GameText.CoachStep[] steps = GameText.教学步骤();
        int noRoom = 0;

        for (int i = 0; i < steps.Length; i++)
        {
            GameText.CoachStep s = steps[i];
            string room = string.IsNullOrEmpty(s.room) ? "**（没有房间）**" : s.room;
            if (string.IsNullOrEmpty(s.room)) noRoom++;

            string first = (s.lines != null && s.lines.Length > 0) ? s.lines[0] : "";
            if (first.Length > 22) first = first.Substring(0, 22) + "…";

            sb.AppendLine($"  第 {i + 1,2} 条  房间={room,-16} trigger={s.trigger,-12} \"{first}\"");
        }

        sb.AppendLine();
        if (noRoom == 0)
            sb.AppendLine("  ✓ 所有步骤都指定了房间");
        else
            sb.AppendLine($"  ⚠ 有 {noRoom} 条没指定房间 —— 那些**永远不会播**（InRoom 触发需要房间名）");

        // ---- 3) 交叉核对：每条字幕的房间是不是真实存在的房间 ----
        sb.AppendLine();
        sb.AppendLine("--- 核对：步骤里的房间名是否真实存在 ---");

        var real = new HashSet<string>();
        foreach (Room r in rooms) real.Add(r.name);

        foreach (GameText.CoachStep s in steps)
        {
            if (string.IsNullOrEmpty(s.room)) continue;
            if (real.Contains(s.room)) continue;
            sb.AppendLine($"  ✗ 步骤引用的房间「{s.room}」在 Level0 里不存在！");
        }
        sb.AppendLine("  （没有 ✗ 就是全部对得上）");

        File.WriteAllText("F:/HanziBulletHell/_roomsub.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[房间字幕]\n" + sb.ToString());
    }
}
#endif
