#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 调试用：往关卡里铺设一批笔画宝箱，方便快速测试汉字合成系统。
///
/// 用法（菜单）：Tools/铺设测试笔画宝箱
/// 用法（命令行）：-executeMethod StrokePickupScatter.ScatterLevel1
///
/// 为什么写成编辑脚本而不是手改场景 YAML：
///   本项目已经因为手改场景文件出过两次事故（Level2 被写成空场景、
///   改 Room3 时切片错位吞掉组件块）。用编辑器 API 生成既有类型检查，
///   也能在保存前做位置合法性校验。
/// </summary>
public static class StrokePickupScatter
{
    // 每次铺设使用的关卡
    const string Level1Path = "Assets/Scenes/Level1.unity";

    /// <summary>要铺设的笔画种类循环表</summary>
    static readonly StrokeType[] Palette =
    {
        StrokeType.Heng, StrokeType.Shu, StrokeType.Pie, StrokeType.Na,
        StrokeType.Dian, StrokeType.Zhe
    };

    [MenuItem("Tools/铺设测试笔画宝箱")]
    public static void ScatterLevel1()
    {
        Scatter(Level1Path);
    }

    public static void Scatter(string scenePath)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError($"[宝箱] 场景不存在：{scenePath}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm == null)
        {
            Debug.LogError("[宝箱] 场景里没有 GameManager");
            return;
        }
        if (gm.strokePickupPrefab == null)
        {
            Debug.LogError("[宝箱] GameManager 的 strokePickupPrefab 未设置");
            return;
        }

        // 1) 清掉上一次铺设的（名字以 _TestStroke 开头的）
        var stale = new List<GameObject>();
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            CollectByPrefix(go.transform, "_TestStroke", stale);
        }
        foreach (GameObject go in stale) Object.DestroyImmediate(go);
        Debug.Log($"[宝箱] 清理旧宝箱 {stale.Count} 个");

        // 2) 收集所有房间的可行走区域与墙壁
        Room[] rooms = Object.FindObjectsOfType<Room>();
        var walls = new List<Collider2D>();
        foreach (GameObject go in scene.GetRootGameObjects())
            CollectWalls(go.transform, walls);

        Debug.Log($"[宝箱] 房间 {rooms.Length} 个，墙壁碰撞体 {walls.Count} 个");

        // 3) 在每个房间里按网格找合法落点
        int placed = 0;
        int rejected = 0;
        int paletteIndex = 0;

        foreach (Room room in rooms)
        {
            BoxCollider2D box = room.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogWarning($"[宝箱] 房间「{room.name}」没有 BoxCollider2D，跳过");
                continue;
            }

            Vector2 center = (Vector2)room.transform.position + box.offset;
            Vector2 half = box.size * 0.5f;

            // 从房间中心向外做网格，边距 2.0 保证不贴墙
            const float margin = 2.0f;
            const float step = 2.4f;

            float x0 = center.x - half.x + margin;
            float x1 = center.x + half.x - margin;
            float y0 = center.y - half.y + margin;
            float y1 = center.y + half.y - margin;

            int inRoom = 0;

            for (float y = y0; y <= y1 + 0.01f; y += step)
            {
                for (float x = x0; x <= x1 + 0.01f; x += step)
                {
                    Vector3 pos = new Vector3(x, y, 0f);

                    // 必须真的在房间范围内
                    if (!room.IsInsideRoom(pos)) { rejected++; continue; }

                    // 不能和墙壁/障碍重叠
                    if (OverlapsWall(pos, walls)) { rejected++; continue; }

                    // 不要铺在玩家出生点附近（Level1 玩家在 -10,0）
                    if (Vector2.Distance(pos, new Vector2(-10f, 0f)) < 2.5f) { rejected++; continue; }

                    StrokeType type = Palette[paletteIndex % Palette.Length];
                    paletteIndex++;

                    Spawn(gm.strokePickupPrefab, pos, type, room.name, inRoom);
                    inRoom++;
                    placed++;
                }
            }

            if (inRoom == 0)
                Debug.LogWarning($"[宝箱] 房间「{room.name}」没找到合法落点");
        }

        Debug.Log($"[宝箱] 铺设完成：放置 {placed} 个，跳过 {rejected} 个位置");

        if (placed == 0)
        {
            Debug.LogError("[宝箱] 一个都没放上，请检查房间范围与墙壁设置");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[宝箱] 场景已保存：{scenePath}");
    }

    static void Spawn(GameObject prefab, Vector3 pos, StrokeType type, string roomName, int index)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;
        go.name = $"_TestStroke_{roomName}_{index}_{type}";

        StrokePickup sp = go.GetComponent<StrokePickup>();
        if (sp != null) sp.strokeType = type;
    }

    /// <summary>
    /// 位置是否和墙壁/障碍重叠。
    /// 用 AABB 扩张判断，不依赖 Physics2D（编辑器里刚放置的碰撞体未必立刻同步）。
    /// </summary>
    static bool OverlapsWall(Vector3 pos, List<Collider2D> walls)
    {
        const float clearance = 0.9f;
        var p = new Vector2(pos.x, pos.y);

        foreach (Collider2D w in walls)
        {
            if (w == null || !w.enabled) continue;

            Bounds b = w.bounds;
            // 把墙的包围盒向外扩一点，保证宝箱不会贴在墙上
            b.Expand(clearance * 2f);

            if (p.x >= b.min.x && p.x <= b.max.x &&
                p.y >= b.min.y && p.y <= b.max.y)
                return true;
        }

        return false;
    }

    static void CollectByPrefix(Transform t, string prefix, List<GameObject> outList)
    {
        if (t.name.StartsWith(prefix)) outList.Add(t.gameObject);

        for (int i = 0; i < t.childCount; i++)
            CollectByPrefix(t.GetChild(i), prefix, outList);
    }

    static void CollectWalls(Transform t, List<Collider2D> outList)
    {
        // 墙体命名约定：名字里带 Wall
        if (t.name.Contains("Wall"))
        {
            foreach (Collider2D c in t.GetComponents<Collider2D>())
                outList.Add(c);
        }

        for (int i = 0; i < t.childCount; i++)
            CollectWalls(t.GetChild(i), outList);
    }
}
#endif
