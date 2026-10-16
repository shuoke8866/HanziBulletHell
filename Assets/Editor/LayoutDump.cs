#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 读出一层/二层的**真实房间布局**（用 Room 组件的碰撞盒，不读 Transform，
/// 因为预制体实例的坐标在场景文件里是 override，直接解析容易错）。
///
/// 目的：第三层要"照抄照搬"已验证的坐标系统，所以先把标准答案打出来。
///
/// 命令行：-executeMethod LayoutDump.Run
/// </summary>
public static class LayoutDump
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 已验证的关卡布局 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level1", "Level2" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);
            GameData.currentFloor = (scene == "Level1") ? 1 : 2;

            sb.AppendLine($"===== {scene} =====");
            sb.AppendLine();
            sb.AppendLine("  --- 房间 ---");

            var rooms = new List<Room>(Object.FindObjectsOfType<Room>());
            rooms.Sort((a, b) =>
            {
                float ax = a.transform.position.x, bx = b.transform.position.x;
                if (Mathf.Abs(ax - bx) > 0.1f) return ax.CompareTo(bx);
                return a.transform.position.y.CompareTo(b.transform.position.y);
            });

            foreach (Room r in rooms)
            {
                var box = r.GetComponent<BoxCollider2D>();
                Vector3 p = r.transform.position;

                string bounds = box != null
                    ? $"碰撞盒 {box.bounds.size.x:0.##}×{box.bounds.size.y:0.##}  " +
                      $"x[{box.bounds.min.x:0.##},{box.bounds.max.x:0.##}] " +
                      $"y[{box.bounds.min.y:0.##},{box.bounds.max.y:0.##}]"
                    : "**没有碰撞盒**";

                sb.AppendLine($"      {r.name,-12} 中心=({p.x:0.##}, {p.y:0.##})  " +
                              $"类型={r.roomType,-9} 波次={r.waveCount}");
                sb.AppendLine($"          {bounds}");
                sb.AppendLine($"          触发器={box?.isTrigger}  刷怪点={r.enemySpawnPoints.Count}  " +
                              $"预制体={r.enemyPrefabs.Count}  数据库池={r.useDatabasePool}");
            }

            sb.AppendLine();
            sb.AppendLine("  --- 门 ---");
            foreach (Door d in Object.FindObjectsOfType<Door>(true))
            {
                var box = d.GetComponent<BoxCollider2D>();
                Vector3 p = d.transform.position;
                string size = box != null ? $"{box.bounds.size.x:0.##}×{box.bounds.size.y:0.##}" : "?";

                sb.AppendLine($"      {d.name,-12} 位置=({p.x:0.##}, {p.y:0.##})  " +
                              $"尺寸={size}  旋转Z={d.transform.localEulerAngles.z:0.#}°");
            }

            sb.AppendLine();
            sb.AppendLine("  --- 墙段分组（每间房几段） ---");
            var byRoom = new Dictionary<string, int>();
            foreach (Transform t in Object.FindObjectsOfType<Transform>())
            {
                if (!t.name.StartsWith("_Wall_")) continue;
                // 名字形如 _Wall_Room1_L0
                string[] parts = t.name.Split('_');
                string key = parts.Length >= 3 ? parts[2] : t.name;
                if (byRoom.ContainsKey(key)) byRoom[key]++;
                else byRoom[key] = 1;
            }
            foreach (var kv in byRoom)
                sb.AppendLine($"      {kv.Key,-12} {kv.Value} 段");

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_layoutdump.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[布局]\n" + sb.ToString());
    }
}
#endif
