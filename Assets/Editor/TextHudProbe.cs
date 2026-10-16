#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 列出每个场景里**所有 UI 文字对象**：名字、内容、屏幕位置、是否激活、有没有脚本驱动。
///
/// 目的：找出"游戏上方一直出现的提示字"到底是哪一个。
///
/// 命令行：-executeMethod TextHudProbe.Run
/// </summary>
public static class TextHudProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 场景里的 UI 文字 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level0", "Level1", "Level2", "Level3" })
        {
            string path = $"Assets/Scenes/{scene}.unity";
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            sb.AppendLine($"===== {scene} =====");

            int n = 0;
            foreach (TMPro.TextMeshProUGUI tmp in Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
            {
                n++;
                RectTransform rt = tmp.rectTransform;

                // 屏幕归一化位置（anchor 0,1 = 左上）
                string where = rt != null
                    ? $"anchor({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}) pos({rt.anchoredPosition.x:0.##},{rt.anchoredPosition.y:0.##})"
                    : "?";

                // 这个对象上挂了什么脚本
                var scripts = new List<string>();
                foreach (MonoBehaviour mb in tmp.GetComponents<MonoBehaviour>())
                    if (mb != null && !(mb is TMPro.TextMeshProUGUI) && !(mb is UnityEngine.UI.Graphic))
                        scripts.Add(mb.GetType().Name);

                sb.AppendLine($"  [{tmp.name}]");
                sb.AppendLine($"      text = \"{(tmp.text ?? "").Replace("\n", "\\n")}\"");
                sb.AppendLine($"      字号 = {tmp.fontSize}   激活 = {tmp.gameObject.activeInHierarchy}");
                sb.AppendLine($"      位置 = {where}");
                sb.AppendLine($"      父物体 = {(tmp.transform.parent != null ? tmp.transform.parent.name : "(根)")}");
                sb.AppendLine($"      同物体脚本 = {(scripts.Count > 0 ? string.Join(", ", scripts.ToArray()) : "**没有**（静态残留）")}");
                sb.AppendLine();
            }

            foreach (TMPro.TextMeshPro tmp in Object.FindObjectsOfType<TMPro.TextMeshPro>(true))
            {
                n++;
                sb.AppendLine($"  [世界空间 {tmp.name}]  text=\"{tmp.text}\"  字号={tmp.fontSize}  " +
                              $"位置=({tmp.transform.position.x:0.#},{tmp.transform.position.y:0.#})");
                sb.AppendLine();
            }

            if (n == 0) sb.AppendLine("  （没有）");
            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_texthud.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[UI 文字]\n" + sb.ToString());
    }
}
#endif
