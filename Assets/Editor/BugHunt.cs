#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 诊断两个玩家反馈的 bug：
///   1. 第三层玩家开局卡墙里
///   2. 教程层过完背景故事后玩家血条消失
///
/// 命令行：-executeMethod BugHunt.Run
/// </summary>
public static class BugHunt
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== Bug 诊断 ==========");
        sb.AppendLine();

        // ============================================================
        //  Bug 1：第三层玩家卡墙
        // ============================================================
        sb.AppendLine("--- Bug 1：第三层玩家出生点 vs 墙体 ---");

        EditorSceneManager.OpenScene("Assets/Scenes/Level3.unity", OpenSceneMode.Single);

        GameObject player = GameObject.Find("Player");
        if (player == null) sb.AppendLine("  ❌ 找不到 Player");
        else
        {
            Vector3 pp = player.transform.position;
            sb.AppendLine($"  玩家位置 = ({pp.x:0.##}, {pp.y:0.##})");

            // 玩家碰撞体多大
            Collider2D pc = player.GetComponent<Collider2D>();
            if (pc != null)
                sb.AppendLine($"  玩家碰撞体 = {pc.GetType().Name}  bounds={pc.bounds.size}");

            // 出生点周围所有墙
            sb.AppendLine();
            sb.AppendLine("  出生点附近的墙（距离 < 12）：");
            int near = 0;
            foreach (Transform t in Object.FindObjectsOfType<Transform>())
            {
                if (!t.name.StartsWith("_Wall_")) continue;

                float d = Vector2.Distance(t.position, pp);
                if (d > 12f) continue;

                near++;
                var sr = t.GetComponent<SpriteRenderer>();
                string sz = sr != null ? $"{sr.bounds.size.x:0.##}×{sr.bounds.size.y:0.##}" : "?";

                // 出生点是不是在这个墙的范围内
                bool inside = sr != null && sr.bounds.Contains(pp);
                sb.AppendLine($"      {(inside ? "★ 玩家在它里面!" : "  ")} " +
                              $"{t.name,-26} pos=({t.position.x:0.#},{t.position.y:0.#})  " +
                              $"尺寸={sz}  距离={d:0.#}");
            }
            if (near == 0) sb.AppendLine("      （12 单位内没有墙）");

            // 玩家 Transform 所在的房间
            sb.AppendLine();
            foreach (Room r in Object.FindObjectsOfType<Room>())
            {
                float d = Vector2.Distance(r.transform.position, pp);
                if (d < 20f)
                    sb.AppendLine($"  附近房间 {r.name} 中心=({r.transform.position.x:0.#}," +
                                  $"{r.transform.position.y:0.#})  距离={d:0.#}  类型={r.roomType}");
            }
        }
        sb.AppendLine();

        // ============================================================
        //  Bug 2：教程层血条消失
        // ============================================================
        sb.AppendLine("--- Bug 2：玩家血条 ---");

        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);

        // PlayerHealthBarUI 是 DontDestroyOnLoad 的运行时对象，
        // 场景里查不到是正常的。查的是"它依赖的东西"有没有问题。
        PlayerHealth ph = null;
        GameObject p0 = GameObject.Find("Player");
        if (p0 != null) ph = p0.GetComponent<PlayerHealth>();

        if (ph == null) sb.AppendLine("  ❌ Player 上没有 PlayerHealth");
        else
        {
            sb.AppendLine($"  PlayerHealth: baseMaxHealth={ph.baseMaxHealth}  " +
                          $"maxHealth={ph.maxHealth}  currentHealth={ph.currentHealth}");
            float ratio = ph.maxHealth > 0 ? (float)ph.currentHealth / ph.maxHealth : 0f;
            sb.AppendLine($"  血量比例 = {ratio:0.###}");
            if (ratio <= 0.001f)
                sb.AppendLine("  ⚠ **currentHealth 是 0** —— 血条会显示成空的（看起来像消失了）");
        }

        // PlayerHealthBarUI 的自动创建方式
        sb.AppendLine();
        sb.AppendLine("  PlayerHealthBarUI 用 [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] + " +
                      "DontDestroyOnLoad 创建");
        sb.AppendLine("  它的 Update 里：if (ph == null || fill == null) return;");
        sb.AppendLine("  → 如果 PlayerHealth 在场景切换的某一帧被销毁，那一帧不更新，");
        sb.AppendLine("     但 static cached 会留着已销毁的对象，之后**永远返回 null**");

        // 检查 cached 的失效逻辑
        sb.AppendLine();
        sb.AppendLine("  关键代码检查：");
        string src = File.ReadAllText("Assets/Scripts/PlayerHealthBarUI.cs");
        bool hasNullCheck = src.Contains("if (cached != null) return cached;");
        sb.AppendLine($"      PlayerHealthBarUI: {(hasNullCheck ? "有" : "没有")} " +
                      "\"if (cached != null) return cached;\" 缓存判空");

        string src2 = File.ReadAllText("Assets/Scripts/OpeningIntroUI.cs");
        sb.AppendLine($"      OpeningIntroUI: Time.timeScale 恢复 = " +
                      $"{(src2.Contains("Time.timeScale = 1f;") ? "有" : "**没有**")}");

        // 血条的初始显示状态
        sb.AppendLine();
        sb.AppendLine("  PlayerHealthBarUI.Build 里 fill 的初始 anchorMax：");
        foreach (string line in src.Split('\n'))
            if (line.Contains("fr.anchorMax") || line.Contains("anchorMax = new Vector2(1f"))
                sb.AppendLine("      " + line.Trim());

        File.WriteAllText("F:/HanziBulletHell/_bughunt.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[Bug诊断]\n" + sb.ToString());
    }
}
#endif
