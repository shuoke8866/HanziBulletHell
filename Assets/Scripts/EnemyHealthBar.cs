using UnityEngine;

/// <summary>
/// 小怪头顶的世界空间血条 —— 一条细横条，浮在敌人上方。
///
/// ## 为什么用世界空间而不是 Canvas
///
/// 敌人有几十个、还在移动。用世界空间 SpriteRenderer 的好处：
///   · 天然跟着敌人走，不用每帧做"世界坐标 → 屏幕坐标"的转换
///   · 不需要给每个敌人在 Canvas 上挂一个 UI 节点（那样层级会很乱）
///   · 可以直接用 sortingOrder 控制遮挡关系
///
/// ## 外观
///
/// 两层矩形：底色（暗红）+ 填充（绿→黄→红）。就两层，不加描边不加数字 ——
/// 小怪血条只是让玩家判断"这只还剩多少"，信息越少越好读。
///
/// 满血时**自动隐藏** —— 一群满血怪头顶全是条会很花。受伤了才出现。
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    // ---------- 尺寸（世界单位） ----------
    const float Width = 0.9f;
    const float Height = 0.09f;
    const float AboveOffset = 0.75f;

    SpriteRenderer back;
    SpriteRenderer fill;

    Enemy enemy;
    float shownRatio = 1f;
    bool built;

    /// <summary>满血时是否隐藏（小怪 true；BOSS 之类想一直显示就设 false）</summary>
    public bool hideWhenFull = true;

    /// <summary>挂到某个敌人身上</summary>
    public static EnemyHealthBar Attach(Enemy target)
    {
        if (target == null) return null;

        var go = new GameObject("HealthBar");
        go.transform.SetParent(target.transform, false);
        go.transform.localPosition = new Vector3(0f, AboveOffset, 0f);

        var bar = go.AddComponent<EnemyHealthBar>();
        bar.enemy = target;
        bar.Build();
        return bar;
    }

    void Build()
    {
        if (built) return;
        built = true;

        back = NewQuad("Back", new Color(0.12f, 0.05f, 0.06f, 0.9f), -1);
        back.transform.localScale = new Vector3(Width, Height, 1f);

        fill = NewQuad("Fill", new Color(0.4f, 0.9f, 0.45f, 1f), 0);

        // 填充用 pivot 在左边的矩形，改 scale.x 就能"从左往右减少"
        // —— 这里不用 Sprite 的 pivot，直接靠 localScale + localPosition 模拟。
        fill.transform.localScale = new Vector3(Width, Height * 0.72f, 1f);
        fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);   // 稍微靠前，盖住底色
    }

    SpriteRenderer NewQuad(string name, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SolidSprite.White;
        sr.color = color;
        sr.sortingOrder = 200 + order;   // 盖在敌人字形之上

        return sr;
    }

    void LateUpdate()
    {
        if (enemy == null) { Destroy(gameObject); return; }
        if (back == null || fill == null) return;

        int max = Mathf.Max(1, enemy.MaxHealth);
        int cur = Mathf.Clamp(enemy.CurrentHealth, 0, max);
        float target = (float)cur / max;

        // 平滑追赶：掉血时条会"唰"地缩下去，而不是瞬间消失
        shownRatio = Mathf.MoveTowards(shownRatio, target, Time.deltaTime * 3.5f);

        // 填充：宽度按比例，位置左对齐
        //
        // 做法：把矩形的中心往左移，让它始终从血条左端开始增长。
        // 中心偏移量 = (1 - 比例) * 半宽
        float w = Width * shownRatio;
        fill.transform.localScale = new Vector3(Mathf.Max(0.0001f, w), Height * 0.72f, 1f);
        fill.transform.localPosition = new Vector3(-(Width - w) * 0.5f, 0f, -0.01f);

        // 颜色：绿 → 黄 → 红
        if (target > 0.6f) fill.color = new Color(0.4f, 0.9f, 0.45f);
        else if (target > 0.3f) fill.color = new Color(0.95f, 0.78f, 0.30f);
        else fill.color = new Color(0.92f, 0.30f, 0.28f);

        // 满血隐藏（一群满血怪头顶全是条会很花）
        bool visible = !hideWhenFull || cur < max;
        if (back.enabled != visible)
        {
            back.enabled = visible;
            fill.enabled = visible;
        }

        // 血条不要跟着敌人翻转 —— 敌人朝向玩家时会 localScale.x 取反
        Vector3 s = transform.localScale;
        if (s.x < 0f) transform.localScale = new Vector3(-s.x, s.y, s.z);
    }
}
