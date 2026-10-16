using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("预制体")]
    public GameObject strokePickupPrefab;
    public GameObject portalPrefab;
    [Tooltip("数据驱动刷怪用的敌人模板（留空则由 Room 用场景里配的敌人预制体）")]
    public GameObject enemyTemplatePrefab;

    [Header("关卡")]
    public int currentFloor = 1;          // 只读视图，真实来源是 GameData.currentFloor

    [Header("难度成长（每层增量）")]
    public float healthGrowthPerFloor = 0.30f;   // 每层敌人血量 +30%
    public float speedGrowthPerFloor = 0.06f;    // 每层敌人速度 +6%
    public int extraEnemyPerFloor = 1;           // 每层额外 +1 只敌人
    public int maxExtraEnemies = 4;              // 额外敌人上限

    [Header("调试")]
    public bool logFloorInfo = true;

    // ---------- 跨层保持 ----------
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // 切换场景后重复出现的 GameManager：本场景的预制体引用更可靠，拷过去后销毁自己
            if (strokePickupPrefab != null) Instance.strokePickupPrefab = strokePickupPrefab;
            if (portalPrefab != null) Instance.portalPrefab = portalPrefab;
            if (enemyTemplatePrefab != null) Instance.enemyTemplatePrefab = enemyTemplatePrefab;
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        currentFloor = GameData.currentFloor;

        // 切场景后玩家对象是新的，缓存要失效
        PlayerRef.Invalidate();

        if (logFloorInfo)
        {
            GameLog.Info($"===== 进入第 {currentFloor} 层 =====");
            GameLog.Info($"敌人强度倍率：血量 x{FloorHealthMultiplier:0.00}  速度 x{FloorSpeedMultiplier:0.00}  额外敌人 +{ExtraEnemyCount}");
        }
    }

    // ---------- 层数难度倍率（Room / Enemy 生成时读取）----------
    /// <summary>
    /// 第 <see cref="GameBalance.BalanceFloor"/> 层的血量倍率。
    ///
    /// **注意用的是 BalanceFloor，不是 GameData.currentFloor。**
    ///
    /// 踩过的坑（玩家反馈"教程层小怪血量没跟着改、BOSS 血 400"）：
    /// 原来这里写的是 `1 + (GameData.currentFloor - 1) * per`，
    /// 而 currentFloor 是 **0 起**（教程层 = 0，第一层 = 1），
    /// 于是教程层算出 `1 + (0-1)*0.3 = **0.7**` —— 比第一层还弱 30%。
    ///
    /// 这一处算错会连带影响：
    ///   · 房间刷怪的血量（Room.ResolveHealth 乘的就是它）
    ///   · BOSS 血量（PlayerPower.HealthForFight 会先除以它，等于把它放大）
    ///
    /// 这就是"0 起下标"和"1 起公式"混用（index base mismatch）。
    /// BalanceFloor 就是专门为这件事存在的：把 0 起换算成 1 起。
    /// </summary>
    public static float FloorHealthMultiplier
    {
        get
        {
            GameManager gm = Instance;
            float per = gm != null ? gm.healthGrowthPerFloor : 0.30f;
            return 1f + (GameBalance.BalanceFloor - 1) * per;
        }
    }

    public static float FloorSpeedMultiplier
    {
        get
        {
            GameManager gm = Instance;
            // 同理用 BalanceFloor，别用 0 起的 currentFloor
            float per = gm != null ? gm.speedGrowthPerFloor : 0.06f;
            return 1f + (GameBalance.BalanceFloor - 1) * per;
        }
    }

    public static int ExtraEnemyCount
    {
        get
        {
            GameManager gm = Instance;
            int per = gm != null ? gm.extraEnemyPerFloor : 1;
            int cap = gm != null ? gm.maxExtraEnemies : 4;

            // 一样要用 BalanceFloor（见 FloorHealthMultiplier 的说明）。
            // 原来是 (currentFloor - 1)：教程层算出 -1，被 Min 夹成负数 ——
            // 目前靠 Room 那边再 Max(0, …) 兜住才没出问题，但那是巧合，不该依赖。
            return Mathf.Clamp((GameBalance.BalanceFloor - 1) * per, 0, cap);
        }
    }

    // ---------- 击杀回调 ----------
    public void OnEnemyKilled(Vector3 deathPos)
    {
        GameData.enemiesKilled++;
        // 普通小怪不掉落笔画——笔画只在宝箱和Boss掉落
    }

    public void OnBossKilled(Vector3 deathPos)
    {
        OnBossKilled(deathPos, null);
    }

    /// <summary>
    /// BOSS 死亡回调。room 用于把掉落物钳制到房间可达范围内 ——
    /// BOSS 可能被配在房间边界之外（Level1 的 BossSpawn 就在 x=24，而房间只到 x=22），
    /// 直接用 bossPos + offset 会把传送门放到墙外，玩家够不到就卡住无法进下一层。
    /// </summary>
    public void OnBossKilled(Vector3 deathPos, Room room)
    {
        GameData.bossesKilled++;
        GameLog.Info($"BOSS 被击败！（第 {GameData.currentFloor} 层）奖励：笔画 + 传送门");

        // Boss必掉随机笔画（6种）
        StrokeType[] allTypes = {
            StrokeType.Heng, StrokeType.Shu,
            StrokeType.Pie,  StrokeType.Na,
            StrokeType.Dian, StrokeType.Zhe
        };
        StrokeType randomType = allTypes[Random.Range(0, allTypes.Length)];

        Vector3 strokePos = deathPos + new Vector3(2, 0, 0);
        Vector3 portalPos = deathPos + new Vector3(-2, 0, 0);

        // 钳制到房间内：优先用 BOSS 死亡位置所在房间的类型/范围信息
        if (room != null)
        {
            Vector3 clampedStroke = room.ClampToRoom(strokePos);
            Vector3 clampedPortal = room.ClampToRoom(portalPos);

            // 诊断日志：传送门位置算错会导致玩家够不到、卡住无法进下一层，
            // 所以把原始值与钳制结果都打出来，便于定位坐标问题。
            GameLog.Info($"[传送门] BOSS死亡于 {deathPos}  房间={room.name}");
            GameLog.Info($"[传送门] 笔画 {strokePos} -> {clampedStroke}");
            GameLog.Info($"[传送门] 门   {portalPos} -> {clampedPortal}  {room.RoomBoundsText()}");

            strokePos = clampedStroke;
            portalPos = clampedPortal;
        }
        else
        {
            GameLog.Warn($"[传送门] 未拿到 Room 引用，传送门可能落在房间外：{portalPos}");
        }

        SpawnStrokePickup(strokePos, randomType);

        // 传送门：**房间已经预放了就不再生成一个**。
        //
        // 原来无条件 SpawnPortal()，而教程层在场景里预放了一个 ——
        // 于是 BOSS 房里出现两个传送门（玩家反馈的 bug）。
        //
        // 现在规矩：房间预放了 → 清房时它自己激活（见 Room.ActivatePortal）；
        //           房间没预放 → 这里生成。保证永远只有一个。
        if (room != null && room.portal != null)
        {
            GameLog.Info($"[传送门] 房间「{room.name}」已预放传送门，跳过运行时生成。");
            return;
        }

        SpawnPortal(portalPos);
    }

    void SpawnPortal(Vector3 pos)
    {
        if (portalPrefab == null)
        {
            Debug.LogError("Portal Prefab 未设置！请在 GameManager 检视面板中拖入。");
            return;
        }
        Instantiate(portalPrefab, pos, Quaternion.identity);
        GameLog.Info("传送门已生成！");
    }

    // ---------- 层数推进 ----------
    public void GoToNextFloor()
    {
        int from = GameData.currentFloor;

        GameData.currentFloor++;
        currentFloor = GameData.currentFloor;

        // 从教程层走进正式第一层：把玩家身上的东西全清掉。
        //
        // 必须在**切场景之前**清 —— 玩家一进 Level1，PlayerHealth.Start()
        // 就会读 carriedHealth 把教程里剩下的血补上、ElementSystem 也会带着
        // 教程吃的元素。等开局界面选完笔画再清就来不及了。
        //
        // 笔画和汉字那部分由 GameData.BeginRealRun 负责（选完起笔才算数），
        // 这里只管"人身上的"：血量、元素、词条加成。
        if (from <= 0 && GameData.currentFloor >= 1)
        {
            GameData.inTutorial = false;
            GameData.ClearCarriedPlayerState();
            GameLog.Info("离开教程层 —— 已清空携带状态，正式开局一无所有");
        }

        string target = SceneForFloor(currentFloor);
        GameLog.Info($"===== 前往第 {currentFloor} 层（{target}）=====");

        // 防止背包打开时 timeScale 残留为 0 导致新场景卡死
        Time.timeScale = 1f;

        SceneManager.LoadScene(target);
    }

    // 关卡编号 → 场景。**0 = 新手教程**，1/2/3 = 第一/二/三层，
    // 第 3 层之后循环 Level3，靠层数倍率继续提升难度。
    //
    // 真正的实现放在 GameBalance 里（那边是"所有数值与流程配置"的归属地），
    // 这里只做转发，避免两处各写一份、改一处漏一处。
    public static string SceneForFloor(int floor)
    {
        return GameBalance.SceneForFloor(floor);
    }

    // ---------- 死亡 / 重开 ----------
    /// <summary>
    /// 玩家死亡：回到**正式第一层**，保留开局选的起手笔画。
    ///
    /// 不回教程层 —— 教程是"学操作"的地方，死一次就要重看一遍提示太劝退。
    /// 但血量、元素、词条、路上捡的笔画全部清空，死亡就是从头再来。
    /// </summary>
    public void RestartRun()
    {
        GameLog.Info("玩家死亡 —— 清空进度，回到第 1 层");

        GameData.RespawnAtRealRun();             // 内部会把 currentFloor 设成 1
        currentFloor = GameData.currentFloor;

        Time.timeScale = 1f;

        SceneManager.LoadScene(SceneForFloor(currentFloor));
    }

    public void SpawnStrokePickup(Vector3 pos, StrokeType type)
    {
        if (strokePickupPrefab == null)
        {
            Debug.LogError("StrokePickup Prefab 未设置！");
            return;
        }
        GameObject pickup = Instantiate(strokePickupPrefab, pos, Quaternion.identity);
        StrokePickup sp = pickup.GetComponent<StrokePickup>();
        if (sp != null)
        {
            sp.strokeType = type;
        }
    }
}
