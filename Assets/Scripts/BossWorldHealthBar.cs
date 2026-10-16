using UnityEngine;

/// <summary>
/// BOSS 头顶的世界空间血条 —— **和小兵血条（EnemyHealthBar）完全一样的做法**。
///
/// ## 为什么要照抄小兵血条
///
/// BOSS 血条原来用 Canvas（屏幕空间 UI），反复出问题：
///   · 换场景不重建 / 被开场故事盖住 / 没打 BOSS 却一直显示
///   · 每修一次冒出新的问题，来回好几轮都没弄对
///
/// 而小兵血条（EnemyHealthBar）**一直是好的**，因为它简单：
///   · 世界空间，两个 SpriteRenderer 方块（底色 + 填充）
///   · 挂在敌人身上，天然跟着走，不用做任何坐标转换
///   · 没有 Canvas、没有 DontDestroyOnLoad、没有场景查找
///   · 敌人死了它跟着销毁 —— 生命周期天然正确
///
/// 所以 BOSS 血条也这么做：**挂到 BOSS 身上**。
/// BOSS 在场景里生成 → 血条跟着出现；BOSS 死了 → 血条跟着消失。
/// **不需要任何"什么时候显示"的判定逻辑** —— 它在不在，取决于 BOSS 在不在。
///
/// ## 和小兵血条的区别
///
///   · 尺寸大一些（3.2 × 0.30，小兵是 0.9 × 0.09）
///   · 位置高一些（头顶 2.2，小兵是 0.75）
///   · `hideWhenFull = false` —— 满血也显示（BOSS 战需要一直看到它的血量）
///   · 多一行名字
/// </summary>
public class BossWorldHealthBar : MonoBehaviour
{
    // ---------- 尺寸（世界单位） ----------
    const float Width = 3.2f;
    const float Height = 0.30f;
    const float AboveOffset = 2.2f;

    /// <summary>满血时是否隐藏。BOSS 设 false（全程显示）</summary>
    public bool hideWhenFull = false;

    /// <summary>显示的名字（空 = 不显示）</summary>
    public string displayName = "";

    /// <summary>名字的字号</summary>
    public float nameFontSize = 10f;

    SpriteRenderer back;
    SpriteRenderer fill;
    TMPro.TextMeshPro nameText;

    IDamageable target;
    float shownRatio = 1f;
    bool built;

    /// <summary>
    /// 挂到任意可受伤对象上。用法：
    ///     BossWorldHealthBar.Attach(gameObject, this as IDamageable, "皇");
    /// </summary>
    public static BossWorldHealthBar Attach(GameObject host, IDamageable t, string name)
    {
        if (host == null || t == null) return null;

        var go = new GameObject("BossHealthBar");
        go.transform.SetParent(host.transform, false);
        go.transform.localPosition = new Vector3(0f, AboveOffset, 0f);

        var bar = go.AddComponent<BossWorldHealthBar>();
        bar.target = t;
        bar.displayName = name;
        bar.Build();

        return bar;
    }

    void Build()
    {
        if (built) return;
        built = true;

        // 底色（暗红）
        back = NewQuad("Back", new Color(0.12f, 0.05f, 0.06f, 0.92f), -1);
        back.transform.localScale = new Vector3(Width, Height, 1f);

        // 填充（从左往右减少）
        fill = NewQuad("Fill", new Color(0.92f, 0.30f, 0.28f, 1f), 0);
        fill.transform.localScale = new Vector3(Width, Height * 0.74f, 1f);
        fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);

        // 名字（世界空间文字，和 FloatingTip 同一套做法 —— 那套是能用的）
        if (!string.IsNullOrEmpty(displayName))
        {
            var nameGo = new GameObject("BossName");
            nameGo.transform.SetParent(transform, false);
            nameGo.transform.localPosition = new Vector3(0f, Height * 0.5f + 0.32f, -0.1f);

            nameText = nameGo.AddComponent<TMPro.TextMeshPro>();
            nameText.text = displayName;
            nameText.fontSize = nameFontSize;
            nameText.alignment = TMPro.TextAlignmentOptions.Center;
            nameText.fontStyle = TMPro.FontStyles.Bold;
            nameText.sortingOrder = 202;

            // **亮字 + 深描边** —— 不管背景是什么色都读得清。
            // （浮字只有"亮字+描边"是稳的，纯改颜色会在不同底色上失效。）
            nameText.color = new Color(1f, 0.94f, 0.78f);
            AddOutline(nameGo, new Color(0.02f, 0.02f, 0.04f), 0.25f);

            // 抵消父物体的缩放，字才不会被拉扁
            var fc = nameGo.AddComponent<FaceCamera>();
            fc.cancelParentRotation = true;
            fc.cancelParentScale = true;
        }
    }

    SpriteRenderer NewQuad(string name, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SolidSprite.White;
        sr.color = color;
        sr.sortingOrder = 200 + order;

        return sr;
    }

    static void AddOutline(GameObject go, Color outlineColor, float width)
    {
        var tmp = go.GetComponent<TMPro.TextMeshPro>();
        if (tmp == null) return;

        Material mat = new Material(tmp.fontSharedMaterial);
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineWidth", width);

        tmp.fontMaterial = mat;
        tmp.characterSpacing = -1f;
    }

    void LateUpdate()
    {
        // 目标没了（BOSS 死了被销毁）→ 自己也消失
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        if (back == null || fill == null) return;

        int max = Mathf.Max(1, target.MaxHealth);
        int cur = Mathf.Clamp(target.CurrentHealth, 0, max);
        float ratio = (float)cur / max;

        shownRatio = Mathf.MoveTowards(shownRatio, ratio, Time.deltaTime * 3.5f);

        // 填充宽度按比例，左对齐
        float w = Width * shownRatio;
        fill.transform.localScale = new Vector3(Mathf.Max(0.0001f, w), Height * 0.74f, 1f);
        fill.transform.localPosition = new Vector3(-(Width - w) * 0.5f, 0f, -0.01f);

        // 颜色：绿 → 黄 → 红
        if (ratio > 0.6f) fill.color = new Color(0.4f, 0.9f, 0.45f);
        else if (ratio > 0.3f) fill.color = new Color(0.95f, 0.78f, 0.30f);
        else fill.color = new Color(0.92f, 0.30f, 0.28f);

        bool visible = !hideWhenFull || cur < max;
        if (back.enabled != visible)
        {
            back.enabled = visible;
            fill.enabled = visible;

            if (nameText != null) nameText.enabled = visible;
        }

        // 血条不要跟着 BOSS 翻转
        Vector3 s = transform.localScale;
        if (s.x < 0f) transform.localScale = new Vector3(-s.x, s.y, s.z);
    }
}
