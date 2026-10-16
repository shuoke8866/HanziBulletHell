#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
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

        // 教程关的小怪血量：**和第一层完全一样**（不设 override，走数据库 × 全局倍率）。
        //
        // 踩过两次坑，都记下来：
        //
        //   1. 原来写死 enemyHealthOverride = 4 / 5，
        //      而其他层是 `数据库24 × 全局倍率3 = 72` → 差 18 倍，小怪一碰就没。
        //
        //   2. 我第一次修的时候按"教程应该更简单"改成了 24 / 30 —— **还是错的**。
        //
        // 为什么"教程更简单"这个直觉本身就不对：
        // 教程层起始房摆了六种笔画的展台，玩家**一进教程就能拿全六种笔画**，
        // 打完教程出来时反而比"刚进第一层的玩家"强得多。
        // 敌人却给得比第一层弱，难度曲线在这里凹下去 ——
        // 玩家的体感就是"教程的怪是纸糊的"。
        //
        // 结论：**难度要跟着玩家的战力走，不是跟着关卡序号走。**
        // 这里干脆不设 override，让教程层和第一层用同一套规则。
        rooms[1].enemyHealthOverride = -1;
        rooms[1].waveCount = 1;
        rooms[2].enemyHealthOverride = -1;
        rooms[2].waveCount = 1;

        // ---- 6) 宝箱 / 笔画 ----
        AddStrokeChest(new Vector2(x0 + Pitch * 2f, 0f));

        // 教程的核心任务之一是"让玩家认全六种笔画"。
        // 所以在起始房正中间摆一排六张展台，每种笔画一个，
        // 玩家一进场就能一眼看全：横、竖、撇、捺、点、折各长什么样。
        AddStrokeGallery(new Vector2(x0, 0f));

        // ---- 7) BOSS 与传送门 ----
        AddBoss(rooms[3], new Vector2(x0 + Pitch * 3f, 0f));
        AddPortal(rooms[3], new Vector2(x0 + Pitch * 3f - 2f, 0f));

        // ---- 7.5) 开场游戏介绍 ----
        // 挂在场景里一个空对象上；它自己只在教程层弹一次。
        // 文案在 GameText.Intro.正文()，改完不用重新生成场景。
        GameObject intro = GameObject.Find("__OpeningIntro");
        if (intro == null) intro = new GameObject("__OpeningIntro");
        if (intro.GetComponent<OpeningIntroUI>() == null)
            intro.AddComponent<OpeningIntroUI>();

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

    /// <summary>
    /// 在房间正中摆一排"笔画展台"：六种基本笔画各一个，附名字，
    /// **同时在展台旁边放一份真的可拾取笔画**。
    ///
    /// 展台本身只是贴图（让玩家看清有哪六种、分别叫什么），
    /// 真正能捡的是旁边那个 StrokePickup —— 两者成对出现，
    /// 玩家看到"哦，这个形状的笔画叫横"，走过去顺手就捡了。
    ///
    /// 为什么不做成"捡起来才认识"：
    /// 教程要给的是**认知**，得先看清全部六种再谈选择。
    /// 但也不能只给看不给捡 —— 那样教程里玩家空着手，什么也做不了。
    /// </summary>
    static void AddStrokeGallery(Vector2 center)
    {
        var all = new[]
        {
            StrokeType.Heng, StrokeType.Shu, StrokeType.Pie,
            StrokeType.Na, StrokeType.Dian, StrokeType.Zhe
        };

        const float spacing = 2.0f;
        float startX = -(all.Length - 1) * 0.5f * spacing;

        GameObject gallery = new GameObject("StrokeGallery");
        gallery.transform.position = new Vector3(center.x, center.y + 0.6f, 0f);

        GameObject pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/StrokePickup.prefab");
        if (pickupPrefab == null)
            Debug.LogWarning("[教程] 找不到 StrokePickup.prefab，展台旁边不会有可捡的笔画");

        for (int i = 0; i < all.Length; i++)
        {
            StrokeType st = all[i];
            Vector3 pos = new Vector3(center.x + startX + i * spacing,
                                      center.y + 0.6f, 0f);

            // ---- 展台：字形 + 名字（只看不捡）----
            GameObject slot = new GameObject($"Gallery_{st}");
            slot.transform.SetParent(gallery.transform, false);
            slot.transform.position = pos;

            GameObject gl = new GameObject("Glyph");
            gl.transform.SetParent(slot.transform, false);
            gl.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = gl.AddComponent<SpriteRenderer>();
            sr.sprite = HanziRenderer.CreateSpriteFor(
                GameData.HanziForSingleStroke(st), 96, 48f);
            sr.color = StrokeUtil.ColorOf(st);
            sr.sortingOrder = 6;
            gl.transform.localScale = Vector3.one * 0.9f;

            GameObject txt = new GameObject("Name");
            txt.transform.SetParent(slot.transform, false);
            txt.transform.localPosition = new Vector3(0f, -0.85f, -0.1f);

            var tm = txt.AddComponent<TextMeshPro>();
            tm.text = StrokeUtil.NameOf(st);
            tm.fontSize = 3f;
            tm.alignment = TextAlignmentOptions.Center;
            tm.color = StrokeUtil.ColorOf(st);
            tm.sortingOrder = 7;

            // ---- 可拾取笔画：放在展台正下方一格 ----
            if (pickupPrefab != null)
            {
                GameObject pick = (GameObject)PrefabUtility.InstantiatePrefab(pickupPrefab);
                pick.name = $"TutorialPickup_{st}";
                pick.transform.position = pos + new Vector3(0f, -1.9f, 0f);

                StrokePickup sp = pick.GetComponent<StrokePickup>();
                if (sp != null) sp.strokeType = st;
            }
        }

        Debug.Log($"[教程] 笔画展台 + 可拾取笔画已放置：{all.Length} 种");
    }

    /// <summary>
    /// 在 BOSS 房里放一个传送门，**初始隐藏**，并挂到房间的 portal 字段上。
    ///
    /// 为什么要绑到房间、而不是让它一开始就亮着：
    ///   · 绑上去之后 GameManager 就知道"这个房间已经有门了"，不会再生成一个
    ///     （原来两个来源各生成一个 → BOSS 房出现两个传送门）
    ///   · 先隐藏是为了不让玩家**跳过 BOSS 战** —— 清房时 Room.ActivatePortal 才亮
    /// </summary>
    static void AddPortal(Room bossRoom, Vector2 pos)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Portal.prefab");
        if (prefab == null) { Debug.LogWarning("[教程] 找不到 Portal.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.name = "Portal_ToLevel1";

        // 一开始藏起来，打完 BOSS 才出现
        go.SetActive(false);

        if (bossRoom != null)
        {
            bossRoom.portal = go;
            EditorUtility.SetDirty(bossRoom);
        }
    }

    // ============================================================
    //  教程提示
    // ============================================================

    /// <summary>
    /// 放一个教程提示牌。
    ///
    /// **不再把文案烤进场景**，只记一个分组 —— TutorialHint 运行时
    /// 从 GameText.Tutorial 现取。这样改文案立刻生效，不用重新生成教程层。
    /// （原来把文案序列化进场景，改了 GameText 却看不到变化，很容易误判工具失效。）
    /// </summary>
    static void AddHint(string name, Vector2 pos, Vector2 size, TutorialHint.TutorialGroup group)
    {
        GameObject go = new GameObject(name);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;

        TutorialHint hint = go.AddComponent<TutorialHint>();
        hint.group = group;
        hint.lineDuration = 7f;
    }

    static void AddHints(float x0, float pitch)
    {
        // 只指定分组；具体文案在 GameText.Tutorial 里，
        // 用 Tools/字幕编辑器 或直接改 GameText.cs 都能立刻生效。

        // Room1：先认全六种笔画 —— 这是教程最主要的目标
        AddHint("Hint_Start", new Vector2(x0, 0f), new Vector2(RoomW - 1f, RoomH - 1f),
            TutorialHint.TutorialGroup.Start);

        // Room2：教收集与自动合成
        AddHint("Hint_Collect", new Vector2(x0 + pitch * 1f, 0f), new Vector2(RoomW - 2f, RoomH - 1f),
            TutorialHint.TutorialGroup.Collect);

        // Room3：教换字与背包
        AddHint("Hint_Swap", new Vector2(x0 + pitch * 2f, 0f), new Vector2(RoomW - 1f, RoomH - 1f),
            TutorialHint.TutorialGroup.Swap);

        // Room4：BOSS 前，顺便交代"教程和实战是分开的"
        AddHint("Hint_Boss", new Vector2(x0 + pitch * 2.5f, 0f), new Vector2(6f, RoomH - 1f),
            TutorialHint.TutorialGroup.Boss);
    }

    // ============================================================
    //  工具
    // ============================================================

}
#endif
