#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 实测第二关 BOSS 房**到底会生成几只 BOSS**。
///
/// 玩家反馈"第二关会生成两个官"，而且"已经改了好几次了" ——
/// 所以这次不看配置、直接**模拟一遍刷怪逻辑**，数生成出来的数量。
///
/// 命令行：-executeMethod BossCountProbe.Run
/// </summary>
public static class BossCountProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== BOSS 生成数量实测 ==========");
        sb.AppendLine();

        foreach (string scene in new[] { "Level1", "Level2", "Level3" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);

            int floor = scene == "Level1" ? 1 : (scene == "Level2" ? 2 : 3);
            GameData.currentFloor = floor;

            sb.AppendLine($"===== {scene}（floor={floor}）=====");
            sb.AppendLine();

            sb.AppendLine($"  GameManager.ExtraEnemyCount = {GameManager.ExtraEnemyCount}");
            sb.AppendLine();

            foreach (Room r in Object.FindObjectsOfType<Room>())
            {
                if (r.roomType != RoomType.Boss) continue;

                sb.AppendLine($"  【BOSS 房 {r.name}】");

                // **不再复刻计算，直接调真实方法。**
                //
                // 第一版这里自己算了一遍 baseCount/extra ——
                // 结果我在 Room 里改了逻辑，探测工具还按老公式算，
                // 显示"还是 2 只"，白折腾一轮。
                // 教训：探测代码必须调用被测代码，不能重新实现它。
                int count = r.PreviewFirstWaveCount();

                sb.AppendLine($"      刷怪点={r.enemySpawnPoints.Count}  预制体={r.enemyPrefabs.Count}  " +
                              $"额外/波={r.extraEnemiesPerWave}  波次={r.waveCount}");
                sb.AppendLine($"      → **第 1 波会生成 {count} 只**" +
                              (count == 1 ? "  ✓" : "  ✗ 不是 1 只！"));

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        File.WriteAllText("F:/HanziBulletHell/_bosscount.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[BOSS数量]\n" + sb.ToString());
    }
}
#endif
