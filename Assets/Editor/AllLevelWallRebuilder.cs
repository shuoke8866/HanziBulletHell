#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 对全部关卡统一重建房间墙体。
///
/// 规则见 RoomWallBuilder：每个房间封成一整圈墙，门只是墙上 3 单位的小口。
///
/// 菜单：Tools/重建所有房间墙体
/// 命令行：-executeMethod AllLevelWallRebuilder.RebuildEverything
/// </summary>
public static class AllLevelWallRebuilder
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level0.unity",
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    /// <summary>
    /// 只跑墙体自检，不重建。用来确认"房间四周到底封没封住"。
    ///
    /// 会用三种判据各跑一遍：
    ///   · 关门时（房间在打）—— 门算实心，这时**不允许有任何洞**
    ///   · 开门时（房间没打）—— 门那段是通道，允许有洞，但要看是不是只有门那么大
    ///   · 紧邻的房间之间 —— 两间房共用一堵墙时，墙必须落在一起
    /// </summary>
    [MenuItem("Tools/检查房间墙体漏洞")]
    public static void ValidateOnly()
    {
        foreach (string path in Scenes)
        {
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Debug.Log($"[墙检] ===== {Path.GetFileName(path)} =====");

            int shut = RoomWallBuilder.ValidateWalls(0.4f, doorsClosed: true, verbose: true);
            int open = RoomWallBuilder.ValidateWalls(0.4f, doorsClosed: false, verbose: false);

            Debug.Log($"[墙检] {Path.GetFileName(path)}：关门漏洞 {shut} / 开门漏洞 {open}");
        }
    }

    /// <summary>
    /// 把房间封成一圈之后，必须确认**每一间都还进得去**。
    ///
    /// 一间房如果一面墙都没开门，玩家就彻底进不去了 ——
    /// 这比"墙上有缝"严重得多（缝只是漏风，封死是关卡废掉）。
    ///
    /// 检查方式：用门建立"房间 ↔ 房间"的连通图，从起始房出发做一次遍历，
    /// 看有没有房间到不了。
    /// </summary>
    [MenuItem("Tools/检查房间连通性")]
    public static void CheckConnectivity()
    {
        foreach (string path in Scenes)
        {
            if (!File.Exists(path)) continue;

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            CheckSceneConnectivity(Path.GetFileName(path));
        }
    }

    static void CheckSceneConnectivity(string file)
    {
        var rooms = Object.FindObjectsOfType<Room>();
        if (rooms.Length == 0) return;

        // 门 → 引用它的房间集合
        var doorToRooms = new Dictionary<Door, List<Room>>();
        foreach (Room r in rooms)
        {
            if (r.doors == null) continue;
            foreach (Door d in r.doors)
            {
                if (d == null) continue;
                if (!doorToRooms.ContainsKey(d)) doorToRooms[d] = new List<Room>();
                if (!doorToRooms[d].Contains(r)) doorToRooms[d].Add(r);
            }
        }

        // 房间 → 通过门能到达的邻居
        var adj = new Dictionary<Room, List<Room>>();
        foreach (Room r in rooms) adj[r] = new List<Room>();

        foreach (var kv in doorToRooms)
        {
            var list = kv.Value;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (!adj[list[i]].Contains(list[j])) adj[list[i]].Add(list[j]);
                    if (!adj[list[j]].Contains(list[i])) adj[list[j]].Add(list[i]);
                }
        }

        // 从起始房开始遍历（没有起始房就用第一个）
        Room start = null;
        foreach (Room r in rooms)
            if (r.roomType == RoomType.Start) { start = r; break; }
        if (start == null) start = rooms[0];

        var reached = new HashSet<Room> { start };
        var queue = new Queue<Room>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Room cur = queue.Dequeue();
            foreach (Room nb in adj[cur])
                if (reached.Add(nb)) queue.Enqueue(nb);
        }

        int unreachable = 0;
        foreach (Room r in rooms)
        {
            if (reached.Contains(r)) continue;
            unreachable++;
            Debug.LogError($"[连通] {file}：「{r.name}」从起始房走不到 —— " +
                           $"它只在 {r.doors.Count} 扇门里出现过，封墙后玩家进不去！");
        }

        // 顺带报告没有任何门的房间
        foreach (Room r in rooms)
        {
            if (r.doors == null || r.doors.Count == 0)
                Debug.LogError($"[连通] {file}：「{r.name}」一扇门都没有，封墙后完全无法进入！");
        }

        Debug.Log($"[连通] {file}：{rooms.Length} 个房间，从「{start.name}」可达 {reached.Count} 个，" +
                  $"不可达 {unreachable} 个");
    }

    [MenuItem("Tools/重建所有房间墙体")]
    public static void RebuildEverything()
    {
        // 先重新生成教程层：TutorialLevelBuilder 是"以 Level1 为底另存为 Level0"，
        // 所以它必须跑在正式关卡改墙之前，否则会把刚铺好的新墙又清掉一遍。
        TutorialLevelBuilder.Build();

        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[墙] 场景不存在，跳过：{path}");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string file = Path.GetFileName(path);

            int removed = RoomWallBuilder.RemoveOldWalls();
            int built = RoomWallBuilder.RebuildAll(verbose: false);

            // 重建完立刻自检。两种情形分开看：
            //   开门时 —— 门那段本来就是通道，允许有洞（这个数只是参考）
            //   关门时 —— 必须是实心的，有任何洞都是真 bug，会把玩家漏出去
            int openLeaks = RoomWallBuilder.ValidateWalls(0.4f, doorsClosed: false, verbose: false);
            int shutLeaks = RoomWallBuilder.ValidateWalls(0.4f, doorsClosed: true, verbose: false);

            Debug.Log($"[墙] {file}：删旧墙 {removed} 段，新铺 {built} 段；" +
                      $"自检 开门时漏洞 {openLeaks} / 关门时漏洞 {shutLeaks}");

            if (shutLeaks > 0)
                RoomWallBuilder.ValidateWalls(0.4f, doorsClosed: true, verbose: true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log("[墙] 全部关卡处理完毕");
    }
}
#endif
