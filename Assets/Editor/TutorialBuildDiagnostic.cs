#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 诊断：教程层生成过程中，传送门 / 宝箱 / 笔画展台分别是在哪一步丢掉的。
///
/// 玩家反馈"level0 的关卡全没了"，实测场景里确实找不到 Portal_ToLevel1、
/// TreasureChest、StrokePickup —— 但生成日志说都放了。
/// 所以要么是生成之后被谁删了，要么是 AddXxx 内部失败了没报错。
///
/// 命令行：-executeMethod TutorialBuildDiagnostic.Run
/// </summary>
public static class TutorialBuildDiagnostic
{
    const string ScenePath = "Assets/Scenes/Level0.unity";

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 教程层生成诊断 ==========");
        sb.AppendLine();

        // ---------- 1) 生成前：预制体在不在 ----------
        sb.AppendLine("--- 1) 生成前检查预制体 ---");
        foreach (string p in new[]
        {
            "Assets/Portal.prefab",
            "Assets/TreasureChest.prefab",
            "Assets/StrokePickup.prefab",
            "Assets/Boss_Shi 1.prefab",
        })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            sb.AppendLine($"      {(prefab != null ? "✓" : "✗")} {p}");
        }
        sb.AppendLine();

        // ---------- 2) 跑一次真实生成 ----------
        sb.AppendLine("--- 2) 执行 TutorialLevelBuilder.Build() ---");
        TutorialLevelBuilder.Build();
        sb.AppendLine("      已执行");
        sb.AppendLine();

        // ---------- 3) 生成后：场景里到底有什么 ----------
        sb.AppendLine("--- 3) 生成后的场景内容 ---");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var roots = new List<string>();
        foreach (GameObject go in scene.GetRootGameObjects()) roots.Add(go.name);

        sb.AppendLine($"      根对象 {roots.Count} 个");

        string[] must =
        {
            "Portal_ToLevel1", "TreasureChest", "StrokeGallery",
            "__OpeningIntro", "Player", "GameManager", "Canvas",
        };

        foreach (string want in must)
        {
            bool inRoots = roots.Contains(want);
            // 也可能在子层级里
            Transform found = FindDeep(scene, want);
            string where = found == null ? "**找不到**"
                : (found.parent == null ? "根对象" : "子级：" + PathOf(found));

            GameObject prefabOf = null;
            if (found != null)
                prefabOf = PrefabUtility.GetCorrespondingObjectFromSource(found.gameObject);

            sb.AppendLine($"      {(found != null ? "✓" : "✗")} {want,-20} {where}" +
                          (prefabOf != null ? $"  （是 {prefabOf.name} 的实例）" : ""));
        }

        sb.AppendLine();
        sb.AppendLine("      所有根对象：");
        foreach (string n in roots) sb.AppendLine("          " + n);

        // ---------- 4) 房间的 portal 字段有没有指上 ----------
        sb.AppendLine();
        sb.AppendLine("--- 4) BOSS 房的 portal 字段 ---");
        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            if (r.roomType != RoomType.Boss) continue;
            sb.AppendLine($"      {r.name}.portal = " +
                          $"{(r.portal != null ? r.portal.name : "**null**")}");
        }

        File.WriteAllText("F:/HanziBulletHell/_tutdiag.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[教程诊断]\n" + sb.ToString());
    }

    static Transform FindDeep(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform t = FindIn(root.transform, name);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindIn(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform r = FindIn(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    static string PathOf(Transform t)
    {
        string p = t.name;
        Transform c = t.parent;
        while (c != null) { p = c.name + "/" + p; c = c.parent; }
        return p;
    }
}
#endif
