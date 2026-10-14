using UnityEngine;

/// <summary>
/// 玩家能力组件的运行时挂载器。
///
/// 为什么用代码挂载而不是往场景里手动添加：
///   这三个组件（笔画特点 / 护盾 / 斩击）都必须挂在玩家身上。手改三个场景的
///   YAML 很容易出错 —— 本项目已经因为手改场景文件踩过两次坑（一次把 Level2
///   写成了空场景，一次吞掉了 Room3 的组件块）。用运行时挂载既可靠，
///   也不用担心场景被改坏。
///
/// 用 AfterSceneLoad 而不是 BeforeSceneLoad：需要一个已经存在的玩家对象。
/// </summary>
public static class PlayerKit
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        EnsureAttached();
    }

    /// <summary>
    /// 确保玩家身上有这三个组件。HanziSystem 在切换汉字时也会调用它，
    /// 所以即使首次挂载晚了也不会漏掉特点初始化。
    /// </summary>
    public static void EnsureAttached()
    {
        GameObject player = PlayerRef.GameObject;
        if (player == null) return;

        if (player.GetComponent<StrokeTraits>() == null)
            player.AddComponent<StrokeTraits>();

        if (player.GetComponent<PlayerShield>() == null)
            player.AddComponent<PlayerShield>();

        if (player.GetComponent<MeleeSlash>() == null)
            player.AddComponent<MeleeSlash>();
    }
}
