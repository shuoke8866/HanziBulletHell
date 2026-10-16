#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 找出"开局时屏幕上会一直显示什么"。
///
/// 逐个列出场景 Canvas 下**根级 UI 对象**及其子对象的激活状态，
/// 这样能一眼看出哪些文字是常驻的。
///
/// 命令行：-executeMethod HudProbe.Run
/// </summary>
public static class HudProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 常驻 UI 清查 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level0", "Level1" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);
            GameData.currentFloor = scene == "Level0" ? 0 : 1;

            sb.AppendLine($"===== {scene} =====");
            sb.AppendLine();

            foreach (Canvas c in Object.FindObjectsOfType<Canvas>(true))
            {
                sb.AppendLine($"  Canvas [{c.name}] order={c.sortingOrder} 激活={c.gameObject.activeInHierarchy}");

                foreach (Transform child in c.transform)
                {
                    DumpNode(child, 2, sb);
                }
                sb.AppendLine();
            }
        }

        File.WriteAllText("F:/HanziBulletHell/_hud.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[常驻 UI]\n" + sb.ToString());
    }

    static void DumpNode(Transform t, int depth, StringBuilder sb)
    {
        string pad = new string(' ', depth * 4);

        var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
        string kind = "";

        if (tmp != null)
        {
            string content = (tmp.text ?? "").Replace("\n", "\\n");
            kind = $"  ★文字 字号={tmp.fontSize} 内容=\"{content}\"";
        }
        else if (t.GetComponent<Image>() != null) kind = "  (图片)";

        sb.AppendLine($"{pad}{t.name}  激活={t.gameObject.activeInHierarchy}{kind}");

        // 只往下两层，避免刷屏
        if (depth >= 4) return;
        foreach (Transform child in t) DumpNode(child, depth + 1, sb);
    }
}
#endif
