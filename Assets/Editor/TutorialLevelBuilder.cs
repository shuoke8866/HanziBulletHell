#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 生成新手教程层 Level0。
///
/// ## 为什么要有这一层
///
/// 游戏一上来就是第一层，玩家完全不知道：WASD 走、鼠标瞄准、
/// 捡笔画会自动合成、数字键能换字、E 开背包。
/// 原来的第一层同时承担"教学"和"正式关卡"两个角色，结果两头都不讨好：
/// 老手嫌开场啰嗦，新手直接被打死。
///
/// ## 这一层教什么
///
///   Room1 起始  走位 + 射击          （一只慢速怪陪练）
///   Room2 收藏  捡笔画 + 自动合成     （2 只弱怪 + 3 个笔画）
///   Room3 换字  数字键换字 + E 背包   （2 只怪 + 1 个宝箱）
///   Room4 BOSS  四字合体"皇" + 传送门
///
/// 房间里的提示牌（TutorialHint）是走进房间就弹出来的，
/// 用大白话写，不像说明书。
///
/// 菜单：Tools/生成新手教程层
/// 命令行：-executeMethod TutorialLevelBuilder.Build
/// </summary>
public static class TutorialLevelBuilder
{
    const string SourceScene = "Assets/Scenes/Level1.unity";
    const string TargetScene = "Assets/Scenes/Level0.unity";

    // 房间尺寸与 Level1 保持一致，避免玩家一进教程就觉得"手感不一样"
    const float RoomW = 14f;
    const float RoomH = 8f;

    [MenuItem("Tools/生成新手教程层")]
    public static void Build()
    {
        if (!File.Exists(SourceScene))
        {
            Debug.LogError($"[教程] 找不到基准场景 {SourceScene}");
            return;
        }

        // ---- 1) 以 Level1 为底（拿到 Player / Camera / GameManager / Canvas 的完整配置），
        //         另存为 Level0，再把关卡内容全部清掉 ----
        Scene scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        if (!EditorSceneManager.SaveScene(scene, TargetScene))
        {
            Debug.LogError($"[教程] 无法另存为 {TargetScene}");
            return;
        }

        scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        var keep = new HashSet<string> { "Main Camera", "Player", "GameManager", "Canvas", "EventSystem" };
        var doomed = new List<GameObject>();

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (keep.Contains(go.name)) continue;
            doomed.Add(go);
        }

        foreach (GameObject go in doomed)
            Object.DestroyImmediate(go);

        Debug.Log($"[教程] 清掉 {doomed.Count} 个旧对象，保留 {keep.Count} 类核心对象");

        // ---- 2) 建房间 ----
        //
        // 房间间距 = RoomW + DoorGap，和正式关卡保持一致：
        // 相邻两间之间只留 DoorGap 的空隙，门正好落在里面。
        // 之前把间距写成 24（= 房间 14 + 空隙 10），门就被放在
        // 10 单位空隙的正中，两边房间的墙都够不着，等于没有门。
        const float DoorGap = 2f;
        const float Pitch = RoomW + DoorGap;      // 14 + 2 = 16

        float x0 = -3f * Pitch * 0.5f;            // 四间房整体居中

        var rooms = new List<Room>();

        rooms.Add(MakeRoom("Room1_Start", new Vector2(x0 + Pitch * 0f, 0f), RoomType.Start, 0));
        rooms.Add(MakeRoom("Room2_Collect", new Vector2(x0 + Pitch * 1f, 0f), RoomType.Combat, 1));
        rooms.Add(MakeRoom("Room3_Swap", new Vector2(x0 + Pitch * 2f, 0f), RoomType.Combat, 1));
        rooms.Add(MakeRoom("Room4_Boss", new Vector2(x0 + Pitch * 3f, 0f), RoomType.Boss, 1));

        // ---- 3) 门（相邻房间共用同一扇，正好落在两间房之间的缝隙里） ----
        MakeDoor("Door_1to2", new Vector2(x0 + Pitch * 0.5f, 0f), rooms[0], rooms[1]);
        MakeDoor("Door_2to3", new Vector2(x0 + Pitch * 1.5f, 0f), rooms[1], rooms[2]);
        MakeDoor("Door_3to4", new Vector2(x0 + Pitch * 2.5f, 0f), rooms[2], rooms[3]);

        // ---- 4) 墙体 ----
        // 和正式关卡共用同一套规则：每个房间封成一整圈墙，
        // 门只是墙上 3 单位的小口。这样四个关卡的结构完全一致。
        RoomWallBuilder.RebuildAll(verbose: false);

        // ---- 5) 敌人与刷怪点 ----
        AddSpawnPoint(rooms[1], "Spawn_C1", new Vector2(x0 + Pitch * 1f - 2f, 2f));
        AddSpawnPoint(rooms[1], "Spawn_C2", new Vector2(x0 + Pitch * 1f + 2f, -2f));
        AddSpawnPoint(rooms[2], "Spawn_S1", new Vector2(x0 + Pitch * 2f - 2f, 2f));
        AddSpawnPoint(rooms[2], "Spawn_S2", new Vector2(x0 + Pitch * 2f + 2f, -2f));
        AddSpawnPoint(rooms[3], "BossSpawn", new Vector2(x0 + Pitch * 3f, 0f));

        // 教程关的怪要弱：血量砍到 40%，玩家不会一开始就死
        rooms[1].enemyHealthOverride = 4;
        rooms[1].waveCount = 1;
        rooms[2].enemyHealthOverride = 5;
        rooms[2].waveCount = 1;

        // ---- 6) 宝箱 / 笔画 ----
        AddStrokeChest(new Vector2(x0 + Pitch * 2f, 0f));

        // ---- 7) BOSS 与传送门 ----
        AddBoss(rooms[3], new Vector2(x0 + Pitch * 3f, 0f));
        AddPortal(new Vector2(x0 + Pitch * 3f - 2f, 0f));

        // ---- 8) 教程提示 ----
        AddHints(x0, Pitch);

        // ---- 9) 玩家落点 ----
        GameObject player = GameObject.Find("Player");
        if (player != null)
            player.transform.position = new Vector3(x0, 0f, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[教程] {TargetScene} 生成完成：4 个房间 / 3 扇门 / 1 个 BOSS / 1 个传送门");
    }

    // ============================================================
    //  房间
    // ============================================================

    static Room MakeRoom(string name, Vector2 center, RoomType type, int waveCount)
    {
        GameObject go = new GameObject(name);
        go.transform.position = new Vector3(center.x, center.y, 0f);

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(RoomW, RoomH);

        Room room = go.AddComponent<Room>();
        room.roomType = type;
        room.waveCount = waveCount;
        room.useDatabasePool = true;
        room.spawnStrokeOnClear = type != RoomType.Start;

        // 起始房/宝箱房不刷怪，Room.Start() 里会自己处理
        return room;
    }

    static void AddSpawnPoint(Room room, string name, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(room.transform, false);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        room.enemySpawnPoints.Add(go.transform);
    }

    // ============================================================
    //  门
    // ============================================================

    static void MakeDoor(string name, Vector2 pos, Room a, Room b)
    {
        GameObject go = new GameObject(name);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        // 门是竖着的（连接左右两个房间），尺寸和正式关卡统一：
        // 短边 = 墙厚，长边 = 门洞宽。具体数值由 RoomWallBuilder 决定，
        // 这里先给个占位，RebuildAll 会把它摆正。
        go.transform.localScale = new Vector3(RoomWallBuilder.DoorThickness,
                                              RoomWallBuilder.DoorSpan, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RoomWallBuilder.SquareSprite;
        sr.color = new Color(0.6f, 0.4f, 0.2f, 1f);
        sr.sortingOrder = 5;

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;

        Door door = go.AddComponent<Door>();
        door.doorSprite = sr;
        door.doorCollider = box;
        door.isOpen = true;

        go.AddComponent<DoorProbe>();

        // 相邻两个房间都登记这扇门：谁在打，门就关着
        a.doors.Add(door);
        b.doors.Add(door);
    }

    // ============================================================
    //  宝箱 / BOSS / 传送门
    // ============================================================

    static void AddStrokeChest(Vector2 pos)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TreasureChest.prefab");
        if (prefab == null) { Debug.LogWarning("[教程] 找不到 TreasureChest.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.name = "TutorialChest";
    }

    /// <summary>
    /// 放进 BOSS 房的 BOSS。
    ///
    /// **不要**再在场景里额外预放一只，否则会变成两只。
    ///
    /// 这里必须走 enemyPrefabs 让房间自己 Instantiate，而不是"预放在场景里"：
    /// Room.SetupEnemy 会对生成的每一只调用 bossScript.SetRoom(this)，
    /// 而预放的那只永远不会被调用到（Room.Start 并不接管场景里已有的敌人）。
    /// SetRoom 没被调用的后果是：BOSS 死时 currentRoom 为 null，
    /// 不会通知房间 OnEnemyDied → 房间永远清不掉 → 门永久关闭、玩家被困。
    ///
    /// 数量：GetBaseEnemyCount() 在"1 个刷怪点 + 1 个 prefab"时返回 1，
    /// 教程层 ExtraEnemyCount 又是 0，所以正好刷 1 只。
    /// </summary>
    static void AddBoss(Room room, Vector2 pos)
    {
        // 教程 BOSS 用「士」：只有横斩和远程，招式最少、最好读
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boss_Shi 1.prefab");
        if (prefab == null) { Debug.LogWarning("[教程] 找不到 Boss_Shi 1.prefab"); return; }

        room.enemyPrefabs.Add(prefab);
        room.waveCount = 1;
    }

    static void AddPortal(Vector2 pos)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Portal.prefab");
        if (prefab == null) { Debug.LogWarning("[教程] 找不到 Portal.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.name = "Portal_ToLevel1";
    }

    // ============================================================
    //  教程提示
    // ============================================================

    static void AddHint(string name, Vector2 pos, Vector2 size, params string[] lines)
    {
        GameObject go = new GameObject(name);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;

        TutorialHint hint = go.AddComponent<TutorialHint>();
        hint.lines = new List<string>(lines);
        hint.lineDuration = 7f;
    }

    static void AddHints(float x0, float pitch)
    {
        // Room1：先教最基础的两件事，别的都先别说
        AddHint("Hint_Start", new Vector2(x0, 0f), new Vector2(RoomW - 1f, RoomH - 1f),
            "用 W A S D 走动。",
            "鼠标指着哪边，字就往哪边打。按住左键就能一直打。",
            "屏幕上飞来飞去的方块别硬接 —— 碰一下掉血，血空了就得重来。");

        // Room2：教收集与自动合成
        AddHint("Hint_Collect", new Vector2(x0 + pitch * 1f, 0f), new Vector2(RoomW - 2f, RoomH - 1f),
            "打倒这些家伙，地上会掉笔画。走过去就自动捡。",
            "笔画攒够了，你的字会自己变成更复杂的字 —— 字越复杂，打得越狠。",
            "顺手按 F 把宝箱开了，里面有笔画。");

        // Room3：教换字与背包
        AddHint("Hint_Swap", new Vector2(x0 + pitch * 2f, 0f), new Vector2(RoomW - 1f, RoomH - 1f),
            "屏幕底下那排字，按 1-9 就能换过去。",
            "打不动了就换个大字试试，火力完全不一样。",
            "按 E 打开背包，A / D 翻页，里面是你现在能拼出来的所有字。",
            "换错了也不要紧，笔画不会丢，随时能换回来。");

        // Room4：BOSS 前的最后一句
        AddHint("Hint_Boss", new Vector2(x0 + pitch * 2.5f, 0f), new Vector2(6f, RoomH - 1f),
            "前面是个难缠的家伙，注意躲它那道横扫的刀光。",
            "打赢了会出现传送门 —— 走过去按 F，就正式开始了。");
    }

    // ============================================================
    //  工具
    // ============================================================

}
#endif
