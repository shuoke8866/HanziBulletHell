#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一次查清四个问题：
///   1. 血条跨关卡不延续
///   2. BOSS 血条不显示
///   3. 第二关生成两个「官」
///   4. 第三关关底 BOSS 不生成
///
/// 命令行：-executeMethod BugSweep.Run
/// </summary>
public static class BugSweep
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 四问题排查 ==========");
        sb.AppendLine();

        // ============================================================
        //  3 + 4：BOSS 生成
        // ============================================================
        foreach (string scene in new[] { "Level2", "Level3" })
        {
            EditorSceneManager.OpenScene($"Assets/Scenes/{scene}.unity", OpenSceneMode.Single);
            GameData.currentFloor = (scene == "Level2") ? 2 : 3;

            sb.AppendLine($"===== {scene} =====");
            sb.AppendLine();

            sb.AppendLine("  --- BOSS 房配置 ---");
            foreach (Room r in Object.FindObjectsOfType<Room>())
            {
                if (r.roomType != RoomType.Boss) continue;

                var box = r.GetComponent<BoxCollider2D>();
                Vector3 c = r.transform.position;

                sb.AppendLine($"      房间 {r.name}  中心=({c.x:0.#},{c.y:0.#})  " +
                              $"波次={r.waveCount}  刷怪点={r.enemySpawnPoints.Count}  " +
                              $"预制体={r.enemyPrefabs.Count}  数据库池={r.useDatabasePool}");
                if (box != null)
                    sb.AppendLine($"          范围 x[{box.bounds.min.x:0.#},{box.bounds.max.x:0.#}] " +
                                  $"y[{box.bounds.min.y:0.#},{box.bounds.max.y:0.#}]");

                foreach (GameObject p in r.enemyPrefabs)
                {
                    if (p == null) { sb.AppendLine("          · **预制体是空引用**"); continue; }

                    sb.AppendLine($"          · {p.name}  激活={p.activeSelf}  " +
                                  $"组件: {ComponentList(p)}");
                }

                sb.AppendLine($"          singleDefId = " +
                              $"{(string.IsNullOrEmpty(r.singleDefId) ? "(空)" : r.singleDefId)}");
            }

            sb.AppendLine();
            sb.AppendLine("  --- 场景里所有 BOSS 脚本 ---");
            foreach (BossShi b in Object.FindObjectsOfType<BossShi>(true))
                sb.AppendLine($"      BossShi   {b.name}  激活={b.gameObject.activeSelf}  " +
                              $"pos=({b.transform.position.x:0.#},{b.transform.position.y:0.#})  " +
                              $"血量={b.maxHealth}");
            foreach (BossGuan b in Object.FindObjectsOfType<BossGuan>(true))
                sb.AppendLine($"      BossGuan  {b.name}  激活={b.gameObject.activeSelf}  " +
                              $"pos=({b.transform.position.x:0.#},{b.transform.position.y:0.#})  " +
                              $"血量={b.maxHealth}");
            foreach (BossHuang b in Object.FindObjectsOfType<BossHuang>(true))
                sb.AppendLine($"      BossHuang {b.name}  激活={b.gameObject.activeSelf}  " +
                              $"pos=({b.transform.position.x:0.#},{b.transform.position.y:0.#})  " +
                              $"血量={b.maxHealth}");

            sb.AppendLine();
        }

        // ============================================================
        //  1 + 2：血条
        // ============================================================
        sb.AppendLine("===== 血条 =====");
        sb.AppendLine();

        string hudSrc = File.ReadAllText("Assets/Scripts/PlayerHealthBarUI.cs");
        sb.AppendLine("  PlayerHealthBarUI 的创建方式：");
        sb.AppendLine($"      RuntimeInitializeOnLoadMethod = " +
                      $"{(hudSrc.Contains("RuntimeInitializeOnLoadMethod") ? "有" : "没有")}");
        sb.AppendLine($"      DontDestroyOnLoad = " +
                      $"{(hudSrc.Contains("DontDestroyOnLoad") ? "有" : "**没有**")}");
        sb.AppendLine();
        sb.AppendLine("      ⚠ RuntimeInitializeOnLoadMethod 只在**游戏启动时跑一次**，");
        sb.AppendLine("        换场景不会再触发 —— 所以第 2 关起血条不会重建。");
        sb.AppendLine("        正确做法是监听 SceneManager.sceneLoaded。");

        string bossSrc = File.ReadAllText("Assets/Scripts/BossHealthBarUI.cs");
        sb.AppendLine();
        sb.AppendLine("  BossHealthBarUI：");
        sb.AppendLine($"      SetVisible 用的是 contentRoot = " +
                      $"{(bossSrc.Contains("contentRoot.gameObject.SetActive") ? "是" : "**不是**")}");
        sb.AppendLine($"      还在用 DontDestroyOnLoad = " +
                      $"{(bossSrc.Contains("DontDestroyOnLoad") ? "**是（换场景后会失效）**" : "否")}");

        File.WriteAllText("F:/HanziBulletHell/_bugsweep.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[排查]\n" + sb.ToString());
    }

    static string ComponentList(GameObject go)
    {
        var names = new List<string>();
        foreach (Component c in go.GetComponents<Component>())
        {
            if (c == null) { names.Add("**MissingScript**"); continue; }
            names.Add(c.GetType().Name);
        }
        return string.Join(", ", names.ToArray());
    }
}
#endif
