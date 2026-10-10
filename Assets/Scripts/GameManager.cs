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
    public static float FloorHealthMultiplier
    {
        get
        {
            GameManager gm = Instance;
            float per = gm != null ? gm.healthGrowthPerFloor : 0.30f;
            return 1f + (GameData.currentFloor - 1) * per;
        }
    }

    public static float FloorSpeedMultiplier
    {
        get
        {
            GameManager gm = Instance;
            float per = gm != null ? gm.speedGrowthPerFloor : 0.06f;
            return 1f + (GameData.currentFloor - 1) * per;
        }
    }

    public static int ExtraEnemyCount
    {
        get
        {
            GameManager gm = Instance;
            int per = gm != null ? gm.extraEnemyPerFloor : 1;
            int cap = gm != null ? gm.maxExtraEnemies : 4;
            return Mathf.Min((GameData.currentFloor - 1) * per, cap);
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
        GameData.bossesKilled++;
        GameLog.Info($"BOSS 被击败！（第 {GameData.currentFloor} 层）奖励：笔画 + 传送门");

        // Boss必掉随机笔画（6种）
        StrokeType[] allTypes = {
            StrokeType.Heng, StrokeType.Shu,
            StrokeType.Pie,  StrokeType.Na,
            StrokeType.Dian, StrokeType.Zhe
        };
        StrokeType randomType = allTypes[Random.Range(0, allTypes.Length)];
        SpawnStrokePickup(deathPos + new Vector3(2, 0, 0), randomType);

        // 生成传送门
        SpawnPortal(deathPos + new Vector3(-2, 0, 0));
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
        GameData.currentFloor++;
        currentFloor = GameData.currentFloor;

        string target = SceneForFloor(currentFloor);
        GameLog.Info($"===== 前往第 {currentFloor} 层（{target}）=====");

        // 防止背包打开时 timeScale 残留为 0 导致新场景卡死
        Time.timeScale = 1f;

        SceneManager.LoadScene(target);
    }

    // 第 1 层 → Level1，第 2 层 → Level2，第 3 层起 → Level3（之后循环 Level3 并靠倍率提升难度）
    public static string SceneForFloor(int floor)
    {
        if (floor <= 1) return "Level1";
        if (floor == 2) return "Level2";
        return "Level3";
    }

    // ---------- 死亡 / 重开 ----------
    // 真肉鸽：清空所有笔画，回到第 1 层
    public void RestartRun()
    {
        GameLog.Info("玩家死亡 —— 重置本轮进度，回到第 1 层");

        GameData.ResetAll();
        currentFloor = 1;

        Time.timeScale = 1f;

        SceneManager.LoadScene(SceneForFloor(1));
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
