#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 教程层综合实测：
///   1. 小怪 / BOSS 的**运行时真实血量**（不是读序列化值，是真的把敌人生成出来）
///   2. 每个教程提示牌的位置、碰撞盒大小、是否覆盖了玩家进房的位置
///
/// 命令行：-executeMethod TutorialRuntimeProbe.Run
/// </summary>
public static class TutorialRuntimeProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 教程层运行时实测 ==========");
        sb.AppendLine();

        EditorSceneManager.OpenScene("Assets/Scenes/Level0.unity", OpenSceneMode.Single);

        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm == null) { sb.AppendLine("❌ 找不到 GameManager"); Done(sb); return; }

        sb.AppendLine("--- 0) 关键前提 ---");
        sb.AppendLine($"      GameManager.enemyTemplatePrefab = " +
                      $"{(gm.enemyTemplatePrefab != null ? gm.enemyTemplatePrefab.name : "**空**")}");
        sb.AppendLine($"      GameData.currentFloor = {GameData.currentFloor}   " +
                      $"(0 = 教程层)");
        sb.AppendLine($"      GameManager.FloorHealthMultiplier = {GameManager.FloorHealthMultiplier}");
        sb.AppendLine($"      GameBalance.EnemyHealthMultiplier = {GameBalance.EnemyHealthMultiplier}");
        sb.AppendLine($"      GameBalance.BossHealthMultiplier  = {GameBalance.BossHealthMultiplier}");
        sb.AppendLine();

        // ============================================================
        //  1) 真的把每个房间的怪生成出来，量血量
        // ============================================================
        sb.AppendLine("--- 1) 运行时真实血量 ---");

        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            // 玩家先进场（Enemy 的 Start 会找玩家）
            player.transform.position = new Vector3(-24f, 0f, 0f);
        }

        foreach (Room room in Object.FindObjectsOfType<Room>())
        {
            if (room.roomType != RoomType.Combat && room.roomType != RoomType.Elite &&
                room.roomType != RoomType.Boss)
                continue;

            sb.AppendLine($"      【{room.name}】类型={room.roomType}  " +
                          $"预制体={room.enemyPrefabs.Count}个  " +
                          $"override={room.enemyHealthOverride}  " +
                          $"数据库池={room.useDatabasePool}");

            // 战斗房：模拟真实刷怪（走 Room.SpawnOne 那一套）
            if (room.roomType == RoomType.Combat || room.roomType == RoomType.Elite)
            {
                if (gm.enemyTemplatePrefab == null)
                {
                    sb.AppendLine("          ✗ 没有模板预制体，房间刷不出怪！");
                    continue;
                }

                for (int i = 0; i < 2; i++)
                {
                    var go = Object.Instantiate(gm.enemyTemplatePrefab);
                    var e = go.GetComponent<Enemy>();
                    if (e == null) { Object.DestroyImmediate(go); continue; }

                    // 走房间真实的血量解析
                    room.SetupEnemy(go);
                    sb.AppendLine($"          样本{i + 1}：defId={e.defId}  实际 maxHealth = **{e.maxHealth}**");
                    Object.DestroyImmediate(go);
                }
            }

            // BOSS 房：量 BOSS
            if (room.roomType == RoomType.Boss)
            {
                foreach (GameObject p in room.enemyPrefabs)
                {
                    if (p == null) continue;
                    var go = Object.Instantiate(p);
                    var bs = go.GetComponent<BossShi>();
                    var bg = go.GetComponent<BossGuan>();
                    var bh = go.GetComponent<BossHuang>();

                    room.SetupEnemy(go);

                    if (bs != null)
                        sb.AppendLine($"          BossShi  实际 maxHealth = **{bs.maxHealth}**");
                    if (bg != null)
                        sb.AppendLine($"          BossGuan 实际 maxHealth = **{bg.maxHealth}**");
                    if (bh != null)
                        sb.AppendLine($"          BossHuang 实际 maxHealth = **{bh.maxHealth}**");

                    Object.DestroyImmediate(go);
                }
            }
            sb.AppendLine();
        }

        // ============================================================
        //  2) 提示牌
        // ============================================================
        sb.AppendLine("--- 2) 教程提示牌 ---");

        var hints = Object.FindObjectsOfType<TutorialHint>(true);
        sb.AppendLine($"      场景里共 {hints.Length} 个提示牌");
        sb.AppendLine();

        // 房间范围（用来判断碰撞盒覆盖了多大地方）
        var rooms = Object.FindObjectsOfType<Room>();

        foreach (TutorialHint h in hints)
        {
            var box = h.GetComponent<BoxCollider2D>();
            Vector3 p = h.transform.position;

            string boxInfo = box == null ? "**没有 BoxCollider2D**"
                : $"{box.size.x:0.#}×{box.size.y:0.#}" +
                  (box.isTrigger ? "" : "  **isTrigger = false，不会触发！**");

            sb.AppendLine($"      {h.name}  分组={h.group}  位置=({p.x:0.#},{p.y:0.#})  碰撞盒={boxInfo}");

            // 玩家出生点（教程层在 -24,0）
            if (box != null)
            {
                Bounds b = box.bounds;
                bool coversSpawn = b.Contains(new Vector3(-24f, 0f, 0f));
                if (coversSpawn)
                    sb.AppendLine("          ⚠ **覆盖了玩家出生点** —— " +
                                  "玩家一出生就在盒子里，OnTriggerEnter2D 不会触发，字幕不出来");
            }

            // 这个提示牌在哪几个房间里
            foreach (Room r in rooms)
            {
                if (r.roomType != RoomType.Combat && r.roomType != RoomType.Start &&
                    r.roomType != RoomType.Boss) continue;
                if (box != null && box.bounds.Contains(r.transform.position))
                    sb.AppendLine($"          覆盖房间：{r.name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- 3) 各分组有几条文案 ---");
        sb.AppendLine($"      Start   = {GameText.Tutorial.起始房().Length} 条");
        sb.AppendLine($"      Collect = {GameText.Tutorial.收集房().Length} 条");
        sb.AppendLine($"      Swap    = {GameText.Tutorial.换字房().Length} 条");
        sb.AppendLine($"      Boss    = {GameText.Tutorial.头目前().Length} 条");
        sb.AppendLine();
        sb.AppendLine("      各条内容：");
        foreach (string s in GameText.Tutorial.起始房())
            sb.AppendLine($"          Start  ：「{s}」{(string.IsNullOrEmpty(s) ? "  ⚠ **空字符串**" : "")}");
        foreach (string s in GameText.Tutorial.收集房())
            sb.AppendLine($"          Collect：「{s}」{(string.IsNullOrEmpty(s) ? "  ⚠ **空字符串**" : "")}");
        foreach (string s in GameText.Tutorial.换字房())
            sb.AppendLine($"          Swap    ：「{s}」{(string.IsNullOrEmpty(s) ? "  ⚠ **空字符串**" : "")}");
        foreach (string s in GameText.Tutorial.头目前())
            sb.AppendLine($"          Boss    ：「{s}」{(string.IsNullOrEmpty(s) ? "  ⚠ **空字符串**" : "")}");

        Done(sb);
    }

    static void Done(StringBuilder sb)
    {
        File.WriteAllText("F:/HanziBulletHell/_tutprobe.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[教程实测]\n" + sb.ToString());
    }
}
#endif
