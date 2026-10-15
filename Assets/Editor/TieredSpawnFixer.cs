#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 让战斗房真正走"分层随机刷怪"，而不是循环用固定预制体。
///
/// ## 问题
///
/// Room.SpawnOne 里有两条互斥的路径：
///
///     if (enemyPrefabs.Count == 0 &amp;&amp; useDatabasePool)   // 路径 A：数据库随机池
///         EnemyDatabase.PickRandom(...)
///     if (enemyPrefabs.Count > 0)                         // 路径 B：循环用配好的预制体
///         SpawnPrefab(enemyPrefabs[index % count], pos)
///
/// 战斗房**同时**配了 useDatabasePool=1 和 3~4 个 enemyPrefabs
/// （都是 Enemy_Ren/Kou/Shu/Yi 这几个基础怪），于是永远走路径 B，
/// 数据库池（以及按层分层的 minFloor 机制）**从来没被执行过**。
/// 表现就是：不管打到第几层，小怪种类都一模一样。
///
/// ## 修法（两步，缺一不可）
///
/// 1) 给 GameManager 设好 enemyTemplatePrefab
///    —— 现在三个场景都是空的。清空 enemyPrefabs 之后如果这个也没设，
///       ResolveEnemyTemplate() 返回 null，会变成"一只怪都不刷"。
/// 2) 清空**战斗房 / 精英房**的 enemyPrefabs
///    —— 让它们落到路径 A，由数据库按层抽。
///       BOSS 房的预制体**保留**（Boss_Shi/Guan/Huang 在数据库里没有定义，
///       靠预制体上的 BossShi/BossGuan/BossHuang 组件驱动）。
///
/// 用 Enemy_Bing 当模板：它对所有 defId 都是中性的，
/// Enemy.Start() 里的 ApplyDefinition() 会按数据库定义重建
/// 体型/颜色/字形/血量/行为，模板只提供"骨架"。
///
/// 菜单：Tools/修复分层刷怪配置
/// 命令行：-executeMethod TieredSpawnFixer.FixAll
/// </summary>
public static class TieredSpawnFixer
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    const string TemplatePrefabPath = "Assets/Enemy_Bing.prefab";

    [MenuItem("Tools/修复分层刷怪配置")]
    public static void FixAll()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
        if (template == null)
        {
            Debug.LogError($"[刷怪] 找不到模板预制体 {TemplatePrefabPath}");
            return;
        }

        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[刷怪] 场景不存在，跳过：{path}");
                continue;
            }

            Fix(path, template);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[刷怪] 全部场景处理完毕");
    }

    static void Fix(string scenePath, GameObject template)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        string file = Path.GetFileName(scenePath);

        // ---- 1) GameManager.enemyTemplatePrefab ----
        int gmFixed = 0;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            var gm = go.GetComponent<GameManager>();
            if (gm == null) gm = go.GetComponentInChildren<GameManager>(true);
            if (gm == null) continue;

            if (gm.enemyTemplatePrefab == null)
            {
                gm.enemyTemplatePrefab = template;
                EditorUtility.SetDirty(gm);
                gmFixed++;
            }
        }

        // ---- 2) 清空战斗房 / 精英房的 enemyPrefabs ----
        int cleared = 0;
        var keptBoss = new List<string>();

        foreach (Room room in Object.FindObjectsOfType<Room>())
        {
            // BOSS 房必须保留预制体：Boss_Shi / Boss_Guan / Boss_Huang
            // 在数据库里没有对应定义，靠预制体上的专属组件驱动。
            if (room.roomType == RoomType.Boss)
            {
                if (room.enemyPrefabs.Count > 0)
                    keptBoss.Add($"{room.name}({room.enemyPrefabs.Count})");
                continue;
            }

            if (room.enemyPrefabs.Count == 0) continue;

            room.enemyPrefabs.Clear();
            room.useDatabasePool = true;      // 确保落到数据库池那条路
            EditorUtility.SetDirty(room);
            cleared++;
        }

        Debug.Log($"[刷怪] {file}：GameManager 模板设置 {gmFixed} 处；" +
                  $"清空 {cleared} 个房间的 enemyPrefabs；" +
                  $"保留 BOSS 房 {keptBoss.Count} 个 [{string.Join(", ", keptBoss)}]");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
#endif
