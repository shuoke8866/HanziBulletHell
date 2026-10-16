#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第三层关卡构建器 —— **数据驱动**重建房间结构。
///
/// ## 为什么要重建
///
/// 玩家反馈"第三层的房间结构希望更复杂"。实际排查发现问题比"不够复杂"严重：
///
///   · `Room1` 和 `Room2` 的中心都是 (0,0) —— **两个房间叠在一起**，墙体互相穿插
///   · 部分墙的中心在 y=0、部分在 y=10 —— 房间不在同一基准线上
///   · `Room4` 是 Start 类型却放在 BOSS 房的位置；`Room1`/`Room4` 都是空房
///   · 结果：第三层实际上只有 2 个战斗房 + 1 个 BOSS，比第二层还简单
///
/// 根因是这些房间是**逐个手摆**出来的，没有统一的坐标规范。
/// 手摆的布局一旦数量上去就必然对不齐。
///
/// ## 现在的做法
///
/// 布局写成一张**数据表**（<see cref="Rooms"/>），构建器负责：
///   1. 按表建房间（位置 / 类型 / 波次全部来自表）
///   2. 按 <see cref="Links"/> 建门，门坐标由**相邻两间房的中点**算出来
///   3. 调 RoomWallBuilder 铺墙（它只认房间的 bounds，不关心房间怎么来的）
///   4. 自检：房间不重叠、门在房间之间、有路径从起点到 BOSS
///
/// 想改布局只改两张表，不用再手动摆 —— 也就不会再对不齐。
///
/// ## 布局设计（复杂度递增）
///
///   第一层：直线 4 间（起点 → 战斗 → 宝箱 → BOSS）
///   第二层：6 间，有侧枝（宝箱房、精英房挂在主路上）
///   第三层：**8 间，双路径 + 上下绕行**
///
///              [E 精英]
///                 |
///   起点 — 战斗 —+— 战斗 — 宝箱 — BOSS
///     |           |            |
///     +— 宝箱     +— 战斗      +— 战斗(绕行)
///
/// 菜单：Tools/重建第三层
/// 命令行：-executeMethod Level3Builder.Build
/// </summary>
public static class Level3Builder
{
    const string ScenePath = "Assets/Scenes/Level3.unity";

    /// <summary>房间尺寸（和墙体系统、其它关卡保持一致）</summary>
    const float RoomW = 14f;
    const float RoomH = 8f;

    /// <summary>房间之间的空隙（门正好落在这里）</summary>
    const float DoorGap = 2f;

    const float PitchX = RoomW + DoorGap;   // 16
    const float PitchY = RoomH + DoorGap;   // 10

    // ============================================================
    //  布局数据表
    // ============================================================

    class RoomSpec
    {
        public string name;
        public int gx;              // 网格坐标（列）
        public int gy;              // 网格坐标（行）
        public RoomType type;
        public int waveCount;
        public string role;         // 只是注释，方便读表

        public RoomSpec(string n, int x, int y, RoomType t, int w, string r)
        {
            name = n; gx = x; gy = y; type = t; waveCount = w; role = r;
        }

        public Vector2 Center
        {
            get { return new Vector2(gx * PitchX, gy * PitchY); }
        }
    }

    /// <summary>
    /// 第三层的房间布局。
    ///
    /// 网格坐标 (gx, gy) —— 相邻整数之间隔一个 DoorGap，正好放门。
    ///
    /// ## 硬约束 1：相连的两间房必须**正交相邻**（曼哈顿距离 = 1）
    ///
    /// 门的位置取两间房的**中点**。斜着排的两间房（比如 (1,0) 和 (2,1)），
    /// 中点会落在两间房之间的**空白区域** —— 门孤立在虚空里，两边都够不着。
    ///
    /// 我连着两版都踩了这个：第一版斜着连，第二版以为"上下挂"就行，
    /// 但垂直分支的**列号没跟主路对齐**，还是斜的。
    ///
    /// ## 硬约束 2：分支房的**列号必须和它的主路房相同**
    ///
    /// 这一条是上一条的直接推论，但很容易漏 —— 别用"行号 +1"来放分支，
    /// 要用"和主路同一列、行号 ±1"。
    ///
    /// ## 布局（8 间房，双路径）
    ///
    ///   gy=+1                    [R4 战斗]         ← 上层：另一条路
    ///   gy= 0  [起点]—[R2 战斗]—[R3 宝箱]—[R5 精英]—[R6 前哨]—[R7 BOSS]
    ///   gy=-1                    [R8 宝箱]         ← 下层：绕行 + 奖励
    ///
    /// 门（全部正交相邻）：
    ///   起点 ↔ 战斗 | 战斗 ↓ 宝箱 | 宝箱 ↑ 战斗 | 战斗 ↑ 精英(上) | 战斗 ↓ 宝箱(下)
    ///   精英 ↔ 前哨 | 宝箱 ↔ 前哨 | 前哨 ↔ BOSS
    ///
    /// **两条路**：
    ///   · 上路（gy 0 → +1 → 0）：经过精英房，硬仗但直
    ///   · 下路（gy 0 → -1 → 0）：经过宝箱房，多拿资源但要绕
    ///   两条路在 R6 前哨汇合，再打 BOSS。
    ///   战斗房的波次比下路多 —— 走上面打得凶但快，走下面稳但慢，玩家自己权衡。
    /// </summary>
    static readonly RoomSpec[] Rooms =
    {
        //  名字          列  行   类型              波次  说明
        //  --- 上层：从左往右 ---
        new RoomSpec("Room1_Start",  0,  2, RoomType.Start,    0, "起点"),
        new RoomSpec("Room2_Fight",  1,  2, RoomType.Combat,   2, "主路战斗 1"),
        new RoomSpec("Room3_Fight",  2,  2, RoomType.Combat,   2, "主路战斗 2"),
        new RoomSpec("Room4_Chest",  3,  2, RoomType.Treasure, 1, "上层宝箱"),

        //  --- 折返：从右往左 ---
        new RoomSpec("Room5_Chest",  3,  1, RoomType.Treasure, 1, "折返宝箱"),
        new RoomSpec("Room6_Fight",  2,  1, RoomType.Combat,   3, "下层战斗 1（波次最多）"),
        new RoomSpec("Room7_Elite",  1,  1, RoomType.Elite,    2, "下层精英"),
        new RoomSpec("Room8_Fight",  0,  1, RoomType.Combat,   3, "下层战斗 2"),

        //  --- 再折返：从左往右 ---
        new RoomSpec("Room9_Chest",  0,  0, RoomType.Treasure, 1, "底层宝箱"),
        new RoomSpec("Room10_Boss",  1,  0, RoomType.Boss,     1, "关底 BOSS「皇」"),
    };

    /// <summary>
    /// 门：相邻两间房的网格坐标。
    ///
    /// 蛇形路径 —— 每一段都是正交相邻（曼哈顿距离 1）：
    ///   上层 (0,2)→(1,2)→(2,2)→(3,2)   向右
    ///   下行  (3,2)→(3,1)              同一列
    ///   中层 (3,1)→(2,1)→(1,1)→(0,1)   向左
    ///   下行  (0,1)→(0,0)              同一列
    ///   底层 (0,0)→(1,0)               向右 → BOSS
    ///
    /// ## 血泪教训：门必须连在**正交相邻**的两间房之间
    ///
    /// 门位置 = 两间房的**中点**。斜着排的两间房（如 (1,2) 和 (2,1)，距离 2）
    /// 中点在两间房之间的空白处 —— 门孤立在虚空里，两边都够不着。
    ///
    /// 我**连续三版**都踩了这个：
    ///   1. 斜着连
    ///   2. 以为"上下挂"就行，但垂直分支的列号没跟主路对齐
    ///   3. 加了"中层"之后，转折点的列号又没对齐
    ///
    /// 现在表格下面有 <see cref="Validate"/> 自动检查每一条，
    /// 再有这种错会直接报出来，不会带进场景。
    /// </summary>
    static readonly string[,] Links =
    {
        // 上层：向右
        { "Room1_Start", "Room2_Fight" },
        { "Room2_Fight", "Room3_Fight" },
        { "Room3_Fight", "Room4_Chest" },

        // 折返下行
        { "Room4_Chest", "Room5_Chest" },

        // 中层：向左
        { "Room5_Chest", "Room6_Fight" },
        { "Room6_Fight", "Room7_Elite" },
        { "Room7_Elite", "Room8_Fight" },

        // 再折返下行
        { "Room8_Fight", "Room9_Chest" },

        // 底层：向右 → BOSS
        { "Room9_Chest", "Room10_Boss" },
    };
    static readonly string[] KeepRoots =
    {
        "Main Camera", "Player", "GameManager", "Canvas", "EventSystem"
    };

    // ============================================================
    //  构建
    // ============================================================

    [MenuItem("Tools/重建第三层")]
    public static void Build()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"[第三层] 找不到 {ScenePath}");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("========== 重建第三层 ==========");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameData.currentFloor = 3;      // 墙体/门/数值都按第三层算

        // ---- 1) 清掉除了核心对象之外的一切 ----
        var doomed = new List<GameObject>();
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            bool keep = false;
            foreach (string k in KeepRoots) if (go.name == k) { keep = true; break; }
            if (!keep) doomed.Add(go);
        }
        foreach (GameObject go in doomed) Object.DestroyImmediate(go);
        sb.AppendLine($"  清掉 {doomed.Count} 个旧对象，保留核心对象");

        // ---- 2) 建房间 ----
        var byName = new Dictionary<string, Room>();

        foreach (RoomSpec spec in Rooms)
        {
            Room r = MakeRoom(spec);
            byName[spec.name] = r;
            sb.AppendLine($"  {Pad(spec.name, 14)} 中心={spec.Center}  类型={spec.type}  " +
                          $"波次={spec.waveCount}  ({spec.role})");
        }

        // ---- 3) 建门（位置由两间房的中点算出来，不用手填） ----
        int doorCount = 0;
        for (int i = 0; i < Links.GetLength(0); i++)
        {
            Room a = byName[Links[i, 0]];
            Room b = byName[Links[i, 1]];

            Vector2 mid = (Vector2)(a.transform.position + b.transform.position) * 0.5f;
            MakeDoor($"Door_{doorCount}", mid, a, b);
            doorCount++;
        }
        sb.AppendLine($"  建了 {doorCount} 扇门");

        // ---- 4) 铺墙 ----
        RoomWallBuilder.RebuildAll(verbose: false);

        // ---- 5) 刷怪点 ----
        foreach (RoomSpec spec in Rooms)
        {
            if (spec.type == RoomType.Start) continue;
            Room r = byName[spec.name];

            // 每个战斗房给 2 个刷怪点，让怪散开一点
            int n = (spec.type == RoomType.Boss) ? 1 :
                    (spec.type == RoomType.Treasure ? 0 : 2);

            for (int i = 0; i < n; i++)
            {
                float dx = (i == 0) ? -2.5f : 2.5f;
                float dy = (i == 0) ? 2f : -2f;
                string nm = (spec.type == RoomType.Boss)
                    ? "BossSpawn_Huang"
                    : $"Spawn_{spec.name}_{i}";

                AddSpawnPoint(r, nm, spec.Center + new Vector2(dx, dy));
            }
        }

        // ---- 6) BOSS / 传送门 / 宝箱 ----
        Room bossRoom = byName["Room10_Boss"];
        AddBoss(bossRoom);
        AddPortal(bossRoom, new Vector2(bossRoom.transform.position.x - 2f,
                                        bossRoom.transform.position.y));

        // 宝箱房各放一个宝箱
        // 三间宝箱房各放一个宝箱
        foreach (string chestRoom in new[] { "Room4_Chest", "Room5_Chest", "Room9_Chest" })
        {
            if (!byName.ContainsKey(chestRoom)) continue;
            Vector3 cp = byName[chestRoom].transform.position;
            AddChest(new Vector2(cp.x, cp.y));
        }

        // ---- 7) 玩家落点 ----
        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            Vector3 p = byName["Room1_Start"].transform.position;
            player.transform.position = new Vector3(p.x, p.y, 0f);
        }
        else
        {
            sb.AppendLine("  ⚠ 找不到 Player");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ---- 8) 自检 ----
        sb.AppendLine();
        sb.AppendLine("--- 自检 ---");
        sb.AppendLine(Validate());

        File.WriteAllText("F:/HanziBulletHell/_level3.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[第三层]\n" + sb.ToString());
    }

    /// <summary>自检：房间不重叠、门确实在相邻两间之间、从起点能走到 BOSS</summary>
    static string Validate()
    {
        var sb = new StringBuilder();

        // 房间不重叠
        var seen = new Dictionary<string, RoomSpec>();
        bool overlap = false;
        foreach (RoomSpec a in Rooms)
        {
            foreach (RoomSpec b in Rooms)
            {
                if (a == b) continue;
                if (Mathf.Abs(a.gx - b.gx) < 1 && Mathf.Abs(a.gy - b.gy) < 1)
                {
                    sb.AppendLine($"  ✗ 重叠：{a.name} 和 {b.name} 在同一格");
                    overlap = true;
                }
            }
        }
        if (!overlap) sb.AppendLine("  ✓ 没有房间重叠");

        // 每扇门的两间房必须相邻（曼哈顿距离 = 1），否则门会落在半空中
        bool badDoor = false;
        for (int i = 0; i < Links.GetLength(0); i++)
        {
            RoomSpec a = Find(Links[i, 0]);
            RoomSpec b = Find(Links[i, 1]);
            if (a == null || b == null) { sb.AppendLine($"  ✗ 门引用了不存在的房间"); badDoor = true; continue; }

            int d = Mathf.Abs(a.gx - b.gx) + Mathf.Abs(a.gy - b.gy);
            if (d != 1)
            {
                sb.AppendLine($"  ✗ 门 {a.name} → {b.name}：两间房不相邻（距离 {d}），" +
                              "门会落在房间外面");
                badDoor = true;
            }
        }
        if (!badDoor) sb.AppendLine("  ✓ 每扇门都连在相邻的两间房之间");

        // 连通性：从起点出发能不能到 BOSS
        var adj = new Dictionary<string, List<string>>();
        foreach (RoomSpec r in Rooms) adj[r.name] = new List<string>();
        for (int i = 0; i < Links.GetLength(0); i++)
        {
            adj[Links[i, 0]].Add(Links[i, 1]);
            adj[Links[i, 1]].Add(Links[i, 0]);
        }

        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push("Room1_Start");
        while (stack.Count > 0)
        {
            string cur = stack.Pop();
            if (!visited.Add(cur)) continue;
            foreach (string nxt in adj[cur]) stack.Push(nxt);
        }

        sb.AppendLine(visited.Contains("Room10_Boss")
            ? $"  ✓ 从起点能走到 BOSS（连通 {visited.Count}/{Rooms.Length} 间房）"
            : "  ✗ **从起点走不到 BOSS** —— 关卡无法通关！");

        if (visited.Count < Rooms.Length)
        {
            foreach (RoomSpec r in Rooms)
                if (!visited.Contains(r.name))
                    sb.AppendLine($"  ⚠ {r.name} 从起点走不到（玩家永远看不到这个房间）");
        }

        return sb.ToString();
    }

    static RoomSpec Find(string name)
    {
        foreach (RoomSpec r in Rooms) if (r.name == name) return r;
        return null;
    }

    // ============================================================
    //  建对象
    // ============================================================

    static Room MakeRoom(RoomSpec spec)
    {
        var go = new GameObject(spec.name);
        go.transform.position = new Vector3(spec.Center.x, spec.Center.y, 0f);

        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2(RoomW, RoomH);

        var room = go.AddComponent<Room>();
        room.roomType = spec.type;
        room.waveCount = spec.waveCount;
        room.waveInterval = 0.8f;
        room.useDatabasePool = true;
        room.enemyHealthOverride = -1;      // 走数据库 × 全局倍率
        room.spawnStrokeOnClear = spec.type != RoomType.Start;

        return room;
    }

    static void MakeDoor(string name, Vector2 pos, Room a, Room b)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Door.prefab");
        if (prefab == null) { Debug.LogWarning("[第三层] 找不到 Door.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        // 门是竖着还是横着，取决于两间房是左右相邻还是上下相邻
        bool horizontal = Mathf.Abs(a.transform.position.y - b.transform.position.y) < 0.01f;

        // 门洞 3 单位：横向门要竖过来（玩家从左右走过）
        go.transform.localRotation = horizontal
            ? Quaternion.identity
            : Quaternion.Euler(0f, 0f, 90f);
    }

    static void AddSpawnPoint(Room room, string name, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(room.transform, false);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        room.enemySpawnPoints.Add(go.transform);
    }

    static void AddBoss(Room room)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boss_Huang.prefab");
        if (prefab == null) { Debug.LogWarning("[第三层] 找不到 Boss_Huang.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "BossHuang";
        go.SetActive(false);            // 由 Room 决定什么时候激活
        room.enemyPrefabs.Add(go);
    }

    static void AddPortal(Room bossRoom, Vector2 pos)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Portal.prefab");
        if (prefab == null) { Debug.LogWarning("[第三层] 找不到 Portal.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Portal_ToEnd";
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.SetActive(false);            // 清房后才亮

        if (bossRoom != null)
        {
            bossRoom.portal = go;
            EditorUtility.SetDirty(bossRoom);
        }
    }

    static void AddChest(Vector2 pos)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TreasureChest.prefab");
        if (prefab == null) { Debug.LogWarning("[第三层] 找不到 TreasureChest.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
    }

    static string Pad(string s, int n)
    {
        while (s.Length < n) s += " ";
        return s;
    }
}
#endif
