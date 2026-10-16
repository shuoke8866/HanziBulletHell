#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 墙体检视：对每间房，检查它四面（L/R/B/T）的墙段**是不是真的存在、位置对不对**。
///
/// 玩家截图显示"墙只画在房间之间的缝隙里，没有围住房间"，
/// 所以这里逐面核对，而不是笼统看总段数。
///
/// 命令行：-executeMethod WallAudit.Run
/// </summary>
public static class WallAudit
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 墙体检视 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level1", "Level3" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);
            GameData.currentFloor = (scene == "Level3") ? 3 : 1;

            sb.AppendLine($"===== {scene} =====");
            sb.AppendLine();

            // 收集所有墙段
            var walls = new List<SpriteRenderer>();
            foreach (SpriteRenderer sr in Object.FindObjectsOfType<SpriteRenderer>())
                if (sr.gameObject.CompareTag("Wall")) walls.Add(sr);

            sb.AppendLine($"  墙段总数 = {walls.Count}");
            sb.AppendLine();

            foreach (Room room in Object.FindObjectsOfType<Room>())
            {
                var box = room.GetComponent<BoxCollider2D>();
                if (box == null) continue;

                Vector2 c = (Vector2)room.transform.position + box.offset;
                Vector2 h = box.size * 0.5f;

                sb.AppendLine($"  【{room.name}】中心=({c.x:0.#},{c.y:0.#})  " +
                              $"范围 x[{c.x - h.x:0.#},{c.x + h.x:0.#}] y[{c.y - h.y:0.#},{c.y + h.y:0.#}]");

                // 四面各查一次
                CheckSide(sb, walls, "左(L)", true, c.x - h.x - 0.25f, c.y - h.y, c.y + h.y);
                CheckSide(sb, walls, "右(R)", true, c.x + h.x + 0.25f, c.y - h.y, c.y + h.y);
                CheckSide(sb, walls, "下(B)", false, c.y - h.y - 0.25f, c.x - h.x, c.x + h.x);
                CheckSide(sb, walls, "上(T)", false, c.y + h.y + 0.25f, c.x - h.x, c.x + h.x);

                sb.AppendLine();
            }

            // 把所有墙段按名字分组打印（看命名规律）
            sb.AppendLine("  墙段明细：");
            var byName = new List<string>();
            foreach (SpriteRenderer w in walls)
                byName.Add($"{w.name}  位置=({w.transform.position.x:0.##},{w.transform.position.y:0.##})  " +
                           $"尺寸=({w.bounds.size.x:0.##}×{w.bounds.size.y:0.##})");
            byName.Sort();
            foreach (string s2 in byName) sb.AppendLine("      " + s2);

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_wallaudit.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[墙体]\n" + sb.ToString());
    }

    /// <summary>检查某一面有没有墙覆盖</summary>
    static void CheckSide(StringBuilder sb, List<SpriteRenderer> walls, string label,
                          bool vertical, float fixedCoord, float a, float b)
    {
        // 这一面的墙应该在 fixedCoord 附近，并且沿 a~b 覆盖
        float covered = 0f;
        int segs = 0;

        foreach (SpriteRenderer w in walls)
        {
            Bounds wb = w.bounds;

            if (vertical)
            {
                if (wb.size.y < wb.size.x) continue;                       // 要竖墙
                if (Mathf.Abs(wb.center.x - fixedCoord) > 0.5f) continue;  // 位置要对
                float lo = Mathf.Max(wb.min.y, a);
                float hi = Mathf.Min(wb.max.y, b);
                if (hi > lo) { covered += hi - lo; segs++; }
            }
            else
            {
                if (wb.size.x < wb.size.y) continue;                       // 要横墙
                if (Mathf.Abs(wb.center.y - fixedCoord) > 0.5f) continue;
                float lo = Mathf.Max(wb.min.x, a);
                float hi = Mathf.Min(wb.max.x, b);
                if (hi > lo) { covered += hi - lo; segs++; }
            }
        }

        float need = b - a;
        string verdict = covered <= 0.01f
            ? "✗ **完全没有墙**"
            : (covered < need - 0.5f
                ? $"✗ 只覆盖 {covered:0.##}/{need:0.##}"
                : $"✓ 覆盖 {covered:0.##}/{need:0.##}（{segs} 段）");

        sb.AppendLine($"      {label}  x/y={fixedCoord,7:0.##}  范围 {a:0.#}~{b:0.#}   {verdict}");
    }
}
#endif
