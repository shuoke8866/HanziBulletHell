using System.Collections.Generic;

// 跨场景保存的游戏数据（静态类，不随场景销毁）
// 注意：所有 List 都"就地修改"，不要重新赋值整个 List——
// 否则其他系统持有的旧引用会指向已经被丢弃的列表。
public static class GameData
{
    // 当前汉字的笔画
    public static List<StrokeType> currentStrokes = new List<StrokeType> { StrokeType.Heng };

    // 背包笔画
    public static List<StrokeType> inventoryStrokes = new List<StrokeType>();

    // 当前元素
    public static ElementType currentElement = ElementType.None;

    // 当前层数（唯一真实来源，GameManager 只是它的视图）
    public static int currentFloor = 1;

    // 本轮统计
    public static int enemiesKilled = 0;
    public static int bossesKilled = 0;
    public static int strokesCollected = 0;

    // 跨层保留的当前生命（0 表示还没初始化，由 PlayerHealth 决定）
    public static int carriedHealth = 0;

    // 汉字歧义偏好：笔画组合 key → 玩家选过的汉字名
    // 例：横横竖 这个组合玩家选了"工"，以后凑齐横横竖就自动变"工"
    public static Dictionary<string, string> hanziPreferences = new Dictionary<string, string>();

    // 重置为新游戏（死亡或主动重开时调用）
    public static void ResetAll()
    {
        currentStrokes.Clear();
        currentStrokes.Add(StrokeType.Heng);

        inventoryStrokes.Clear();

        currentElement = ElementType.None;
        currentFloor = 1;

        enemiesKilled = 0;
        bossesKilled = 0;
        strokesCollected = 0;
        carriedHealth = 0;

        hanziPreferences.Clear();

        // 词条加成也要清掉
        PlayerStats.ResetAll();

        GameLog.Info("GameData 已重置为新游戏");
    }

    // 本轮进度摘要
    public static string RunSummary()
    {
        return $"第 {currentFloor} 层 | 笔画 {currentStrokes.Count} 画 | 背包 {inventoryStrokes.Count} | " +
               $"击杀 {enemiesKilled} 小怪 / {bossesKilled} BOSS";
    }
}
