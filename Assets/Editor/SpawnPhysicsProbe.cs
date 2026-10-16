#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 用**物理引擎**实测玩家出生点会不会被挡住。
///
/// 光比较坐标不够 —— 玩家的碰撞体有半径、墙有厚度，
/// 而且可能是"相邻房间的墙叠在玩家身上"这种坐标上看不出的情况。
/// 所以这里直接把场景里的碰撞体喂给 Physics2D 查重叠。
///
/// 命令行：-executeMethod SpawnPhysicsProbe.Run
/// </summary>
public static class SpawnPhysicsProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 出生点物理实测 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level0", "Level1", "Level2", "Level3" })
        {
            string path = $"Assets/Scenes/{scene}.unity";
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            GameData.currentFloor = SceneToFloor(scene);

            sb.AppendLine($"--- {scene}（currentFloor={GameData.currentFloor}）---");

            GameObject player = GameObject.Find("Player");
            if (player == null) { sb.AppendLine("  ❌ 找不到 Player"); sb.AppendLine(); continue; }

            Vector3 pp = player.transform.position;
            sb.AppendLine($"  玩家位置 = ({pp.x:0.##}, {pp.y:0.##})");

            // 把场景里所有碰撞体同步到物理世界（编辑器里物理世界不是自动更新的）
            var cols = Object.FindObjectsOfType<Collider2D>();
            int wallCols = 0;

            Physics2D.SyncTransforms();

            foreach (Collider2D c in cols)
            {
                if (c == null) continue;
                if (!c.gameObject.CompareTag("Wall")) continue;
                wallCols++;
            }

            sb.AppendLine($"  场景碰撞体总数 = {cols.Length}，其中 tag=Wall 的 = {wallCols}");

            // 用玩家自己的碰撞体去查重叠
            Collider2D pc = player.GetComponent<Collider2D>();
            if (pc == null) { sb.AppendLine("  ❌ 玩家没有 Collider2D"); sb.AppendLine(); continue; }

            var filter = new ContactFilter2D();
            filter.useTriggers = true;          // 触发器也算（门可能就是触发器）
            filter.NoFilter();

            var hits = new List<Collider2D>();
            int n = pc.OverlapCollider(filter, hits);

            sb.AppendLine($"  玩家碰撞体 = {pc.GetType().Name}  " +
                          $"bounds=({pc.bounds.size.x:0.##}, {pc.bounds.size.y:0.##})  " +
                          $"isTrigger={pc.isTrigger}");
            sb.AppendLine($"  **与玩家重叠的碰撞体数量 = {n}**");

            foreach (Collider2D h in hits)
            {
                if (h == null) continue;
                sb.AppendLine($"      ★ 重叠：{h.gameObject.name}  " +
                              $"tag={h.gameObject.tag}  " +
                              $"{h.GetType().Name}  isTrigger={h.isTrigger}  " +
                              $"pos=({h.transform.position.x:0.#},{h.transform.position.y:0.#})");
            }

            if (n == 0)
                sb.AppendLine("      ✓ 出生点没有任何重叠 —— 不会卡住");

            // 玩家位置处的房间
            sb.AppendLine("  玩家所在房间：");
            foreach (Room r in Object.FindObjectsOfType<Room>())
            {
                Collider2D rc = r.GetComponent<Collider2D>();
                if (rc != null && rc.bounds.Contains(pp))
                    sb.AppendLine($"      {r.name}  类型={r.roomType}  " +
                                  $"bounds=({rc.bounds.size.x:0.#}, {rc.bounds.size.y:0.#})");
            }

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_spawnphys.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[出生点实测]\n" + sb.ToString());
    }

    static int SceneToFloor(string scene)
    {
        switch (scene)
        {
            case "Level0": return 0;
            case "Level1": return 1;
            case "Level2": return 2;
            case "Level3": return 3;
            default: return 1;
        }
    }
}
#endif
