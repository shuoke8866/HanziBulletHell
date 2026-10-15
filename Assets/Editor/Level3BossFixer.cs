#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 修正第三关的 BOSS 配置：**关底 BOSS 是「皇」**。
///
/// 原来是两个 BOSS 房前后脚排在一起：
///   Room3 (24,10) → Boss_Huang（皇）
///   Room4 (40,10) → Boss_Guan （关）
///
/// 这有两个问题：
///   1. 房间触发器是互相重叠的，而 Room.AnotherCombatRoomActive 会在
///      "还有房间没打完"时拒绝后面的房间刷怪 —— 两个 BOSS 房连排，
///      后一个很可能根本不刷，玩家打完一个就卡在那儿等传送门。
///   2. 玩家打赢的到底是不是"关底 BOSS"完全看运气，体验很怪。
///
/// 改法：
///   · Room3 保留 Boss_Huang —— 它才是关底
///   · Room4 清成空房间（不刷怪、不放预制体），只作为放传送门的过道
///
/// 菜单：Tools/修正第三关BOSS
/// 命令行：-executeMethod Level3BossFixer.Fix
/// </summary>
public static class Level3BossFixer
{
    const string ScenePath = "Assets/Scenes/Level3.unity";

    /// <summary>关底 BOSS 放哪个房间</summary>
    const string BossRoomName = "Room3";

    /// <summary>清空成过道的房间</summary>
    const string CorridorRoomName = "Room4";

    [MenuItem("Tools/修正第三关BOSS")]
    public static void Fix()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"[BOSS] 找不到 {ScenePath}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject huang = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boss_Huang.prefab");
        if (huang == null)
        {
            Debug.LogError("[BOSS] 找不到 Assets/Boss_Huang.prefab");
            return;
        }

        int fixedRooms = 0;

        foreach (Room room in Object.FindObjectsOfType<Room>())
        {
            if (room.roomType != RoomType.Boss) continue;

            if (room.name == BossRoomName)
            {
                // 关底：只放皇
                room.enemyPrefabs.Clear();
                room.enemyPrefabs.Add(huang);
                room.waveCount = 1;
                room.enemyHealthOverride = -1;
                EditorUtility.SetDirty(room);

                Debug.Log($"[BOSS] 「{room.name}」设为关底 BOSS「皇」");
                fixedRooms++;
            }
            else if (room.name == CorridorRoomName)
            {
                // 过道：不刷怪，只留给传送门。
                //
                // roomType 必须一起改掉 —— 留成 Boss 的话，
                // Room 会认为"这是个战斗房但一只怪都没刷"，
                // roomCleared 永远是 false，门就永久关着了。
                // 改成 Start（非战斗房）后 Start() 里直接标记为已清理。
                room.roomType = RoomType.Start;
                room.enemyPrefabs.Clear();
                room.waveCount = 0;
                room.useDatabasePool = false;
                room.enemySpawnPoints.Clear();
                EditorUtility.SetDirty(room);

                Debug.Log($"[BOSS] 「{room.name}」改成过道（非战斗房，不刷怪）");
                fixedRooms++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[BOSS] Level3 处理完成，改动 {fixedRooms} 个房间");
    }
}
#endif
