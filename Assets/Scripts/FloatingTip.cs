using UnityEngine;
using TMPro;

/// <summary>
/// 世界空间的飘字提示 —— 出现在对象**上方**，可以淡出，也可以常驻到手动清除。
///
/// ## 两种用途
///
///   1. **一次性提示**：拾取笔画时"恭喜你获得了「竖」"，升起 + 淡出
///      → 用 <see cref="Show"/>
///   2. **常驻提示**：靠近宝箱时的"按 F 打开宝箱"，玩家走开或开箱才消失
///      → 用 <see cref="ShowAt"/> 拿到一个 <see cref="Handle"/>，之后 <see cref="Clear"/>
///
/// ## 连续获得时立刻替换
///
/// 玩家要求"连续获得时会立刻跳到下一个笔画的提示"。
/// 所以 <see cref="Show"/> 会先把上一条**直接销毁**，不等它淡出 ——
/// 屏幕上永远只显示最新那一条。
///
/// （之前用的是"最多同时 4 条"的计数限制，连续拾取时会因为达到上限而
///   **不显示最新的那条** —— 正好和需求相反。）
/// </summary>
public static class FloatingTip
{
    /// <summary>一次性提示：升起多高</summary>
    const float RiseHeight = 1.1f;

    /// <summary>一次性提示：停留多久</summary>
    const float Lifetime = 1.1f;

    /// <summary>常驻提示的句柄（用来单独清掉某一条）</summary>
    public class Handle
    {
        internal GameObject go;
    }

    static GameObject current;

    /// <summary>把当前的一条立刻销毁（不等淡出）</summary>
    public static void ClearCurrent()
    {
        if (current != null) Object.Destroy(current);
        current = null;
    }

    // ============================================================
    //  一次性提示（拾取用）
    // ============================================================

    /// <summary>
    /// 在玩家（或指定对象）上方飘一行字，升起后淡出。
    /// **会替换掉上一条**，保证连续获得时立刻跳到新的那条。
    /// </summary>
    public static void Show(string text, Color color, Transform above = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        ClearCurrent();

        Transform anchor = above;
        if (anchor == null) anchor = PlayerRef.Transform;
        if (anchor == null) return;      // 没有玩家就不显示（比如编辑器里）

        GameObject go = CreateTip(text, color, anchor, 1.0f);
        if (go == null) return;

        current = go;
        go.AddComponent<Runner>().Init(Lifetime, RiseHeight, true);
    }

    // ============================================================
    //  常驻提示（靠近宝箱用）
    // ============================================================

    /// <summary>
    /// 在指定对象上方显示一条提示，**不自动消失**，直到调用 <see cref="Clear"/>。
    ///
    /// <paramref name="holdSeconds"/> 传正数时可以限时自动消失；
    /// 传 <c>float.PositiveInfinity</c> 表示一直留着（靠 Clear 收）。
    /// </summary>
    public static Handle ShowAt(string text, Color color, Transform anchor,
                                float height = 1.2f, float holdSeconds = float.PositiveInfinity)
    {
        if (string.IsNullOrEmpty(text) || anchor == null) return null;

        GameObject go = CreateTip(text, color, anchor, height);
        if (go == null) return null;

        var h = new Handle { go = go };
        go.AddComponent<Runner>().Init(holdSeconds, 0f, false);

        return h;
    }

    /// <summary>清掉一条常驻提示</summary>
    public static void Clear(Handle h)
    {
        if (h == null || h.go == null) return;
        Object.Destroy(h.go);
        h.go = null;
    }

    // ============================================================
    //  建对象
    // ============================================================

    static GameObject CreateTip(string text, Color color, Transform anchor, float height)
    {
        var go = new GameObject("FloatingTip");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = new Vector3(0f, height, -0.5f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 4.6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.sortingOrder = 120;
        tmp.fontStyle = FontStyles.Bold;

        // 抵消父物体的旋转与缩放（玩家的 transform 会水平翻转，不抵消字会镜像）
        var face = go.AddComponent<FaceCamera>();
        face.cancelParentRotation = true;
        face.cancelParentScale = true;

        return go;
    }

    /// <summary>驱动升起 + 淡出（或者只是"活着等 Clear"）</summary>
    class Runner : MonoBehaviour
    {
        TextMeshPro tmp;
        Vector3 startLocal;
        float t;
        float life;
        float rise;
        bool fading;

        public void Init(float lifetime, float riseHeight, bool fadeOut)
        {
            tmp = GetComponent<TextMeshPro>();
            startLocal = transform.localPosition;
            life = lifetime;
            rise = riseHeight;
            fading = fadeOut;
        }

        void Update()
        {
            // 常驻提示：不升起也不淡出，等 Clear
            if (!fading && float.IsPositiveInfinity(life)) return;

            t += Time.unscaledDeltaTime;

            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, life));

            if (rise > 0f)
            {
                // 先快后慢地升起（缓出）
                float up = rise * (1f - (1f - k) * (1f - k));
                transform.localPosition = startLocal + new Vector3(0f, up, 0f);
            }

            if (fading && tmp != null)
            {
                Color c = tmp.color;
                c.a = k < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, k);
                tmp.color = c;
            }

            if (k >= 1f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // 自然到期时清掉引用（被 ClearCurrent 销毁的情况已经清过了）
            if (current == gameObject) current = null;
        }
    }
}
