#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 清掉上一轮为了测试合成系统而铺在场景里的临时笔画宝箱（名字以 _TestStroke_ 开头）。
///
/// 这些宝箱是开发期用的，**不该出现在正式关卡里** ——
/// 第一层铺了 37 个，等于一开局就能捡满一整套笔画，把成长曲线完全冲掉。
///
/// 如果想再铺回来测试，用 Tools/铺设测试笔画宝箱（StrokePickupScatter）。
///
/// 菜单：Tools/清除测试笔画宝箱
/// 命令行：-executeMethod TestPickupCleaner.RemoveAll
/// </summary>
public static class TestPickupCleaner
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    const string Prefix = "_TestStroke_";

    [MenuItem("Tools/清除测试笔画宝箱")]
    public static void RemoveAll()
    {
        int total = 0;

        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[清理] 场景不存在，跳过：{path}");
                continue;
            }

            total += Remove(path);
        }

        Debug.Log($"[清理] 全部场景处理完毕，共删除 {total} 个测试宝箱");
    }

    static int Remove(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var stale = new List<GameObject>();
        foreach (GameObject go in scene.GetRootGameObjects())
            Collect(go.transform, stale);

        foreach (GameObject go in stale)
            Object.DestroyImmediate(go);

        Debug.Log($"[清理] {Path.GetFileName(scenePath)}：删除 {stale.Count} 个测试宝箱");

        if (stale.Count > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        return stale.Count;
    }

    static void Collect(Transform t, List<GameObject> outList)
    {
        if (t.name.StartsWith(Prefix)) outList.Add(t.gameObject);

        for (int i = 0; i < t.childCount; i++)
            Collect(t.GetChild(i), outList);
    }
}
#endif
