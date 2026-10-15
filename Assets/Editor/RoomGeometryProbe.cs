#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 打印每个关卡里房间的实际判定范围与门的位置，用来核对墙体该铺在哪。
///
/// 背景：房间的 BoxCollider2D 是**故意互相重叠**的（触发用），
/// 直接用 collider 边界去铺墙会铺错位置 —— 必须先看清真实几何。
///
/// 菜单：Tools/诊断房间几何
/// 命令行：-executeMethod RoomGeometryProbe.Run
/// </summary>
public static class RoomGeometryProbe
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level0.unity",
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    [MenuItem("Tools/诊断房间几何")]
    public static void Run()
    {
        var sb = new StringBuilder();

        foreach (string path in Scenes)
        {
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            sb.AppendLine($"===== {Path.GetFileName(path)} =====");

            foreach (Room room in Object.FindObjectsOfType<Room>())
            {
                BoxCollider2D box = room.GetComponent<BoxCollider2D>();
                if (box == null) continue;

                Vector2 c = (Vector2)room.transform.position + box.offset;
                Vector2 h = box.size * 0.5f;

                sb.AppendLine($"  房间「{room.name}」type={room.roomType} " +
                              $"中心=({c.x},{c.y}) 范围 x[{c.x - h.x},{c.x + h.x}] y[{c.y - h.y},{c.y + h.y}]");

                if (room.doors == null) continue;
                foreach (Door d in room.doors)
                {
                    if (d == null) { sb.AppendLine("      门=<空>"); continue; }

                    Vector3 p = d.transform.position;
                    Vector3 s = d.transform.localScale;
                    string side = SideOf(c, h, p);
                    sb.AppendLine($"      门「{d.name}」({p.x},{p.y}) scale=({s.x},{s.y}) → {side}");
                }
            }

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_rooms.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[几何] 已写入 _rooms.txt\n" + sb.ToString());
    }

    static string SideOf(Vector2 c, Vector2 h, Vector3 p)
    {
        float dx = p.x - c.x;
        float dy = p.y - c.y;

        float nx = Mathf.Abs(dx) / Mathf.Max(0.01f, h.x);
        float ny = Mathf.Abs(dy) / Mathf.Max(0.01f, h.y);

        if (nx >= ny)
            return dx < 0 ? $"左边 (dx={dx:0.##})" : $"右边 (dx={dx:0.##})";

        return dy < 0 ? $"下边 (dy={dy:0.##})" : $"上边 (dy={dy:0.##})";
    }
}
#endif
