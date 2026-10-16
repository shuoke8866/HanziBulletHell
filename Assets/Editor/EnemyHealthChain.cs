#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 实测：小怪从"数据库定义"到"游戏里真实血量"这一路上，值到底有没有掉。
///
/// 玩家反馈"只有 BOSS 召唤的小怪血量变了，房间刷的还是露头就秒"，
/// 这两条路径的差别就是房间刷怪多调了一次 Room.SetupEnemy。
/// 所以这里把每一步的值都打出来，看到底哪一步把它改坏了。
///
/// 命令行：-executeMethod EnemyHealthChain.Run
/// </summary>
public static class EnemyHealthChain
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 小怪血量链路实测 ==========");
        sb.AppendLine();

        EditorSceneManager.OpenScene("Assets/Scenes/Level1.unity", OpenSceneMode.Single);

        GameData.currentFloor = 1;

        // ---------- 1) 数据库里的值 ----------
        sb.AppendLine("--- 1) EnemyDatabase（编辑器里改的就是这里）---");
        foreach (EnemyDef d in EnemyDatabase.All)
            sb.AppendLine($"      {Pad(d.displayName, 12)} health = {d.health}");
        sb.AppendLine();

        // ---------- 2) GameManager 上的模板预制体 ----------
        GameManager gm = Object.FindObjectOfType<GameManager>();
        sb.AppendLine("--- 2) GameManager.enemyTemplatePrefab（房间刷怪用的模板）---");

        if (gm == null) { sb.AppendLine("      ❌ 找不到 GameManager"); }
        else if (gm.enemyTemplatePrefab == null)
        {
            sb.AppendLine("      ❌ enemyTemplatePrefab 是空的！");
            sb.AppendLine("         → 房间会退回去用 enemyPrefabs 或者干脆刷不出怪");
        }
        else
        {
            Enemy te = gm.enemyTemplatePrefab.GetComponent<Enemy>();
            sb.AppendLine($"      预制体名: {gm.enemyTemplatePrefab.name}");
            sb.AppendLine($"      组件上的 defId: {(te != null ? te.defId : "(没有 Enemy 组件)")}");
            sb.AppendLine($"      组件上的 maxHealth（**序列化在预制体上的值**）: " +
                          $"{(te != null ? te.maxHealth.ToString() : "-")}");

            if (te != null)
            {
                EnemyDef td = EnemyDatabase.Get(te.defId);
                sb.AppendLine($"      数据库里同 id 的 health: {(td != null ? td.health.ToString() : "(找不到这个 id)")}");

                if (td != null && td.health != te.maxHealth)
                {
                    sb.AppendLine();
                    sb.AppendLine($"      ⚠ **不一致**：数据库 {td.health} ≠ 预制体 {te.maxHealth}");
                    sb.AppendLine("         SetupEnemy 里算的是 预制体值 × 层数倍率，");
                    sb.AppendLine("         所以数据库改多少都不会影响房间刷出来的怪！");
                }
            }
        }
        sb.AppendLine();

        // ---------- 3) 真的调一次 Room.SetupEnemy，看最终血量 ----------
        //
        // 关键：这里**必须调用真实方法**，不能自己复刻一遍计算。
        // 上一版就是自己复刻的（probe 里写了一遍 maxHealth * mul），
        // 结果源码改了、probe 还是按旧逻辑算，显示"没修好" —— 白折腾一轮。
        sb.AppendLine("--- 3) 真实调用 Room.SetupEnemy 后的最终血量 ---");

        Room combatRoom = null;
        foreach (Room rr in Object.FindObjectsOfType<Room>())
            if (rr.roomType == RoomType.Combat) { combatRoom = rr; break; }

        if (combatRoom == null) sb.AppendLine("      ❌ 场景里没有战斗房");
        else if (gm == null || gm.enemyTemplatePrefab == null)
            sb.AppendLine("      ❌ 没有模板预制体");
        else
        {
            sb.AppendLine($"      用房间「{combatRoom.name}」当样本");
            sb.AppendLine();

            // 把数据库里前几种怪的 id 都试一遍 —— 房间里刷什么由数据库池决定，
            // 但模板预制体只有一个，所以重点验证"同一个模板 + 不同 defId"能不能出不同血量
            var tryIds = new List<string>();
            int shown = 0;
            foreach (EnemyDef d in EnemyDatabase.All)
            {
                if (shown++ >= 6) break;
                tryIds.Add(d.id);
            }

            foreach (string id in tryIds)
            {
                GameObject clone = Object.Instantiate(gm.enemyTemplatePrefab);
                Enemy e = clone.GetComponent<Enemy>();
                if (e == null) { Object.DestroyImmediate(clone); continue; }

                e.defId = id;
                e.EnsureDefinition();

                EnemyDef d = EnemyDatabase.Get(id);
                int raw = d != null ? d.health : -1;
                int expect = Mathf.RoundToInt(raw * GameBalance.EnemyHealthMultiplier);

                combatRoom.SetupEnemy(clone);

                sb.AppendLine($"      defId={Pad(id, 12)} 数据库={Pad(raw.ToString(), 5)}" +
                              $" ×{GameBalance.EnemyHealthMultiplier} = {Pad(expect.ToString(), 5)}" +
                              $" → 实际={Pad(e.maxHealth.ToString(), 5)}" +
                              (e.maxHealth == expect ? "✓" : "✗ 对不上！"));

                Object.DestroyImmediate(clone);
            }
        }
        sb.AppendLine();

        // ---------- 3.5) 四条生成路径横向对比 ----------
        sb.AppendLine("--- 3.5) 四条生成路径对比（同一只怪应该得到同一个血量）---");
        if (gm != null && gm.enemyTemplatePrefab != null)
        {
            EnemyDef sample = EnemyDatabase.Get("yi");
            int want = Enemy.ScaledHealth(sample.health);
            sb.AppendLine($"      目标：横线怪(yi) 数据库 {sample.health} × " +
                          $"{GameBalance.EnemyHealthMultiplier} = {want}");
            sb.AppendLine();

            // 路径 A：房间 SetupEnemy
            {
                var go = Object.Instantiate(gm.enemyTemplatePrefab);
                var e = go.GetComponent<Enemy>();
                e.defId = "yi";
                if (combatRoom != null) combatRoom.SetupEnemy(go);
                sb.AppendLine($"      A 房间刷怪（Room.SetupEnemy）      → {e.maxHealth}" +
                              (e.maxHealth == want ? "  ✓" : "  ✗"));
                Object.DestroyImmediate(go);
            }

            // 路径 B：只走 ApplyDefinition（BOSS 召唤 / 普通 Instantiate 都走这条）
            {
                var go = Object.Instantiate(gm.enemyTemplatePrefab);
                var e = go.GetComponent<Enemy>();
                e.defId = "yi";
                e.EnsureDefinition();
                e.ApplyDefinition();          // 不带 keepHealth
                sb.AppendLine($"      B 召唤/直接生成（ApplyDefinition）→ {e.maxHealth}" +
                              (e.maxHealth == want ? "  ✓" : "  ✗"));
                Object.DestroyImmediate(go);
            }

            // 路径 C：房间有显式 override（教程层用）—— 不该被 ×3 影响
            {
                var go = Object.Instantiate(gm.enemyTemplatePrefab);
                var e = go.GetComponent<Enemy>();
                e.defId = "yi";
                if (combatRoom != null)
                {
                    int saved = combatRoom.enemyHealthOverride;
                    combatRoom.enemyHealthOverride = 5;
                    combatRoom.SetupEnemy(go);
                    combatRoom.enemyHealthOverride = saved;
                }
                sb.AppendLine($"      C 房间覆盖 override=5              → {e.maxHealth}" +
                              (e.maxHealth == 5 ? "  ✓（覆盖优先，倍率不叠加）" : "  ✗"));
                Object.DestroyImmediate(go);
            }
        }
        sb.AppendLine();

        // ---------- 4) BOSS 召唤路径用的是什么预制体 ----------
        sb.AppendLine("--- 4) BOSS 召唤路径（玩家说这条是**对的**）---");
        foreach (Object o in Object.FindObjectsOfType(typeof(MonoBehaviour)))
        {
            var bg = o as BossGuan;
            if (bg != null)
                sb.AppendLine($"      BossGuan.summonPrefab = " +
                              $"{(bg.summonPrefab != null ? bg.summonPrefab.name : "(空)")}");

            var bh = o as BossHuang;
            if (bh != null)
            {
                sb.AppendLine($"      BossHuang.summonPrefab = " +
                              $"{(bh.summonPrefab != null ? bh.summonPrefab.name : "(空)")}");
                if (bh.summonPrefab != null)
                {
                    Enemy he = bh.summonPrefab.GetComponent<Enemy>();
                    sb.AppendLine($"          该预制体的 maxHealth = {(he != null ? he.maxHealth.ToString() : "-")}" +
                                  $"  defId = {(he != null ? he.defId : "-")}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- 5) 场景里房间都配了什么 ---");
        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            sb.AppendLine($"      {Pad(r.name, 14)} 类型={r.roomType}  " +
                          $"enemyPrefabs={r.enemyPrefabs.Count}个  " +
                          $"healthOverride={r.enemyHealthOverride}  " +
                          $"用数据库池={r.useDatabasePool}");
            foreach (GameObject p in r.enemyPrefabs)
                if (p != null) sb.AppendLine($"          · {p.name}");
        }

        File.WriteAllText("F:/HanziBulletHell/_hpchain.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[血量链路]\n" + sb.ToString());
    }

    static string Pad(string s, int n)
    {
        while (s.Length < n) s += " ";
        return s;
    }
}
#endif
