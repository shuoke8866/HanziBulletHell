#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把关卡场景按正确顺序写进 Build Settings。
///
/// 为什么要专门写个脚本：WebGL 构建时如果 Level0 不在 Build Settings 里，
/// SceneManager.LoadScene("Level0") 会直接失败，玩家点开始就黑屏。
/// 手改 ProjectSettings 的 YAML 很容易漏字段，不如用官方 API。
///
/// 顺序固定为 Level0 → Level1 → Level2 → Level3（索引 0 就是启动场景）。
///
/// 菜单：Tools/更新构建场景列表
/// 命令行：-executeMethod BuildSceneListFixer.Fix
/// </summary>
public static class BuildSceneListFixer
{
    static readonly string[] Wanted =
    {
        "Assets/Scenes/Level0.unity",     // 新手教程（启动场景）
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    [MenuItem("Tools/更新构建场景列表")]
    public static void Fix()
    {
        var list = new List<EditorBuildSettingsScene>();
        int missing = 0;

        foreach (string path in Wanted)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[构建] 场景不存在，跳过：{path}");
                missing++;
                continue;
            }

            list.Add(new EditorBuildSettingsScene(path, true));
        }

        // 保留列表里其他已存在的场景（比如测试用的小场景），放在最后
        foreach (EditorBuildSettingsScene old in EditorBuildSettings.scenes)
        {
            if (string.IsNullOrEmpty(old.path)) continue;
            if (list.Exists(s => s.path == old.path)) continue;
            if (!File.Exists(old.path)) continue;

            list.Add(new EditorBuildSettingsScene(old.path, old.enabled));
        }

        EditorBuildSettings.scenes = list.ToArray();

        Debug.Log($"[构建] 场景列表已更新，共 {list.Count} 个" +
                  (missing > 0 ? $"（{missing} 个缺失被跳过）" : ""));

        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            Debug.Log($"[构建]   {(s.enabled ? "✓" : "✗")} {s.path}");
    }
}
#endif
