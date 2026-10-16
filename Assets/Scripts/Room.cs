using UnityEngine;
using System.Collections.Generic;

public enum RoomType
{
    Start,      // 起始房：无战斗
    Combat,     // 普通战斗房
    Elite,      // 精英房：必有精英 + 更好奖励
    Treasure,   // 宝箱房：无战斗，直接给奖励
    Boss        // Boss 房
}

public class Room : MonoBehaviour
{
    [Header("房间类型")]
    public RoomType roomType = RoomType.Combat;

    [Header("门")]
    public List<Door> doors = new List<Door>();

    [Header("敌人来源")]
    [Tooltip("勾选后用 EnemyDatabase 按权重随机抽怪（推荐，会自动随层数出精英）")]
    public bool useDatabasePool = false;
    [Tooltip("固定刷这一种怪（留空则用上面的随机池）")]
    public string singleDefId = "";
    public List<GameObject> enemyPrefabs = new List<GameObject>();
    public List<Transform> enemySpawnPoints = new List<Transform>();

    [Header("波次")]
    [Tooltip("总共几波（0 或 1 = 单波）")]
    public int waveCount = 1;
    [Tooltip("每波之间的间隔（秒）")]
    public float waveInterval = 1.2f;

    [Header("敌人属性覆盖")]
    public int enemyHealthOverride = -1;
    [Tooltip("每波敌人的额外数量（在层数加成之外再加）")]
    public int extraEnemiesPerWave = 0;

    [Header("清房奖励")]
    public bool grantBoonOnClear = true;          // 随机词条
    public bool spawnStrokeOnClear = false;       // 额外掉落笔画

    /// <summary>
    /// 掉落几个笔画。
    ///
    /// 0 或负数 = 按层数自动取值（见 GameBalance.CombatBonusStrokesForFloor /
    /// TreasureBonusStrokesForFloor），这样第 1 层给得多、便于起手，
    /// 后面逐层收紧，不必去每个场景里手改。
    /// 想给某个房间单独配数量时，把它设成正数即可覆盖。
    /// </summary>
    [Tooltip("掉落笔画数。0 或负数 = 按层数自动取值")]
    public int bonusStrokesOnClear = 0;

    [Header("状态")]
    public bool roomCleared = false;

    private List<GameObject> aliveEnemies = new List<GameObject>();
    private bool enemiesSpawned = false;
    // 说明：玩家是否在房间内的标记已不再使用，相关逻辑改由触发器的进入/离开事件处理
    private int currentWave = 0;

    // 起始房 / 宝箱房不刷怪
    bool IsNonCombat
    {
        get { return roomType == RoomType.Start || roomType == RoomType.Treasure; }
    }

    // 本房间已经"占住"的门（引用计数，防止和相邻房间互相锁死）
    private readonly List<Door> claimedDoors = new List<Door>();

    void Start()
    {
        // 起始房和宝箱房直接算已清理
        if (IsNonCombat) roomCleared = true;

        OpenAllDoors();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;



        // 宝箱房：进入即给奖励（不用打）
        if (roomType == RoomType.Treasure && !roomCleared)
        {
            GrantClearReward();
            roomCleared = true;
            return;
        }

        if (roomCleared || enemiesSpawned) return;

        // 场景里房间触发器是互相重叠的（Room2 和 Room2-2 的触发区有重合），
        // 如果不加这道守卫，玩家一走进重叠区就会同时触发两个房间、
        // 两个房间互相锁门 → 玩家被关死。
        if (AnotherCombatRoomActive())
        {
            GameLog.Info($"「{name}」触发被忽略：还有没打完的房间（{ActiveRoomName()}）");
            return;
        }

        EnterRoom();
    }

    // 是否已有别的战斗房间开打且没清空
    bool AnotherCombatRoomActive()
    {
        Room[] all = FindObjectsOfType<Room>();
        foreach (Room r in all)
        {
            if (r == this) continue;
            if (r.IsNonCombat) continue;
            if (r.enemiesSpawned && !r.roomCleared) return true;
        }
        return false;
    }

    string ActiveRoomName()
    {
        Room[] all = FindObjectsOfType<Room>();
        foreach (Room r in all)
        {
            if (r == this) continue;
            if (!r.IsNonCombat && r.enemiesSpawned && !r.roomCleared) return r.name;
        }
        return "?";
    }

    // 供 UI/调试查询
    public bool IsNonCombatRoom { get { return IsNonCombat; } }

    /// <summary>
    /// 这个房间是不是"已经开打、但还没清完"。
    ///
    /// 给门用：门要根据"有没有房间正在战斗"来决定开关，
    /// 而 enemiesSpawned 是私有的，所以这里开一个只读入口。
    /// </summary>
    public bool IsFighting { get { return enemiesSpawned && !roomCleared; } }

    void OnTriggerExit2D(Collider2D other)
    {
        // 注意：不重置 enemiesSpawned！防止玩家进出时重复刷怪
    }

    void EnterRoom()
    {
        if (enemiesSpawned) return;

        enemiesSpawned = true;
        currentWave = 0;

        GameLog.Info($"玩家进入「{name}」（{roomType}，第 {GameData.currentFloor} 层）");

        ClaimDoors();
        GameLog.Info($"[门] 「{name}」占门后: {DoorInfo()}");
        StartNextWave();
    }

    /// <summary>本房间门的状态摘要（诊断用：门没开会导致玩家被困在房间里）</summary>
    public string DoorInfo()
    {
        if (doors == null || doors.Count == 0) return "无门";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < doors.Count; i++)
        {
            Door d = doors[i];
            if (d == null) { sb.Append("[空] "); continue; }

            DoorProbe probe = d.GetComponent<DoorProbe>();
            string claimText = probe != null
                ? (probe.IsClaimed ? "被占用" : "空闲")
                : "无Probe";

            sb.Append($"[{d.name} {claimText} isOpen={d.isOpen}] ");
        }
        return sb.ToString().Trim();
    }

    // 占住本房间的所有门。用引用计数后，共享门不会被另一个房间提前解锁；
    // 同时因为只是"关这一间房的门"，不会把玩家要去的宝箱房/关底房锁上。
    void ClaimDoors()
    {
        claimedDoors.Clear();

        foreach (Door door in doors)
        {
            if (door == null) continue;

            DoorProbe probe = door.GetComponent<DoorProbe>();
            if (probe == null) probe = door.gameObject.AddComponent<DoorProbe>();

            probe.Claim();
            claimedDoors.Add(door);
        }
    }

    void ReleaseDoors()
    {
        foreach (Door door in claimedDoors)
        {
            if (door == null) continue;
            DoorProbe probe = door.GetComponent<DoorProbe>();
            if (probe != null) probe.Release();
        }
        claimedDoors.Clear();
    }

    // ================= 波次 =================
    /// <summary>
    /// 算出第 1 波会生成几只 —— **只给编辑器诊断用**，不改任何状态。
    ///
    /// 存在的意义：让诊断工具能"问真实逻辑"而不是自己复刻一遍公式。
    /// （复刻过一次，结果我修了逻辑、工具还按老公式算，白折腾一轮。）
    /// </summary>
    public int PreviewFirstWaveCount()
    {
        int extra = GameManager.ExtraEnemyCount + extraEnemiesPerWave;
        int baseCount = GetBaseEnemyCount();

        if (roomType == RoomType.Elite) extra += 1;
        if (roomType == RoomType.Boss) { extra = 0; baseCount = 1; }

        return baseCount + extra;
    }

    void StartNextWave()
    {
        currentWave++;

        int extra = GameManager.ExtraEnemyCount + extraEnemiesPerWave;
        int baseCount = GetBaseEnemyCount();

        // 最后一波按类型加成
        if (roomType == RoomType.Elite) extra += 1;

        // ============================================================
        //  BOSS 房：**永远只生成 1 只**
        // ============================================================
        //
        // 踩过的坑（玩家反馈"第二关会生成两个官"，而且改了多次没解决）：
        //
        // ExtraEnemyCount 是"按层数递增的额外敌人"——
        //     floor1 → 1、floor2 → 2、floor3 → 3
        // 这个设计是给**普通战斗房**加杂兵用的，但它**没有排除 BOSS 房**。
        // 于是原来那行 `extra = Max(0, extra - 1)` 只是"减一个"：
        //     floor1：1-1 = 0 → 1 只 ✓
        //     floor2：2-1 = 1 → **2 只** ✗
        //     floor3：3-1 = 2 → **3 只** ✗
        //
        // 层数越高 BOSS 越多 —— 而且不是"改几次就好"的问题，
        // 因为根因在乘法而不是加法，之前调刷怪点/预制体都碰不到它。
        //
        // 现在直接**把额外数量清零**：BOSS 房就该只有 BOSS。
        // 想要"BOSS + 杂兵"的话，应该用另一个字段显式配置，而不是靠层数递增加出来。
        if (roomType == RoomType.Boss)
        {
            extra = 0;
            baseCount = 1;
        }

        int count = baseCount + extra;

        // 第 1 波在进门瞬间生成（此时玩家正在跨门，没有反应时间也没关系）；
        // 后续波次先给预警，避免敌人在玩家毫无准备时凭空出现。
        bool telegraph = currentWave > 1 && waveTelegraph > 0.01f;

        if (telegraph)
        {
            for (int i = 0; i < count; i++)
                Fx.SpawnTelegraph(GetSpawnPosition(i), waveTelegraph);

            Sfx.Play(Sfx.Id.Error, 0.28f);
            StartCoroutine(SpawnAfterDelay(count, waveTelegraph));
        }
        else
        {
            for (int i = 0; i < count; i++)
                SpawnOne(i);
        }

        GameLog.Info($"「{name}」第 {currentWave}/{Mathf.Max(1, waveCount)} 波" +
                     $"{(telegraph ? $"（预警 {waveTelegraph:0.0}s 后）" : "")}，生成 {count} 只");
    }

    System.Collections.IEnumerator SpawnAfterDelay(int count, float delay)
    {
        yield return new WaitForSeconds(delay);

        // 房间可能在等待期间被清掉（例如玩家死亡重开）
        for (int i = 0; i < count; i++)
            SpawnOne(i);
    }

    /// <summary>下一波的预警时长（秒）。0 = 不预警</summary>
    public float waveTelegraph = 0.7f;

    /// <summary>
    /// 这个预制体是不是 BOSS。
    ///
    /// 判据用**有没有 BOSS 脚本**，而不是看名字 ——
    /// 名字是给人看的，脚本才是实际行为（`Boss_Shi 1` 这种名字里带空格和数字，
    /// 靠字符串匹配很容易漏）。
    /// </summary>
    static bool IsBossPrefab(GameObject prefab)
    {
        if (prefab == null) return false;

        return prefab.GetComponent<BossShi>() != null
            || prefab.GetComponent<BossGuan>() != null
            || prefab.GetComponent<BossHuang>() != null;
    }

    int GetBaseEnemyCount()
    {
        if (enemySpawnPoints.Count > 0 && enemyPrefabs.Count > 0) return enemySpawnPoints.Count;
        if (enemyPrefabs.Count > 0) return Random.Range(1, 4);
        return 2;
    }

    // 选一只怪的来源
    void SpawnOne(int index)
    {
        Vector3 pos = GetSpawnPosition(index);

        // 0) **类型守卫：BOSS 只能出现在 BOSS 房**
        //
        // 玩家反馈"第三关 BOSS 会莫名其妙在别的房间就生成，然后最后一个房间
        // 还是会生成 BOSS"。加这道守卫有两个作用：
        //   · 万一数据配错了（非 BOSS 房挂了 BOSS 预制体），不会真的刷出来
        //   · **明确报出来**，而不是静默地多刷一只 BOSS
        //
        // 之前的做法是"配了什么就刷什么"，配错只会表现为"莫名其妙多一个 BOSS"，
        // 排查时完全看不出是哪间房、哪个预制体的问题。
        if (roomType != RoomType.Boss && index < enemyPrefabs.Count && IsBossPrefab(enemyPrefabs[index]))
        {
            Debug.LogError(
                $"[刷怪] 房间「{name}」的 roomType={roomType}（不是 Boss 房），" +
                $"但它的 enemyPrefabs[{index}] 是 BOSS 预制体「{enemyPrefabs[index].name}」。\n" +
                "已跳过这只 —— 否则会出现'同一个 BOSS 刷两次'。\n" +
                "修法：要么把这间房的 roomType 改成 Boss，要么把它 enemyPrefabs 里的 " +
                "BOSS 预制体换成普通小怪。");
            return;
        }

        // 1) 固定 id（数据驱动）
        if (!string.IsNullOrEmpty(singleDefId))
        {
            SpawnFromDatabase(singleDefId, pos);
            return;
        }

        // 2) 数据库随机池
        //
        // 条件里加了 enemyPrefabs.Count == 0：只要房间显式配了预制体，就应该用它。
        // 原来的写法是 (useDatabasePool || 无预制体)，于是"同时勾了数据库池 + 配了 Boss 预制体"
        // 的房间会先生成随机杂兵并 return，Boss 永远不出现 —— 第二关的 Boss 房就是这么空掉的。
        if (enemyPrefabs.Count == 0 && useDatabasePool)
        {
            bool allowElite = roomType == RoomType.Elite || roomType == RoomType.Boss || GameBalance.BalanceFloor >= 4;
            float eliteChance = roomType == RoomType.Elite ? 0.75f : 0.12f;
            EnemyDef def = EnemyDatabase.PickRandom(allowElite, eliteChance);
            if (def != null)
            {
                SpawnFromDatabase(def.id, pos);
                return;
            }
        }

        // 3) 回退到场景里配的预制体
        if (enemyPrefabs.Count > 0)
        {
            GameObject prefab = enemyPrefabs[index % enemyPrefabs.Count];
            SpawnPrefab(prefab, pos);
        }
    }

    Vector3 GetSpawnPosition(int index)
    {
        if (enemySpawnPoints.Count > 0)
        {
            Vector3 basePos = enemySpawnPoints[index % enemySpawnPoints.Count].position;

            // 同一刷怪点出多只时散开，避免完全重叠
            float jitter = index / Mathf.Max(1, enemySpawnPoints.Count) * 1.2f;
            if (jitter > 0f)
                basePos += new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter), 0f);

            // 守卫：刷怪点必须落在本房间范围内。
            //
            // 踩过的坑：Level3 的 Room3（最终BOSS 皇）和 Room4（BOSS 官）共用了同一个
            // BossSpawn (40,10)，而它在 Room4 的范围里。于是"皇"刷到了隔壁房间，
            // 玩家在 Room3 里看不到敌人 → 房间永远清不掉 → 门永久关闭 →
            // AnotherCombatRoomActive() 又让后续所有房间都不刷怪，彻底卡死。
            //
            // 这里做一次兜底：刷怪点跑到房间外就改用房间中心，并报错提示去修场景。
            if (!IsInsideRoom(basePos))
            {
                Vector3 fallback = RoomCenter;
                Debug.LogError(
                    $"[房间] 「{name}」的刷怪点在房间范围之外（{basePos}），" +
                    $"已改用房间中心 {fallback} 刷怪。\n" +
                    $"  这会导致敌人刷到别的房间、本房间永远清不掉，请修正场景里 " +
                    $"enemySpawnPoints 的位置。{RoomBoundsText()}");
                return fallback;
            }

            return basePos;
        }

        return transform.position + new Vector3(Random.Range(-3f, 3f), Random.Range(-2f, 2f), 0f);
    }

    /// <summary>房间触发器的真实中心</summary>
    public Vector3 RoomCenter
    {
        get
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return transform.position;
            return (Vector2)transform.position + box.offset;
        }
    }

    /// <summary>某个世界坐标是否落在本房间范围内</summary>
    public bool IsInsideRoom(Vector3 worldPos)
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return true;   // 没有碰撞体就不做限制

        Vector2 center = (Vector2)transform.position + box.offset;
        Vector2 half = box.size * 0.5f;

        return worldPos.x >= center.x - half.x && worldPos.x <= center.x + half.x
            && worldPos.y >= center.y - half.y && worldPos.y <= center.y + half.y;
    }

    void SpawnFromDatabase(string defId, Vector3 pos)
    {
        GameObject template = ResolveEnemyTemplate();
        if (template == null)
        {
            Debug.LogError($"「{name}」找不到任何敌人预制体模板，无法生成 {defId}。" +
                           "请在 GameManager 上设置 enemyTemplatePrefab，或给房间配置 enemyPrefabs。");
            return;
        }

        GameObject enemy = Instantiate(template, pos, Quaternion.identity);
        Enemy e = enemy.GetComponent<Enemy>();
        if (e != null) e.defId = defId;

        SetupEnemy(enemy);
        aliveEnemies.Add(enemy);
    }

    // 依次尝试：房间自己配的 → GameManager 的模板 → 场景里其他房间用过的 → 场景里任意一个 Enemy
    GameObject ResolveEnemyTemplate()
    {
        if (enemyPrefabs.Count > 0 && enemyPrefabs[0] != null)
            return enemyPrefabs[0];

        if (GameManager.Instance != null && GameManager.Instance.enemyTemplatePrefab != null)
            return GameManager.Instance.enemyTemplatePrefab;

        // 场景里其他房间配置过的敌人预制体
        Room[] rooms = FindObjectsOfType<Room>();
        foreach (Room r in rooms)
        {
            if (r == this) continue;
            if (r.enemyPrefabs.Count > 0 && r.enemyPrefabs[0] != null)
                return r.enemyPrefabs[0];
        }

        // 最后兜底：场景里已经在用的任何一个敌人
        Enemy any = FindObjectOfType<Enemy>();
        if (any != null) return any.gameObject;

        return null;
    }

    void SpawnPrefab(GameObject prefab, Vector3 pos)
    {
        GameObject enemy = Instantiate(prefab, pos, Quaternion.identity);
        SetupEnemy(enemy);
        aliveEnemies.Add(enemy);
    }

    /// <summary>
    /// 给刚生成出来的敌人套上房间的配置（血量 / 移速 / 定义里的外形与弹幕）。
    ///
    /// 原来是 private，为了让实测工具能验证"这条链路算出来的血量是多少"，
    /// 改成 public —— 探测工具**直接调用真实逻辑**，而不是自己复刻一遍。
    /// （复刻过一次，结果改动没生效却显示"已修复"，白折腾一轮。）
    /// </summary>
    public void SetupEnemy(GameObject enemy)
    {
        Enemy e = enemy != null ? enemy.GetComponent<Enemy>() : null;
        if (e == null) return;

        e.SetRoom(this);

        // 血量：一条路径算完
        //   数据库 health × 全局倍率 × 层数倍率
        // 房间有显式覆盖（教程层的教学怪）时用覆盖值代替数据库那一段。
        e.EnsureDefinition();
        e.maxHealth = ResolveHealth(e);
        e.healthOverridden = true;

        e.moveSpeed *= GameManager.FloorSpeedMultiplier;

        // 血量已经算好，套用定义里其余属性（移速/射速/外形/底形/弹幕）时别再动它
        e.ApplyDefinition(true);

        // BOSS 用另一套血量规则：按玩家战力反推，让它落在目标时长区间
        ApplyBossHealth(enemy);
    }

    /// <summary>
    /// 算出这只怪最终该有多少血。
    ///
    /// **唯一真源是 EnemyDatabase**，不是预制体上序列化的值 ——
    /// 所有战斗房共用一个模板 Enemy_Bing（旧值 5 血），
    /// 读预制体的话你把数据库改到 400 也毫无作用（踩过这个坑）。
    /// </summary>
    int ResolveHealth(Enemy e)
    {
        float hpMul = GameManager.FloorHealthMultiplier;

        if (enemyHealthOverride > 0)
            return Mathf.Max(1, Mathf.RoundToInt(enemyHealthOverride * hpMul));

        if (e.def != null && e.def.health > 0)
            return Mathf.Max(1, Mathf.RoundToInt(Enemy.ScaledHealth(e.def.health) * hpMul));

        // 数据库里没这个 id → 退回预制体值并告警
        int fallback = Mathf.Max(1, e.maxHealth);
        Debug.LogWarning(
            $"「{name}」刷出的敌人 defId=\"{e.defId}\" 在数据库里找不到，" +
            $"退回预制体血量 {fallback}。检查 EnemyDatabase 里有没有这个 id。");
        return Mathf.Max(1, Mathf.RoundToInt(fallback * hpMul));
    }

    /// <summary>
    /// BOSS 血量：按玩家战力动态缩放。
    ///
    /// 目标时长已包含层数倍率（HealthForFight 内部会除掉倍率），
    /// 所以这里只做一次乘法，不要再把倍率也喂给 HealthForFight，
    /// 否则倍率会被平方放大 —— 曾经就是这么写出 170 秒 BOSS 战的。
    ///
    /// 最后统一乘 GameBalance.BossHealthMultiplier：测试期调小便于快速打到关底，
    /// 正式数值把它改回 1.0 即可，不用动这里的代码。
    /// </summary>
    void ApplyBossHealth(GameObject enemy)
    {
        float bossMul = Mathf.Max(0.01f, GameBalance.BossHealthMultiplier)
                      * GameBalance.BossHealthForFloor(GameData.currentFloor);

        // 教程层再削一刀：那儿的 BOSS 只是让玩家确认自己会打了，
        // 死一次就要重看一遍提示，是最劝退的体验之一。
        if (GameData.currentFloor <= 0)
            bossMul *= GameBalance.TutorialBossExtraMultiplier;

        BossShi bossScript = enemy.GetComponent<BossShi>();
        if (bossScript != null)
        {
            bossScript.SetRoom(this);
            bossScript.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossScript.maxHealth, 14f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier * bossMul));
        }

        BossGuan bossGuan = enemy.GetComponent<BossGuan>();
        if (bossGuan != null)
        {
            bossGuan.SetRoom(this);
            bossGuan.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossGuan.maxHealth, 16f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier * bossMul));
        }

        // 第三层关底 BOSS「皇」：最长的战斗，但也要有上限
        BossHuang bossHuang = enemy.GetComponent<BossHuang>();
        if (bossHuang != null)
        {
            bossHuang.SetRoom(this);
            bossHuang.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossHuang.maxHealth, 30f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier * bossMul));
        }
    }

    public void OnEnemyDied(GameObject enemy)
    {
        aliveEnemies.Remove(enemy);
        GameLog.Info($"「{name}」剩余敌人：{aliveEnemies.Count}");

        if (aliveEnemies.Count > 0) return;

        // 还有下一波
        if (currentWave < waveCount)
        {
            Invoke("StartNextWave", waveInterval);
            return;
        }

        ClearRoom();
    }

    void ClearRoom()
    {
        if (roomCleared) return;

        roomCleared = true;
        ReleaseDoors();

        GameLog.Info($"[门] 「{name}」清房释放后: {DoorInfo()}");

        Sfx.Play(Sfx.Id.RoomClear, 0.5f);
        Fx.RoomClearBurst(transform.position);
        Fx.Shake(0.20f);

        GameLog.Info($"「{name}」已清理！");
        GameToast.Show(GameText.Toast.房间清理完成(), new Color(0.6f, 1f, 0.7f));

        ActivatePortal();
        GrantClearReward();
    }

    /// <summary>
    /// 场景里预放的传送门（教程层用），一开始是隐藏的，清房后才亮出来。
    ///
    /// 为什么要有这个东西：
    /// 原来传送门是 BOSS 死亡时由 GameManager **运行时生成**的。
    /// 而教程层又在场景里**预放**了一个 —— 两个来源各来一个，
    /// 结果 BOSS 房里同时出现两个传送门，玩家反馈的就是这个 bug。
    ///
    /// 现在规矩是：**房间自己预放了就用房间的，GameManager 不再重复生成。**
    /// 好处是传送门的位置可以在编辑器里直接摆，不用靠代码算坐标。
    /// </summary>
    [Tooltip("场景里预放的传送门。填了它，BOSS 死亡时 GameManager 就不会再生成一个。")]
    public GameObject portal;

    void ActivatePortal()
    {
        if (portal == null) return;

        portal.SetActive(true);
        GameLog.Info($"「{name}」预放的传送门已激活。");
    }

    // 清房奖励：词条 + 可选笔画
    void GrantClearReward()
    {
        if (grantBoonOnClear)
        {
            BoonDef boon = BoonPool.Random();
            PlayerStats.Apply(boon);
            Sfx.Play(Sfx.Id.Boon, 0.5f);
            if (PlayerRef.Transform != null)
                Fx.SynthesisBurst(PlayerRef.Transform.position, boon.color);
            GameToast.Show(GameText.Toast.获得词条(boon.displayName, boon.description), boon.color);
        }

        if (spawnStrokeOnClear && GameManager.Instance != null)
        {
            // 数量优先用房间里配的值；没配（<=0）就按层数自动取，
            // 第 1 层给得多便于起手，后面逐层收紧。
            // 层数取 GameData.currentFloor 而不是缓存字段，避免切层后不同步。
            int count = bonusStrokesOnClear;
            if (count <= 0)
            {
                int floor = GameData.currentFloor;
                count = IsNonCombat
                    ? GameBalance.TreasureBonusStrokesForFloor(floor)
                    : GameBalance.CombatBonusStrokesForFloor(floor);
            }

            for (int i = 0; i < count; i++)
            {
                StrokeType t = (StrokeType)Random.Range(0, 6);
                Vector3 pos = transform.position + new Vector3(Random.Range(-2f, 2f), Random.Range(-1f, 1f), 0f);
                GameManager.Instance.SpawnStrokePickup(pos, t);
            }

            GameLog.Info($"「{name}」掉落 {count} 个笔画（第 {GameData.currentFloor} 层）");
        }
    }

    void OpenAllDoors()
    {
        foreach (Door door in doors)
        {
            if (door != null) door.Open();
        }
    }

    void CloseAllDoors()
    {
        foreach (Door door in doors)
        {
            if (door != null) door.Close();
        }
    }

    /// <summary>
    /// 把一个世界坐标钳制到本房间的可达范围内。
    ///
    /// 为什么需要：BOSS 掉落的传送门位置是 bossPos + (-2,0) 算出来的，而 BOSS
    /// 可能被配在房间边界之外。Level1 的 BossSpawn 就在 (24,0)，而 Boss 房的
    /// 实际范围只到 x=22 —— 传送门正好落在边界上，玩家（本身有碰撞半径）
    /// 够不到，于是卡在第二关之前无法传送。
    ///
    /// 房间触发器的真实中心是「transform 位置 + BoxCollider2D.offset」，
    /// 尺寸是 BoxCollider2D.size。这里按这个范围做钳制，并留出 inset 余量。
    /// </summary>
    public Vector3 ClampToRoom(Vector3 worldPos, float inset = 1.5f)
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return worldPos;

        Vector2 center = (Vector2)transform.position + box.offset;
        Vector2 half = box.size * 0.5f;

        // 房间太小时不要把 inset 用满，否则中心会跳位
        float insetX = Mathf.Min(inset, Mathf.Max(0f, half.x - 0.5f));
        float insetY = Mathf.Min(inset, Mathf.Max(0f, half.y - 0.5f));

        float minX = center.x - half.x + insetX;
        float maxX = center.x + half.x - insetX;
        float minY = center.y - half.y + insetY;
        float maxY = center.y + half.y - insetY;

        return new Vector3(
            Mathf.Clamp(worldPos.x, minX, maxX),
            Mathf.Clamp(worldPos.y, minY, maxY),
            worldPos.z);
    }

    /// <summary>房间可达范围的可读描述（诊断日志用）</summary>
    public string RoomBoundsText()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return "（无 BoxCollider2D，无法约束范围）";

        Vector2 center = (Vector2)transform.position + box.offset;
        Vector2 half = box.size * 0.5f;
        return $"房间范围 x=[{center.x - half.x:0.#},{center.x + half.x:0.#}] " +
               $"y=[{center.y - half.y:0.#},{center.y + half.y:0.#}]";
    }
}
