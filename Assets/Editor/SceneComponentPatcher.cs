#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把关卡场景缺的组件补上。
///
/// 做两件事：
///   · 每个关卡的 Player 都挂 HanziPickerUI（笔顺歧义时的选字面板）
///     —— 战斗房里随时可能凑出"十/丁"这种歧义笔顺，每个关卡都要有
///   · Level1 的 Player 额外挂 OpeningStrokeUI（开局选笔画界面）
///
/// 为什么用脚本而不是直接改 prefab：
/// BackpackUI / QuickBarUI / StrokeInventory 都不是挂在 Player.prefab 上，
/// 而是各个场景里单独加的 —— 跟着场景走，保持一致。
///
/// 菜单：Tools/补齐关卡组件
/// 命令行：-executeMethod SceneComponentPatcher.Patch
/// </summary>
public static class SceneComponentPatcher
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level0.unity",
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    [MenuItem("Tools/补齐关卡组件")]
    public static void Patch()
    {
        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[组件] 场景不存在，跳过：{path}");
                continue;
            }

            PatchScene(path);
        }
    }

    static void PatchScene(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        string file = Path.GetFileName(path);

        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogWarning($"[组件] {file} 里找不到 Player，跳过");
            return;
        }

        int added = 0;

        if (player.GetComponent<HanziPickerUI>() == null)
        {
            player.AddComponent<HanziPickerUI>();
            added++;
            Debug.Log($"[组件] {file}：挂上 HanziPickerUI");
        }

        if (player.GetComponent<StrokeInventory>() == null)
        {
            player.AddComponent<StrokeInventory>();
            added++;
            Debug.Log($"[组件] {file}：挂上 StrokeInventory");
        }

        // 开局选笔画只在正式第一层出现
        if (file == "Level1.unity" && player.GetComponent<OpeningStrokeUI>() == null)
        {
            player.AddComponent<OpeningStrokeUI>();
            added++;
            Debug.Log($"[组件] {file}：挂上 OpeningStrokeUI");
        }

        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[组件] {file}：新增 {added} 个组件");
    }
}
#endif
