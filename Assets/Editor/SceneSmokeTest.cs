#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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

        ReportHanziSystem(player);
        ReportStrokePickups(rooms);
    }

    /// <summary>
    /// 报告笔画宝箱的数量、类型分布与位置合法性。
    ///
    /// 位置检查很重要：宝箱如果落在房间里却压在墙上，玩家就走不过去 ——
    /// 这正是之前 Room3 刷怪点越界那类问题的翻版，必须在测试里拦住。
    /// </summary>
    static void ReportStrokePickups(Room[] rooms)
    {
        StrokePickup[] pickups = UnityEngine.Object.FindObjectsOfType<StrokePickup>();
        if (pickups.Length == 0) return;

        var byType = new Dictionary<StrokeType, int>();
        int outside = 0;

        foreach (StrokePickup sp in pickups)
        {
            StrokeType t = sp.strokeType;
            int c;
            byType.TryGetValue(t, out c);
            byType[t] = c + 1;

            // 每个宝箱至少要落在某个房间范围内，否则玩家不一定够得到
            bool inAny = false;
            foreach (Room r in rooms)
            {
                if (r.IsInsideRoom(sp.transform.position)) { inAny = true; break; }
            }
            if (!inAny) outside++;
        }

        string dist = "";
        foreach (var kv in byType) dist += $"{StrokeUtil.NameOf(kv.Key)}={kv.Value} ";

        Debug.Log($"[冒烟]   笔画宝箱 {pickups.Length} 个：{dist}");

        if (outside > 0)
            Debug.LogError($"[冒烟] 有 {outside} 个宝箱落在所有房间范围之外 —— 玩家可能拿不到！");
    }

    /// <summary>
    /// 报告汉字系统状态，并实跑一遍新合成机制（笔画池 → 最优解 → 特点开关）。
    /// 这些是本次改动的核心，只静态看代码看不出问题。
    /// </summary>
    static void ReportHanziSystem(GameObject player)
    {
        if (player == null) return;

        var hanzi = player.GetComponent<HanziSystem>();
        var inv = player.GetComponent<StrokeInventory>();

        if (hanzi == null)
        {
            Debug.LogError("[冒烟] Player 上没有 HanziSystem！");
            return;
        }

        // 批处理下 MonoBehaviour 的 Awake 不一定会被 PlayerLoop 驱动到，
        // 这里显式把汉字重置成「一」，保证后面的求解器验证从确定状态出发
        hanzi.ResetToDefaultHanzi();

        Debug.Log($"[冒烟]   汉字系统：当前「{hanzi.currentHanziName}」" +
                  $"，背包 {inv?.strokes.Count ?? 0}/{inv?.maxSlots ?? 0}");

        // 运行时挂载能力组件
        PlayerKit.EnsureAttached();

        bool hasTraits = player.GetComponent<StrokeTraits>() != null;
        bool hasShield = player.GetComponent<PlayerShield>() != null;
        bool hasSlash = player.GetComponent<MeleeSlash>() != null;

        Debug.Log($"[冒烟]   能力组件：笔画特点={(hasTraits ? "有" : "无")} " +
                  $"护盾={(hasShield ? "有" : "无")} 斩击={(hasSlash ? "有" : "无")}");

        if (!hasTraits || !hasShield || !hasSlash)
            Debug.LogError("[冒烟] PlayerKit 未能挂载全部能力组件！");

        // 求解器实跑
        List<HanziOption> ranked = hanzi.GetSwitchCandidates();
        var pool = hanzi.BuildStrokePool();

        string poolStr = "";
        foreach (StrokeType s in pool) poolStr += StrokeUtil.NameOf(s);

        Debug.Log($"[冒烟]   笔画池=[{poolStr}] 候选数={ranked.Count}");

        if (ranked.Count > 0)
        {
            List<HanziOption> best = HanziSolver.TiedWithBest(ranked);
            string names = "";
            foreach (HanziOption o in best)
            {
                if (names.Length > 0) names += " ";
                names += o.def.hanziName;
            }
            Debug.Log($"[冒烟]   最优解：{ranked[0].def.hanziName}（并列 {best.Count} 个：{names}）");
        }
        else
        {
            Debug.LogError("[冒烟] 笔画池凑不出任何候选汉字 —— 合成系统可能坏了！");
        }

        // 特点开关：空汉字必须把所有特点清掉
        var st = player.GetComponent<StrokeTraits>();
        if (st != null)
        {
            st.Refresh(null);
            bool none = !st.Pierce && !st.ShieldOnFire && !st.MeleeSlash;

            Debug.Log($"[冒烟]   特点开关：空字清空={none}（期望 true）");
            if (!none)
                Debug.LogError("[冒烟] 空汉字时笔画特点没有被清空！");
        }

        SimulatePickups(hanzi, inv);
    }

    /// <summary>
    /// 模拟一串拾取，端到端验证"拾取后自动切到最优解"。
    /// 只做前几个场景，够证明机制连通即可。
    /// </summary>
    static void SimulatePickups(HanziSystem hanzi, StrokeInventory inv)
    {
        if (hanzi == null || inv == null) return;

        // 清空背包，从「一」开始
        inv.strokes.Clear();
        inv.SaveToGameData();
        hanzi.ResetToDefaultHanzi();

        var sequence = new List<StrokeType>
        {
            StrokeType.Heng, StrokeType.Shu, StrokeType.Heng,
            StrokeType.Pie, StrokeType.Na
        };

        Debug.Log("[冒烟]   === 模拟拾取序列 ===");

        foreach (StrokeType s in sequence)
        {
            // 直接把笔画交给合成逻辑（等价于玩家捡起来）
            bool ok = hanzi.TryAddStroke(s);

            string poolStr = "";
            foreach (StrokeType p in hanzi.BuildStrokePool()) poolStr += StrokeUtil.NameOf(p);

            Debug.Log($"[冒烟]     捡「{StrokeUtil.NameOf(s)}」 → " +
                      $"当前「{hanzi.currentHanziName}」 背包={inv.strokes.Count} " +
                      $"池=[{poolStr}] 切换={(ok ? "成功" : "未变化")}");
        }

        if (hanzi.currentHanziName == "一")
            Debug.LogError("[冒烟] 连续捡了 5 笔，汉字仍停在「一」—— 自动合成没生效！");
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
