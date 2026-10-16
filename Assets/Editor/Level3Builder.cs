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
    /// <summary>
    /// 第三层的房间布局 —— **坐标系统照抄 Level1**（已验证能玩）。
    ///
    /// ## 照抄的规格（从 Level1 实测出来的）
    ///
    ///   房间尺寸           14 × 8
    ///   横向间距 PitchX    16  （房间宽 14 + 间隙 2）
    ///   纵向间距 PitchY    10  （房间高 8 + 间隙 2）
    ///   门尺寸             恒为 0.5 × 3，**不旋转**
    ///   门的坐标           左右相邻 → x 取两房边界的中点（= 左房右边界 + 1）
    ///                      上下相邻 → 给门的 BoxCollider2D 设 size=(3, 0.5)，
    ///                                 并把整个门转 90°（Level2 的 Door_heng2-1 就是这么做的）
    ///
    /// Level1 实测：Room1 x[-15,-1] / Room2 x[1,15] / Room3 x[17,31] / Room4 x[33,47]，
    /// 门在 x=0 / 16 / 32 —— 即"间隙正中"，和我们算的一致。
    ///
    /// ## 布局（9 房，3×3 网格，蛇形）
    ///
    ///   gy=2   [起点] ── [战斗] ── [宝箱]
    ///                               │
    ///   gy=1   [战斗] ── [精英] ── [战斗]
    ///            │
    ///   gy=0   [宝箱] ── [BOSS]
    ///
    /// 相邻只差一格（横向差 1 列 或 纵向差 1 行），门的坐标就是确定值，不用猜。
    /// </summary>
    static readonly RoomSpec[] Rooms =
    {
        //  名字          列  行   类型              波次  说明
        new RoomSpec("Room1_Start",  0,  2, RoomType.Start,    0, "起点"),
        new RoomSpec("Room2_Fight",  1,  2, RoomType.Combat,   2, "上层战斗 1"),
        new RoomSpec("Room3_Chest",  2,  2, RoomType.Treasure, 1, "上层宝箱"),

        new RoomSpec("Room4_Fight",  2,  1, RoomType.Combat,   3, "中层战斗（波次最多）"),
        new RoomSpec("Room5_Elite",  1,  1, RoomType.Elite,    2, "中层精英"),
        new RoomSpec("Room6_Chest",  0,  1, RoomType.Treasure, 1, "中层宝箱"),

        new RoomSpec("Room7_Chest",  0,  0, RoomType.Treasure, 1, "下层宝箱"),
        new RoomSpec("Room8_Fight",  1,  0, RoomType.Combat,   3, "下层战斗"),
        new RoomSpec("Room9_Boss",   2,  0, RoomType.Boss,     1, "关底 BOSS「皇」"),
    };

    /// <summary>
    /// 门：只写"哪两间房相连"，坐标全部由构建器按 Level1 的规格算出来。
    ///
    /// 每条都必须是**正交相邻**（横向差 1 列、纵向差 0 行；或反之），
    /// 自检会检查 —— 斜着连的门会落在房间外面的空中。
    /// </summary>
    static readonly string[,] Links =
    {
        // 上层：从左往右
        { "Room1_Start", "Room2_Fight" },
        { "Room2_Fight", "Room3_Chest" },

        // 右侧下来一层
        { "Room3_Chest", "Room4_Fight" },

        // 中层：从右往左
        { "Room4_Fight", "Room5_Elite" },
        { "Room5_Elite", "Room6_Chest" },

        // 左侧再下来一层
        { "Room6_Chest", "Room7_Chest" },

        // 下层：从左往右 → BOSS
        { "Room7_Chest", "Room8_Fight" },
        { "Room8_Fight", "Room9_Boss" },
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

        // ---- 3) 建门 ----
        //
        // 坐标按 **Level1 的规格**算（实测过、能玩）：
        //   左右相邻 → 门在 x = 两房边界的中点，尺寸 0.5×3，旋转 0
        //   上下相邻 → 门在 y = 两房边界的中点，**碰撞盒改成 3×0.5** 并旋转 90°
        //             （Level2 的 Door_heng2-1 就是这么做的）
        //
        // 两间房中心差正好是 PitchX 或 PitchY，所以"中点"一定落在 2 单位的
        // 间隙正中，墙上的门洞（3 单位）也正好以它为中心。
        int doorCount = 0;
        for (int i = 0; i < Links.GetLength(0); i++)
        {
            Room a = byName[Links[i, 0]];
            Room b = byName[Links[i, 1]];

            Vector2 ca = a.transform.position;
            Vector2 cb = b.transform.position;

            bool horizontalPair = Mathf.Abs(ca.y - cb.y) < 0.01f;

            // 中点 = 两房中心的中点，正好在间隙正中
            Vector2 mid = (ca + cb) * 0.5f;

            MakeDoor($"Door_{doorCount}", mid, horizontalPair);
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
        Room bossRoom = byName["Room9_Boss"];
        AddBoss(bossRoom);
        AddPortal(bossRoom, new Vector2(bossRoom.transform.position.x - 2f,
                                        bossRoom.transform.position.y));

        // 宝箱房各放一个宝箱
        // 三间宝箱房各放一个宝箱
        foreach (string chestRoom in new[] { "Room3_Chest", "Room6_Chest", "Room7_Chest" })
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

        sb.AppendLine(visited.Contains("Room9_Boss")
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

        // **必须显式设 isTrigger** —— 不能指望 Room.Reset() 帮我们设。
        //
        // 踩过的坑（玩家反馈"第三层开局卡墙里"）：
        // Room.Reset() 里有一句 `box.isTrigger = true`，看起来"加了组件就是触发器"。
        // 但 Unity 的 Reset() **只在"通过 Inspector 添加组件"时才会被自动调用** ——
        // 脚本里 AddComponent **不会**触发它。
        //
        // 其它三层的房间是手动在编辑器里建的 → Reset 跑了 → isTrigger = true ✓
        // 第三层是用这个脚本建的 → Reset 没跑 → isTrigger 保持默认 false ✗
        // 结果玩家一出生就在一个**实心盒子**里，被自己的房间卡住动不了。
        box.isTrigger = true;

        var room = go.AddComponent<Room>();
        room.roomType = spec.type;
        room.waveCount = spec.waveCount;
        room.waveInterval = 0.8f;
        room.useDatabasePool = true;
        room.enemyHealthOverride = -1;      // 走数据库 × 全局倍率
        room.spawnStrokeOnClear = spec.type != RoomType.Start;

        return room;
    }

    /// <summary>
    /// 造一扇门。**尺寸和朝向照抄 Level1 / Level2 的已验证做法。**
    ///
    /// <paramref name="horizontalPair"/> = 两间房是左右相邻的。
    ///   true  → 门堵的是**竖墙**上的洞：竖条 0.5×3，不旋转
    ///   false → 门堵的是**横墙**上的洞：碰撞盒改成 3×0.5 并旋转 90°
    ///           （只旋转不够 —— 碰撞盒是 0.5×3 的竖条，转 90° 后是 3×0.5，
    ///             和 Level2 的做法一致）
    /// </summary>
    static void MakeDoor(string name, Vector2 pos, bool horizontalPair)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Door.prefab");
        if (prefab == null) { Debug.LogWarning("[第三层] 找不到 Door.prefab"); return; }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        if (!horizontalPair)
        {
            // 上下相邻：门要横过来堵住横墙的洞
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var box = go.GetComponent<BoxCollider2D>();
            if (box != null) box.size = new Vector3(3f, 0.5f, 1f);
        }
        // 左右相邻时保持预制体原样（0.5×3，旋转 0）—— 和 Level1 完全一致
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
