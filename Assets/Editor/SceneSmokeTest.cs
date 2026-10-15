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

            // 门的引用必须解析得到真实对象。
            // 场景里的门是预制体实例，如果预制体链接断掉，序列化数据会变成
            // "stripped" 空壳 —— 运行时 door == null，房间既关不上也开不了门，
            // 玩家就被关在里面出不去（"宝箱房无法开门"就是这么来的）。
            foreach (var d in r.doors)
            {
                if (d == null)
                {
                    Debug.LogError($"[冒烟]   房间「{r.name}」的 doors 列表里有【空引用】—— " +
                                   "门既关不上也开不了，玩家可能被永久关住！");
                    continue;
                }

                // DoorProbe 是门的引用计数 / 战斗锁定层。
                // Door 预制体里**没有**这个组件，现在是靠 Room.ClaimDoors 在运行时补挂的。
                // 那样"门该不该关"要等玩家进房才知道，非常脆弱 ——
                // 建议直接在 Door.prefab 里挂上，这里报 Error 提醒。
                if (d.GetComponent<DoorProbe>() == null)
                    Debug.LogError($"[冒烟]   门「{d.name}」没有 DoorProbe —— " +
                                   "门的开关逻辑要依赖运行时补挂，建议写进 Door.prefab");

                // 门的碰撞体缺失 → 关不上，玩家可以穿墙
                if (d.GetComponent<Collider2D>() == null)
                    Debug.LogError($"[冒烟]   门「{d.name}」没有 Collider2D —— 关不上，玩家能穿过去");
            }

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
            Debug.Log($"[冒烟]   可用笔画的最优解：{ranked[0].def.hanziName}（并列 {best.Count} 个：{names}）");
        }
        else
        {
            // 注意：这里用"可用笔画"（池 − 当前字占用）来判定。
            // 开局池里只有 1 横、而当前字「一」正占着它 → 可用为 0，
            // 拼不出任何字是**正常**的，不是故障。
            Debug.Log($"[冒烟]   可用笔画还拼不出别的字（当前「{hanzi.currentHanziName}」占用了全部笔画）");
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
        SimulateReversibleSwitch(hanzi, inv);
        CheckTieredSpawning();
    }

    /// <summary>
    /// 验证"敌人按层分层引入"真的生效。
    ///
    /// 为什么需要这个检查：光看场景配置是看不出问题的。
    /// Room.SpawnOne 有两条互斥路径，战斗房如果同时配了 useDatabasePool
    /// **和** enemyPrefabs，就会走"循环用固定预制体"那条，
    /// 按层分层的 PickRandom **一次都不会被调用** —— 表现就是
    /// "不管打到第几层，小怪种类都一模一样"。
    ///
    /// 这里直接按每一层跑一遍 EnemyDatabase.PickRandom，
    /// 统计抽到哪些怪、有没有超出该层应有的范围。
    /// </summary>
    static void CheckTieredSpawning()
    {
        Debug.Log("[冒烟]   === 验证敌人分层 ===");

        // 先检查模板预制体（清空 enemyPrefabs 后它必须存在，否则一只怪都刷不出）
        var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
        if (gm != null)
        {
            if (gm.enemyTemplatePrefab == null)
                Debug.LogError("[冒烟] GameManager.enemyTemplatePrefab 是空的 —— " +
                               "数据库刷怪会失败，房间将一只怪都不刷！");
            else
                Debug.Log($"[冒烟]   刷怪模板 = {gm.enemyTemplatePrefab.name}");
        }

        // 检查战斗房有没有"卡在固定预制体路径"上
        foreach (Room r in UnityEngine.Object.FindObjectsOfType<Room>())
        {
            if (r.roomType == RoomType.Boss || r.roomType == RoomType.Treasure ||
                r.roomType == RoomType.Start) continue;

            if (r.enemyPrefabs.Count > 0)
                Debug.LogError($"[冒烟] 房间「{r.name}」配了 {r.enemyPrefabs.Count} 个 enemyPrefabs —— " +
                               "会绕过数据库池，按层分层的刷怪不会生效！应当清空它。");
        }

        // 按层模拟抽取
        int savedFloor = GameData.currentFloor;

        try
        {
            // ---- 精英怪：第 1 层不该出，第 3 层必须能出 ----
            for (int floor = 1; floor <= 3; floor++)
            {
                GameData.currentFloor = floor;

                int eliteHits = 0;
                const int eliteTrials = 200;
                for (int i = 0; i < eliteTrials; i++)
                {
                    EnemyDef d = EnemyDatabase.PickRandom(true, 0.12f);
                    if (d != null && d.isElite) eliteHits++;
                }

                Debug.Log($"[冒烟]   第 {floor} 层精英出现 {eliteHits}/{eliteTrials} 次");

                if (floor == 1 && eliteHits > eliteTrials / 4)
                    Debug.LogError($"[冒烟] 第 1 层精英出得太频繁（{eliteHits}/{eliteTrials}）—— " +
                                   "精英的 minFloor 过滤可能没生效！");

                if (floor == 3 && eliteHits == 0)
                    Debug.LogError("[冒烟] 第 3 层完全不出精英 —— 精英可能被误过滤掉了！");
            }

            for (int floor = 1; floor <= 3; floor++)
            {
                GameData.currentFloor = floor;

                var seen = new Dictionary<string, int>();
                const int samples = 300;
                for (int i = 0; i < samples; i++)
                {
                    EnemyDef d = EnemyDatabase.PickRandom(false, 0f);
                    if (d == null) continue;
                    if (!seen.ContainsKey(d.id)) seen[d.id] = 0;
                    seen[d.id]++;
                }

                // 该层"应该"只出现 minFloor <= floor 的怪
                var leaked = new List<string>();
                string list = "";
                foreach (var kv in seen)
                {
                    EnemyDef d = EnemyDatabase.Get(kv.Key);
                    if (d != null && d.minFloor > floor) leaked.Add(d.displayName);

                    if (list.Length > 0) list += " ";
                    list += $"{d.displayName}×{kv.Value}";
                }

                Debug.Log($"[冒烟]   第 {floor} 层抽 {samples} 次 → {list}");

                // 越级怪有 EarlyAppearChance 的概率放行，属于设计内，不算错。
                // 但如果比例过高，说明过滤压根没生效。
                int leakedCount = 0;
                foreach (string k in leaked)
                    if (seen.ContainsKey(k)) leakedCount++;

                float ratio = leakedCount / (float)Mathf.Max(1, seen.Count);
                if (ratio > 0.6f)
                    Debug.LogError($"[冒烟] 第 {floor} 层出现 {leaked.Count} 种越级怪" +
                                   $"（{string.Join("/", leaked.ToArray())}）—— 分层过滤可能没生效！");
            }
        }
        finally
        {
            GameData.currentFloor = savedFloor;
        }

        Debug.Log("[冒烟]   === 敌人分层检查结束 ===");
    }

    /// <summary>
    /// 验证"切换可逆"—— 本次改动的核心。
    ///
    /// 旧实现每切一次字都会丢弃装不下的笔画，于是池子越来越小，
    /// 切几次后只能拼出「一」，且再也回不去。这里模拟：
    ///   反复在"最复杂的字"和"最简单的字"之间来回切，
    ///   检查永久笔画池数量是否始终不变。
    /// </summary>
    static void SimulateReversibleSwitch(HanziSystem hanzi, StrokeInventory inv)
    {
        if (hanzi == null) return;

        Debug.Log("[冒烟]   === 验证切换可逆性 ===");

        int poolBefore = hanzi.AllStrokes().Count;
        Debug.Log($"[冒烟]     切换前：当前「{hanzi.currentHanziName}」 永久池 {poolBefore} 笔");

        // 连续切换 12 次（尽量在复杂字与简单字之间来回）
        int switched = 0;
        for (int i = 0; i < 12; i++)
        {
            // 交替：一次挑最复杂、一次挑最简单
            List<HanziOption> all = hanzi.GetAllFormable();
            if (all.Count == 0) break;

            HanziDef target = (i % 2 == 0)
                ? all[0].def                        // 最复杂
                : all[all.Count - 1].def;           // 最简单

            if (target == null) continue;
            if (target.hanziName == hanzi.currentHanziName) continue;

            if (hanzi.SwitchTo(target)) switched++;
        }

        int poolAfter = hanzi.AllStrokes().Count;
        Debug.Log($"[冒烟]     切换 {switched} 次后：当前「{hanzi.currentHanziName}」 永久池 {poolAfter} 笔");

        if (poolAfter != poolBefore)
            Debug.LogError($"[冒烟] 永久笔画池被改变了（{poolBefore} → {poolAfter}）—— 切换应该是无损的！");
        else
            Debug.Log("[冒烟]     ✓ 笔画池无损，切换可逆");
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

        int advanced = 0;
        foreach (StrokeType s in sequence)
        {
            // 直接把笔画交给拾取逻辑（等价于玩家捡起来）
            bool ok = hanzi.TryAddStroke(s);
            if (ok) advanced++;

            string availStr = "";
            foreach (StrokeType p in hanzi.AvailableStrokes()) availStr += StrokeUtil.NameOf(p);

            Debug.Log($"[冒烟]     捡「{StrokeUtil.NameOf(s)}」 → " +
                      $"当前「{hanzi.currentHanziName}」 永久池={hanzi.AllStrokes().Count}笔 " +
                      $"可用=[{availStr}] 换字={(ok ? "是" : "否")}");
        }

        // 依新规则"只有捡到最多的那种笔画才自动变字"，不保证每次都变；
        // 但 5 笔下去至少应该变过一次。
        if (advanced == 0)
            Debug.LogError("[冒烟] 连续捡了 5 笔，一次都没换字 —— 自动合成没生效！");
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
