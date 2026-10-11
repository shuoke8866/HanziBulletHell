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
    public bool spawnStrokeOnClear = false;       // 额外掉落一个笔画
    public int bonusStrokesOnClear = 1;

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
        StartNextWave();
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
    void StartNextWave()
    {
        currentWave++;

        int extra = GameManager.ExtraEnemyCount + extraEnemiesPerWave;
        int baseCount = GetBaseEnemyCount();

        // 最后一波按类型加成
        if (roomType == RoomType.Elite) extra += 1;
        if (roomType == RoomType.Boss) extra = Mathf.Max(0, extra - 1);

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

        // 1) 固定 id（数据驱动）
        if (!string.IsNullOrEmpty(singleDefId))
        {
            SpawnFromDatabase(singleDefId, pos);
            return;
        }

        // 2) 数据库随机池
        if (useDatabasePool || enemyPrefabs.Count == 0)
        {
            bool allowElite = roomType == RoomType.Elite || roomType == RoomType.Boss || GameData.currentFloor >= 3;
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
            return basePos;
        }

        return transform.position + new Vector3(Random.Range(-3f, 3f), Random.Range(-2f, 2f), 0f);
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

    void SetupEnemy(GameObject enemy)
    {
        Enemy enemyScript = enemy.GetComponent<Enemy>();
        if (enemyScript != null)
        {
            enemyScript.SetRoom(this);

            float hpMul = GameManager.FloorHealthMultiplier;
            int scaledHealth = Mathf.Max(1, Mathf.RoundToInt(enemyScript.maxHealth * hpMul));

            if (enemyHealthOverride > 0)
                enemyScript.maxHealth = Mathf.Max(1, Mathf.RoundToInt(enemyHealthOverride * hpMul));
            else
                enemyScript.maxHealth = scaledHealth;

            // 防止 Enemy.Start() 的定义默认值把它重写回去
            enemyScript.healthOverridden = true;
            enemyScript.moveSpeed *= GameManager.FloorSpeedMultiplier;
        }

        // ---- BOSS 血量：按玩家战力动态缩放 ----
        //
        // 目标时长已包含层数倍率（HealthForFight 内部会除掉倍率），
        // 所以这里只做一次乘法，不要再把倍率也喂给 HealthForFight，
        // 否则倍率会被平方放大 —— 曾经就是这么写出 170 秒 BOSS 战的。

        BossShi bossScript = enemy.GetComponent<BossShi>();
        if (bossScript != null)
        {
            bossScript.SetRoom(this);
            bossScript.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossScript.maxHealth, 14f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier));
        }

        BossGuan bossGuan = enemy.GetComponent<BossGuan>();
        if (bossGuan != null)
        {
            bossGuan.SetRoom(this);
            bossGuan.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossGuan.maxHealth, 22f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier));
        }

        // 第三层关底 BOSS「皇」：最长的战斗，但也要有上限
        BossHuang bossHuang = enemy.GetComponent<BossHuang>();
        if (bossHuang != null)
        {
            bossHuang.SetRoom(this);
            bossHuang.maxHealth = Mathf.Max(1, Mathf.RoundToInt(
                PlayerPower.HealthForFight(bossHuang.maxHealth, 30f,
                    GameManager.FloorHealthMultiplier)
                * GameManager.FloorHealthMultiplier));
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

        Sfx.Play(Sfx.Id.RoomClear, 0.5f);
        Fx.RoomClearBurst(transform.position);
        Fx.Shake(0.20f);

        GameLog.Info($"「{name}」已清理！");
        GameToast.Show($"房间清理完成！", new Color(0.6f, 1f, 0.7f));

        GrantClearReward();
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
            GameToast.Show($"获得词条：{boon.displayName} —— {boon.description}", boon.color);
        }

        if (spawnStrokeOnClear && GameManager.Instance != null)
        {
            for (int i = 0; i < bonusStrokesOnClear; i++)
            {
                StrokeType t = (StrokeType)Random.Range(0, 6);
                Vector3 pos = transform.position + new Vector3(Random.Range(-2f, 2f), Random.Range(-1f, 1f), 0f);
                GameManager.Instance.SpawnStrokePickup(pos, t);
            }
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
}
