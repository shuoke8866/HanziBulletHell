#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 验证「字幕 × 门」协同是否真的能工作。
///
/// 重点查这次修的根因：**门初始化时就是开的**。
///
/// 命令行：-executeMethod DoorRuleVerify.Run
/// </summary>
public static class DoorRuleVerify
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字幕 × 门 协同验证 ==========");
        sb.AppendLine();

        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);
        GameData.currentFloor = 0;
        GameData.inTutorial = true;

        // ---- 1) 场景里的门初始状态 ----
        sb.AppendLine("--- 1. 场景里的门 ---");
        int openAtStart = 0;

        foreach (Door d in Object.FindObjectsOfType<Door>(true))
        {
            string state = d.isOpen ? "**开**" : "关";
            sb.AppendLine($"  {d.name,-14} pos=({d.transform.position.x:0.#},{d.transform.position.y:0.#})  " +
                          $"isOpen={state}");
            if (d.isOpen) openAtStart++;
        }

        sb.AppendLine();
        if (openAtStart > 0)
            sb.AppendLine($"  ⚠ 有 {openAtStart} 扇门场景里就是开的。");
        else
            sb.AppendLine("  ✓ 所有门场景里都是关的");

        sb.AppendLine("  （注意：Door.Start() 会按 isOpen 设状态，所以场景里写开 = 一进游戏就开）");
        sb.AppendLine();

        // ---- 2) 规则表 ----
        sb.AppendLine("--- 2. 门规则表 ---");

        GameText.门规则[] rules = GameText.门规则表();
        if (rules == null || rules.Length == 0)
        {
            sb.AppendLine("  ✗ **规则表是空的** —— 没有任何字幕会开门");
        }
        else
        {
            GameText.CoachStep[] steps = GameText.教学步骤();

            foreach (GameText.门规则 r in rules)
            {
                string first = "(越界)";
                string room = "?";

                if (r.字幕序号 >= 0 && r.字幕序号 < steps.Length)
                {
                    GameText.CoachStep s = steps[r.字幕序号];
                    room = s.room ?? "?";
                    first = (s.lines != null && s.lines.Length > 0) ? s.lines[0] : "";
                    if (first.Length > 24) first = first.Substring(0, 24) + "…";
                }

                // 门名是否存在
                bool doorExists = false;
                foreach (Door d in Object.FindObjectsOfType<Door>(true))
                    if (d.name == r.门名) { doorExists = true; break; }

                sb.AppendLine($"  第 {r.字幕序号 + 1,2} 条 → 「{r.门名}」  延迟 {r.延迟:0.#}s");
                sb.AppendLine($"      字幕: [{room}] \"{first}\"");
                if (!doorExists)
                    sb.AppendLine($"      ✗ **场景里没有叫「{r.门名}」的门！**");
            }
        }

        sb.AppendLine();

        // ---- 3) 每扇门是否被某个规则覆盖 ----
        sb.AppendLine("--- 3. 覆盖检查（每扇门有没有规则会开它）---");

        foreach (Door d in Object.FindObjectsOfType<Door>(true))
        {
            bool covered = false;
            foreach (GameText.门规则 r in rules)
                if (r.门名 == d.name) { covered = true; break; }

            sb.AppendLine($"  {d.name,-14} {(covered ? "✓ 有规则会开" : "⚠ 没有规则开它 —— 会一直关着")}");
        }

        sb.AppendLine();

        // ---- 4) 教学步骤与门规则的对照 ----
        sb.AppendLine("--- 4. 全部字幕（看哪几条配了开门）---");

        GameText.CoachStep[] allSteps = GameText.教学步骤();
        for (int i = 0; i < allSteps.Length; i++)
        {
            string door = null;
            foreach (GameText.门规则 r in rules)
                if (r.字幕序号 == i) { door = r.门名; break; }

            string first = (allSteps[i].lines != null && allSteps[i].lines.Length > 0)
                ? allSteps[i].lines[0] : "";
            if (first.Length > 26) first = first.Substring(0, 26) + "…";

            sb.AppendLine($"  {i + 1,2}. [{allSteps[i].room,-14}] \"{first}\"" +
                          (door != null ? $"  → 开「{door}」" : ""));
        }

        File.WriteAllText("F:/HanziBulletHell/_doorrule.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[门规则]\n" + sb.ToString());
    }
}
#endif
