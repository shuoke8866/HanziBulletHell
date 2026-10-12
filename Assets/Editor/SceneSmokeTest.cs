#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景冒烟测试：在编辑器里逐个加载每个关卡并运行若干秒，
/// 捕获加载期间的异常与错误日志。
///
/// 用途：WebGL 构建里"进入下一层就卡住"这类问题，靠静态检查文件很难定位。
/// 这个测试直接让 Unity 真正加载并运行目标场景，把异常打出来。
///
/// 用法（命令行）：
///   Unity.exe -quit -batchmode -nographics -projectPath . -executeMethod SceneSmokeTest.RunAll -logFile -
/// 只测一个场景：
///   -executeMethod SceneSmokeTest.RunAll  （默认测全部）
/// </summary>
public static class SceneSmokeTest
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    /// <summary>每个场景运行多少秒</summary>
    const float RunSeconds = 3f;

    static int errorCount;
    static int exceptionCount;

    public static void RunAll()
    {
        Application.logMessageReceived += OnLog;

        Debug.Log("========== 场景冒烟测试开始 ==========");

        foreach (string path in Scenes)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[冒烟] 场景文件不存在: {path}");
                continue;
            }

            Debug.Log($"---------- 加载 {path} ----------");
            errorCount = 0;
            exceptionCount = 0;

            try
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Debug.Log($"[冒烟] 已加载: {scene.name}  根对象数={scene.rootCount}");

                // 统计场景里的关键组件，确认内容没丢
                ReportSceneContents();

                // 运行若干帧（编辑器批处理下用 EditorApplication.QueuePlayerLoopUpdate 推进）
                RunFrames(RunSeconds);
            }
            catch (Exception e)
            {
                exceptionCount++;
                Debug.LogError($"[冒烟] 加载 {path} 抛出异常: {e}");
            }

            Debug.Log($"[冒烟] {Path.GetFileName(path)} 完成：错误 {errorCount} 个，异常 {exceptionCount} 个");
        }

        Application.logMessageReceived -= OnLog;
        Debug.Log($"========== 场景冒烟测试结束：累计错误 {errorCount}，异常 {exceptionCount} ==========");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void ReportSceneContents()
    {
        var rooms = UnityEngine.Object.FindObjectsOfType<Room>();
        var enemies = UnityEngine.Object.FindObjectsOfType<Enemy>();
        var bosses = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
        int bossCount = 0;
        foreach (var mb in bosses)
        {
            string n = mb.GetType().Name;
            if (n == "BossShi" || n == "BossGuan" || n == "BossHuang") bossCount++;
        }

        var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
        var player = PlayerRef.GameObject;

        Debug.Log($"[冒烟]   房间 {rooms.Length} 个 / 敌人 {enemies.Length} 个 / BOSS {bossCount} 个");
        Debug.Log($"[冒烟]   GameManager={(gm != null ? "有" : "无")}  玩家={(player != null ? player.name : "无")}");

        foreach (var r in rooms)
        {
            Debug.Log($"[冒烟]   房间「{r.name}」type={r.roomType} waveCount={r.waveCount} " +
                      $"doors={r.doors.Count} 占位={r.transform.position}  门状态: {r.DoorInfo()}");

            // 刷怪点必须在房间内。
            // 踩过的坑：Level3 的 Room3（最终BOSS 皇）与 Room4 共用了同一个刷怪点，
            // 而那个点在 Room4 的范围里 —— 结果"皇"刷在隔壁房间，玩家在本房间
            // 永远清不掉，门永久关闭，并且 AnotherCombatRoomActive() 会让后续所有
            // 房间都不再刷怪，彻底卡死。这里主动报警。
            foreach (var sp in r.enemySpawnPoints)
            {
                if (sp == null)
                {
                    Debug.LogError($"[冒烟]   房间「{r.name}」的 enemySpawnPoints 里有空引用！");
                    continue;
                }

                if (!r.IsInsideRoom(sp.position))
                {
                    Debug.LogError(
                        $"[冒烟]   房间「{r.name}」的刷怪点 {sp.name} 在 ({sp.position.x},{sp.position.y}) " +
                        $"落在房间范围之外 —— 敌人会刷到别的房间，本房间永远清不掉。" +
                        $"  房间范围: {r.RoomBoundsText()}  房间中心: {r.RoomCenter}");
                }
            }
        }

        if (gm == null)
            Debug.LogError("[冒烟] 场景里没有 GameManager —— 传送门将无法工作！");
        if (player == null)
            Debug.LogError("[冒烟] 场景里没有 Player —— 无法游玩！");
    }

    static void RunFrames(float seconds)
    {
        // 批处理模式下没有正常的 PlayerLoop 推进，需要手动驱动
        float end = (float)EditorApplication.timeSinceStartup + seconds;
        int frames = 0;

        while ((float)EditorApplication.timeSinceStartup < end && frames < 10000)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            System.Threading.Thread.Sleep(16);
            frames++;
        }

        Debug.Log($"[冒烟]   推进了 {frames} 帧");
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            errorCount++;
            if (type == LogType.Exception) exceptionCount++;
        }
    }
}
#endif
