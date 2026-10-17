#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 诊断：教学层的 TutorialCoach 到底会不会出来、字幕会不会显示。
///
/// 检查链路（每一步都可能断）：
///   1. GameData.inTutorial 是不是 true
///   2. PlayerRef.Transform 找不找得到玩家
///   3. GameText.教学步骤() 有没有内容、每条有没有 room
///   4. 玩家出生点落在哪个房间，那间房的步骤是不是 InRoom
///   5. FloatingTip.ShowAtOffset 能不能成功建出文字对象
///
/// 命令行：-executeMethod CoachDiagnose.Run
/// </summary>
public static class CoachDiagnose
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== TutorialCoach 诊断 ==========");
        sb.AppendLine();

        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);
        GameData.currentFloor = 0;
        GameData.inTutorial = true;

        // ---- 1) 全局状态 ----
        sb.AppendLine("--- 1. 全局状态 ---");
        sb.AppendLine($"  GameData.inTutorial = {GameData.inTutorial}   （必须 true）");
        sb.AppendLine($"  GameData.currentFloor = {GameData.currentFloor}");
        sb.AppendLine();

        // ---- 2) 玩家 ----
        sb.AppendLine("--- 2. 玩家引用 ---");
        Transform p = PlayerRef.Transform;
        sb.AppendLine($"  PlayerRef.Transform = {(p != null ? p.name + " @ " + p.position : "**null**")}");
        GameObject byTag = GameObject.FindGameObjectWithTag(GameBalance.PlayerTag);
        sb.AppendLine($"  FindGameObjectWithTag(\"{GameBalance.PlayerTag}\") = " +
                      $"{(byTag != null ? byTag.name : "**null**")}");
        sb.AppendLine();

        // ---- 3) 步骤 ----
        sb.AppendLine("--- 3. 教学步骤 ---");
        GameText.CoachStep[] steps = GameText.教学步骤();
        sb.AppendLine($"  步骤数 = {steps.Length}");

        int inRoom = 0, noRoom = 0;
        for (int i = 0; i < steps.Length; i++)
        {
            bool isInRoom = steps[i].trigger == GameText.CoachTrigger.InRoom;
            if (isInRoom) inRoom++;
            if (string.IsNullOrEmpty(steps[i].room)) noRoom++;

            string first = steps[i].lines != null && steps[i].lines.Length > 0 ? steps[i].lines[0] : "";
            if (first.Length > 20) first = first.Substring(0, 20) + "…";
            sb.AppendLine($"    {i + 1,2}. trigger={steps[i].trigger,-10} room={steps[i].room ?? "(null)",-14} " +
                          $"lines={steps[i].lines?.Length ?? 0} 首句=\"{first}\"");
        }
        sb.AppendLine($"  InRoom 步骤 = {inRoom}   没有 room 的 = {noRoom}");
        sb.AppendLine();

        // ---- 4) 出生房匹配 ----
        sb.AppendLine("--- 4. 出生房与步骤匹配 ---");
        if (p != null)
        {
            foreach (Room r in Object.FindObjectsOfType<Room>())
            {
                var box = r.GetComponent<Collider2D>();
                if (box == null) continue;
                if (!box.bounds.Contains(p.position)) continue;

                sb.AppendLine($"  玩家在「{r.name}」");

                int match = 0;
                for (int i = 0; i < steps.Length; i++)
                    if (steps[i].trigger == GameText.CoachTrigger.InRoom && steps[i].room == r.name)
                    {
                        match++;
                        sb.AppendLine($"      → 匹配第 {i + 1} 条: \"{steps[i].lines[0]}\"");
                    }

                if (match == 0)
                    sb.AppendLine("      ✗ **这间房没有任何 InRoom 步骤 —— 所以不会有字幕！**");
            }
        }
        sb.AppendLine();

        // ---- 5) FloatingTip 能不能建出文字 ----
        sb.AppendLine("--- 5. 文字渲染链路 ---");
        var anchor = new GameObject("dummyAnchor");
        anchor.transform.position = p != null ? p.position : Vector3.zero;

        FloatingTip.Handle h = FloatingTip.ShowAtOffset(
            "测试文字", Color.white, anchor.transform,
            new Vector3(0f, 1f, 0f), 1f,
            float.PositiveInfinity, faceCamera: false);

        if (h == null) sb.AppendLine("  ✗ ShowAtOffset 返回 null —— 文字建不出来");
        else
        {
            var found = GameObject.Find("FloatingTip");
            if (found == null) sb.AppendLine("  ✗ 返回了句柄但找不到 FloatingTip 对象");
            else
            {
                var tmp = found.GetComponent<TMPro.TextMeshPro>();
                sb.AppendLine($"  ✓ 文字对象已创建，内容=\"{tmp.text}\" 字号={tmp.fontSize} " +
                              $"lossyScale={found.transform.lossyScale} 激活={found.activeInHierarchy}");
            }
            FloatingTip.Clear(h);
        }
        Object.DestroyImmediate(anchor);

        File.WriteAllText("F:/HanziBulletHell/_coachdiag.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[Coach 诊断]\n" + sb.ToString());
    }
}
#endif
