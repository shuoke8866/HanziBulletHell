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
        /// <summary>这条提示的对象（调用方可以改它的位置/缩放）</summary>
        public GameObject go;
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
        return ShowAtOffset(text, color, anchor, new Vector3(0f, height, -0.5f), 1f, holdSeconds);
    }

    /// <summary>
    /// 同 <see cref="ShowAt"/>，但可以**指定相对锚点的偏移和缩放**。
    ///
    /// 教程提示用这个：文字出现在提示点的**左上角**，并且缩小一点
    /// （教程文字要贴着那个小圆点，不能像拾取飘字那么大）。
    /// </summary>
    public static Handle ShowAtOffset(string text, Color color, Transform anchor,
                                      Vector3 localOffset, float scale,
                                      float holdSeconds = float.PositiveInfinity,
                                      bool faceCamera = true)
    {
        if (string.IsNullOrEmpty(text) || anchor == null) return null;

        GameObject go = CreateTip(text, color, anchor, 0f, faceCamera);
        if (go == null) return null;

        go.transform.localPosition = localOffset;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

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

    static GameObject CreateTip(string text, Color color, Transform anchor, float height,
                               bool faceCamera = true)
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

        // **亮字 + 深描边**：文字不管压在什么底色上都读得清。
        // 光调颜色会在不同底色上失效（教程气泡就是这么反复出问题的）。
        AddOutline(go, new Color(0.02f, 0.02f, 0.04f), 0.25f);

        // 抵消父物体的旋转与缩放（玩家的 transform 会水平翻转，不抵消字会镜像）
        //
        // faceCamera = false 用于**常驻提示**（比如教程文字）：
        // FaceCamera 每帧会 `transform.localScale = 初始父缩放 / 当前父缩放`，
        // 那会把调用方设的缩放**覆盖掉**。
        // 教程文字挂在会缩放的圆点下面（圆点在说话时放大 1.25 倍），
        // 所以它的字号一直被 FaceCamera 写回去 —— 我把 TextScale 从 0.62
        // 改成 1.0 却看不出任何变化，就是这个原因。
        if (faceCamera)
        {
            var face = go.AddComponent<FaceCamera>();
            face.cancelParentRotation = true;
            face.cancelParentScale = true;
        }

        return go;
    }

    /// <summary>给世界空间文字加深色描边（每个物体一份材质实例）</summary>
    static void AddOutline(GameObject go, Color outlineColor, float width)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp == null) return;

        Material mat = new Material(tmp.fontSharedMaterial);
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineWidth", width);

        tmp.fontMaterial = mat;
        tmp.characterSpacing = -1f;
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
