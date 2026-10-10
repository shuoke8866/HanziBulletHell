using UnityEngine;

/// <summary>
/// 玩家引用缓存。
///
/// 背景：敌人/BOSS 原本各自在 Start 里调 GameObject.FindGameObjectWithTag("Player")，
/// 每生成一只怪就做一次全场景标签查找；场景里怪多时是明显的浪费。
/// 这里统一缓存，切场景时自动失效重建。
/// </summary>
public static class PlayerRef
{
    static Transform cached;

    /// <summary>玩家 Transform（找不到返回 null）</summary>
    public static Transform Transform
    {
        get
        {
            // Unity 的"空对象"语义：被销毁后 == null 为真，所以会自动重新查找
            if (cached != null) return cached;

            GameObject go = GameObject.FindGameObjectWithTag(GameBalance.PlayerTag);
            cached = go != null ? go.transform : null;
            return cached;
        }
    }

    /// <summary>玩家 GameObject</summary>
    public static GameObject GameObject
    {
        get
        {
            Transform t = Transform;
            return t != null ? t.gameObject : null;
        }
    }

    /// <summary>切场景时调用，避免指向已销毁的对象</summary>
    public static void Invalidate()
    {
        cached = null;
    }
}
