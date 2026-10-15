#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把关卡场景缺的组件补上。
///
/// 目前只做一件事：给 Level1 的 Player 挂上 OpeningStrokeUI（开局选笔画界面）。
///
/// 为什么用脚本而不是直接改 prefab：
/// BackpackUI / QuickBarUI / StrokeInventory 都不是挂在 Player.prefab 上，
/// 而是各个场景里单独加的 —— 所以跟着场景走，保持一致性。
///
/// 菜单：Tools/补齐关卡组件
/// 命令行：-executeMethod SceneComponentPatcher.Patch
/// </summary>
public static class SceneComponentPatcher
{
    [MenuItem("Tools/补齐关卡组件")]
    public static void Patch()
    {
        PatchLevel1();
    }

    static void PatchLevel1()
    {
        const string path = "Assets/Scenes/Level1.unity";
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[组件] 找不到 {path}");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("[组件] Level1 里找不到 Player");
            return;
        }

        int added = 0;

        if (player.GetComponent<OpeningStrokeUI>() == null)
        {
            player.AddComponent<OpeningStrokeUI>();
            added++;
            Debug.Log("[组件] 已给 Level1 的 Player 挂上 OpeningStrokeUI");
        }

        if (player.GetComponent<StrokeInventory>() == null)
        {
            player.AddComponent<StrokeInventory>();
            added++;
            Debug.Log("[组件] Level1 的 Player 缺 StrokeInventory，已补上");
        }

        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[组件] Level1 处理完成，新增 {added} 个组件");
    }
}
#endif
