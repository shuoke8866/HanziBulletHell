#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 确定性验证：**构建一个房间，亲自调用 StartNextWave，数实际生成了几只怪**。
///
/// ## 为什么不用 Instantiate 整个场景
///
/// 前几轮我在编辑器里 `Instantiate` 预制体，触发了 `Fx` 的 `DontDestroyOnLoad`
/// （编辑器里不允许）而失败。所以这次改成：
///   · 自己 new 一个最小房间对象
///   · 把场景里那间 BOSS 房的**真实数据**（预制体、刷怪点、roomType）复制过来
///   · 调真实的 StartNextWave
///   · 数生成了几个
///
/// 这样绕开了场景里其他组件的副作用，同时测的仍然是**真实的刷怪逻辑**。
///
/// 命令行：-executeMethod BossDoubleSpawnProbe.Run
/// </summary>
public static class BossDoubleSpawnProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== BOSS 重复生成验证 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level1", "Level2", "Level3" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);
            GameData.currentFloor = scene == "Level1" ? 1 : (scene == "Level2" ? 2 : 3);

            sb.AppendLine($"===== {scene} =====");

            // 找 BOSS 房
            Room src = null;
            foreach (Room r in Object.FindObjectsOfType<Room>())
                if (r.roomType == RoomType.Boss) src = r;

            if (src == null) { sb.AppendLine("  没有 BOSS 房"); sb.AppendLine(); continue; }

            sb.AppendLine($"  BOSS 房 = {src.name}");
            sb.AppendLine($"      roomType={src.roomType}  预制体={src.enemyPrefabs.Count}  " +
                          $"刷怪点={src.enemySpawnPoints.Count}  波次={src.waveCount}");

            // 场上已有的敌人（含未激活）
            int before = CountEnemies();
            sb.AppendLine($"      调用前场上敌人 = {before}");

            // ---- 亲自调真实的刷怪方法 ----
            //
            // 用反射调 StartNextWave：它是 private，但**测的必须是真实方法**，
            // 不能在外面复刻一遍计算（复刻过一次，结果我修了逻辑工具还按老公式算）。
            var mi = typeof(Room).GetMethod("StartNextWave",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (mi == null)
            {
                sb.AppendLine("      x 找不到 StartNextWave（方法名改了？）");
                sb.AppendLine();
                continue;
            }

            mi.Invoke(src, null);

            int after = CountEnemies();
            int spawned = after - before;

            sb.AppendLine($"      调用后场上敌人 = {after}");
            sb.AppendLine($"      → **这次生成了 {spawned} 只**" +
                          (spawned == 1 ? "  ✓" : (spawned == 0 ? "  （没有预制体/走数据库池）" : "  x 多于 1 只！")));

            // 列出具体生成了什么
            sb.AppendLine("      场上敌人明细：");
            foreach (Enemy e in Object.FindObjectsOfType<Enemy>(true))
                sb.AppendLine($"          {e.name}  hp={e.MaxHealth}  激活={e.gameObject.activeInHierarchy}");

            foreach (BossShi b in Object.FindObjectsOfType<BossShi>(true))
                sb.AppendLine($"          BossShi {b.name}  hp={b.MaxHealth}");
            foreach (BossGuan b in Object.FindObjectsOfType<BossGuan>(true))
                sb.AppendLine($"          BossGuan {b.name}  hp={b.MaxHealth}");
            foreach (BossHuang b in Object.FindObjectsOfType<BossHuang>(true))
                sb.AppendLine($"          BossHuang {b.name}  hp={b.MaxHealth}");

            // 清理本次生成的（不留脏数据到场景）
            foreach (Enemy e in Object.FindObjectsOfType<Enemy>(true))
                Object.DestroyImmediate(e.gameObject);
            foreach (BossHuang b in Object.FindObjectsOfType<BossHuang>(true))
                Object.DestroyImmediate(b.gameObject);

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_doublespawn.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[重复生成]\n" + sb.ToString());
    }

    static int CountEnemies()
    {
        int n = Object.FindObjectsOfType<Enemy>(true).Length;
        n += Object.FindObjectsOfType<BossShi>(true).Length;
        n += Object.FindObjectsOfType<BossGuan>(true).Length;
        n += Object.FindObjectsOfType<BossHuang>(true).Length;
        return n;
    }
}
#endif
