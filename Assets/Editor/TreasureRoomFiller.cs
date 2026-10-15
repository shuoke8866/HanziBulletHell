#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 往每个宝箱房里放实体宝箱。
///
/// 为什么需要：宝箱房（RoomType.Treasure）现在的奖励是"进房间自动给词条 + 掉几个笔画"，
/// 房间里**空无一物**。而项目里早就做好了两个宝箱预制体却完全没用上：
///   TreasureChest       按 F 打开，给一个笔画
///   ElementChest_Fire   给火元素（弹幕附带灼烧）
///   ElementChest_Water  给水元素（弹幕附带减速）
///
/// 这个脚本用编辑器 API 摆放（而不是手改场景 YAML —— 本项目已经因为手改场景
/// 出过好几次事故），并且摆放前会做位置合法性校验：
///   - 必须落在宝箱房的 BoxCollider2D 范围内
///   - 不能压在墙体上
///   - 互相之间保持间距
///
/// 菜单：Tools/布置宝箱房
/// 命令行：-executeMethod TreasureRoomFiller.FillAll
/// </summary>
public static class TreasureRoomFiller
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    const string TreasurePrefabPath = "Assets/TreasureChest.prefab";
    const string FirePrefabPath = "Assets/ElementChest_Fire.prefab";
    const string WaterPrefabPath = "Assets/ElementChest_Water.prefab";

    /// <summary>摆放的实例名前缀，用来识别"上次放的"以便清理</summary>
    const string Prefix = "_Chest_";

    [MenuItem("Tools/布置宝箱房")]
    public static void FillAll()
    {
        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[宝箱] 场景不存在，跳过：{path}");
                continue;
            }

            Fill(path);
        }

        Debug.Log("[宝箱] 全部场景处理完毕");
    }

    static void Fill(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        GameObject treasurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePrefabPath);
        GameObject firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirePrefabPath);
        GameObject waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);

        if (treasurePrefab == null)
        {
            Debug.LogError($"[宝箱] 找不到预制体 {TreasurePrefabPath}");
            return;
        }

        // ---- 1) 清掉上次放的 ----
        var stale = new List<GameObject>();
        foreach (GameObject go in scene.GetRootGameObjects())
            CollectByPrefix(go.transform, Prefix, stale);
        foreach (GameObject go in stale) Object.DestroyImmediate(go);

        // ---- 2) 收集墙体，用于避让 ----
        var walls = new List<Collider2D>();
        foreach (GameObject go in scene.GetRootGameObjects())
            CollectWalls(go.transform, walls);

        // ---- 3) 找到所有宝箱房 ----
        Room[] rooms = Object.FindObjectsOfType<Room>();
        int placed = 0;

        foreach (Room room in rooms)
        {
            if (room.roomType != RoomType.Treasure) continue;

            BoxCollider2D box = room.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogWarning($"[宝箱] 宝箱房「{room.name}」没有 BoxCollider2D，跳过");
                continue;
            }

            Vector2 center = (Vector2)room.transform.position + box.offset;
            Vector2 half = box.size * 0.5f;

            // 一排 5 个：4 个普通宝箱（各给一个笔画）+ 1 个元素宝箱
            // 元素宝箱轮流放火/水，避免同层重复
            const int count = 5;
            const float spacing = 2.2f;
            float startX = -(count - 1) * 0.5f * spacing;

            bool useFire = (GameData.currentFloor % 2) == 1;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = new Vector3(center.x + startX + i * spacing, center.y, 0f);

                if (!Inside(room, pos)) { continue; }
                if (OverlapsWall(pos, walls)) { continue; }

                GameObject prefab = treasurePrefab;
                if (i == count / 2)
                    prefab = useFire ? firePrefab : waterPrefab;
                if (prefab == null) prefab = treasurePrefab;

                GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.identity;
                go.name = $"{Prefix}{room.name}_{i}";

                placed++;
            }

            Debug.Log($"[宝箱] 宝箱房「{room.name}」放置完成，房间中心 ({center.x},{center.y})");
        }

        Debug.Log($"[宝箱] {Path.GetFileName(scenePath)}：共放置 {placed} 个宝箱（清理旧 {stale.Count} 个）");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static bool Inside(Room room, Vector3 pos)
    {
        // 直接用 Room 自己的判定，和运行时"这个点算不算在房间内"保持同一套逻辑
        return room.IsInsideRoom(pos);
    }

    /// <summary>位置是否压到墙体（用 AABB 扩张判断，不依赖 Physics2D 同步）</summary>
    static bool OverlapsWall(Vector3 pos, List<Collider2D> walls)
    {
        const float clearance = 1.2f;
        var p = new Vector2(pos.x, pos.y);

        foreach (Collider2D w in walls)
        {
            if (w == null || !w.enabled) continue;

            Bounds b = w.bounds;
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
        if (t.name.Contains("Wall"))
            foreach (Collider2D c in t.GetComponents<Collider2D>())
                outList.Add(c);

        for (int i = 0; i < t.childCount; i++)
            CollectWalls(t.GetChild(i), outList);
    }
}
#endif
