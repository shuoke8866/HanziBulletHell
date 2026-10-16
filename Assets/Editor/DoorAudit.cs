#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 门诊断：位置 / 朝向 / 缩放 / 激活状态 / 能否真的开关。
///
/// 玩家反馈"第三层连门都没设计怎么玩"，所以这里把门的一切都打出来。
///
/// 命令行：-executeMethod DoorAudit.Run
/// </summary>
public static class DoorAudit
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 门诊断 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level1", "Level3" })
        {
            string path = $"Assets/Scenes/{scene}.unity";
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            GameData.currentFloor = (scene == "Level3") ? 3 : 1;

            sb.AppendLine($"--- {scene} ---");

            Door[] doors = Object.FindObjectsOfType<Door>(true);
            sb.AppendLine($"  场景里的 Door 组件数 = {doors.Length}");
            sb.AppendLine();

            foreach (Door d in doors)
            {
                Transform t = d.transform;
                var box = d.GetComponent<BoxCollider2D>();
                var sr = d.GetComponent<SpriteRenderer>();

                string size = box != null
                    ? $"碰撞盒 {box.bounds.size.x:0.##}×{box.bounds.size.y:0.##}"
                    : "**没有 BoxCollider2D**";

                string sprite = sr != null
                    ? (sr.sprite != null ? $"有贴图({sr.sprite.name})" : "**贴图为空**")
                    : "**没有 SpriteRenderer**";

                sb.AppendLine($"      {d.name}");
                sb.AppendLine($"          位置 = ({t.position.x:0.##}, {t.position.y:0.##})");
                sb.AppendLine($"          缩放 = ({t.localScale.x:0.##}, {t.localScale.y:0.##})  " +
                              $"旋转 = {t.localEulerAngles.z:0.#}°");
                sb.AppendLine($"          {size}   触发器={box?.isTrigger}");
                sb.AppendLine($"          {sprite}   激活={d.gameObject.activeInHierarchy}");

                // 门是横着还是竖着（决定玩家从哪个方向通过）
                bool horizontal = box != null && box.bounds.size.x > box.bounds.size.y;
                sb.AppendLine($"          朝向 = {(horizontal ? "横向（玩家左右通过）" : "纵向（玩家上下通过）")}");
                sb.AppendLine();
            }

            // 门和房间的关系
            sb.AppendLine("  门相对于房间的位置：");
            Room[] rooms = Object.FindObjectsOfType<Room>();
            foreach (Door d in doors)
            {
                Vector3 dp = d.transform.position;

                // 找最近的两个房间
                Room a = null, b = null;
                float da = float.MaxValue, db = float.MaxValue;
                foreach (Room r in rooms)
                {
                    float dist = Vector2.Distance(r.transform.position, dp);
                    if (dist < da) { b = a; db = da; a = r; da = dist; }
                    else if (dist < db) { b = r; db = dist; }
                }

                sb.AppendLine($"      {d.name,-10} 最近 {(a != null ? a.name : "?")}({da:0.#})" +
                              $" / {(b != null ? b.name : "?")}({db:0.#})");
            }

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_doorprobe.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[门诊断]\n" + sb.ToString());
    }
}
#endif
