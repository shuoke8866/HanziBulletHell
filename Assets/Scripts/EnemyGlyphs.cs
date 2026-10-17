using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **敌人专用字库** —— 和玩家字库完全独立。
///
/// ## 为什么单独一个文件
///
/// 原来玩家和敌人共用一份字形数据（`HanziRenderer.defs`），
/// 后果是"改敌人的「木」"会**连带改掉玩家的「木」**。
/// 玩家要的是：敌人可以有自己的一套"残缺、混乱"的字，
/// 而且改它绝不影响自己操控的汉字。
///
/// 现在两套字库按名字分开（见 HanziRenderer.SetPlayer / SetEnemy）：
///
///     Assets/Scripts/HanziRenderer.cs      玩家的 49 个字
///     Assets/Scripts/EnemyGlyphs.cs        敌人的字（这个文件）
///
/// 两边可以用**同名**的字（都叫"木"），内容互不相干。
///
/// ## 怎么改敌人的字
///
/// 两个办法：
///   1. 用菜单 Tools/字形拖动编辑器 → 顶上把「字库」切到"敌人"，拖点保存
///   2. 直接改这个文件里的坐标（每个字一行，和玩家那套格式完全一样）
///
/// ## 关于"残缺"
///
/// 这个文件里的是**基础字形**。如果要"某些笔画直接消失"的残缺感，
/// 不用在这里删笔画 —— 在敌人定义器里调「残缺度」滑块就行
/// （那是生成贴图时按概率丢掉笔画，见 HanziRenderer.Rasterize）。
/// 两者可以叠加：这里画一个歪掉的底子，再叠残缺度。
/// </summary>
public partial class HanziRenderer
{
    /// <summary>
    /// 敌人字库的"标称字表"。
    ///
    /// 这是敌人**允许使用**的字 —— 敌人定义器的下拉框就列这些。
    /// 之所以要单独限定，是因为敌人字库是空的（等你去画），
    /// 如果直接用"字库里现有的字"，一开始会是空的、什么都选不了。
    ///
    /// 想给敌人加新字：往这个数组里加一个字，再在下面 BuildEnemyDefinitions
    /// 里用 Save("那个字") 画出来即可。
    /// </summary>
    public static readonly string[] EnemyGlyphNames =
    {
        // 与玩家字形库同步（共 64 个）
        "一", "丨", "丿", "乀", "丶", "乛", "二", "十", "人", "八", 
        "丁", "了", "刀", "三", "大", "工", "干", "官", "士", "土", 
        "口", "山", "川", "小", "上", "下", "王", "丰", "木", "天", 
        "太", "兀", "日", "中", "水", "少", "火", "主", "玉", "五", 
        "正", "白", "田", "目", "圭", "由", "兵", "里", "皇", "才", 
        "求", "永", "乐", "光", "先", "尖", "尘", "我", "利", "見", 
        "豆", "坐", "具", "胃"
    };

    /// <summary>
    /// 构建敌人字库。
    ///
    /// **现在故意留空** —— 敌人会回退到玩家字库显示（见 Enemy.SetGlyph），
    /// 所以游戏照常能玩。等你在字形编辑器里（字库切到"敌人"）画，
    /// 画了哪个字，敌人就自动改用你自己那版。
    ///
    /// 这样设计是为了满足"我自己设计就行"：不预先塞一堆我猜的形状给你，
    /// 你画一个就生效一个，没画的就还是原来的样子。
    /// </summary>
    static Dictionary<string, Poly[]> BuildEnemyDefinitions()
    {
        var d = new Dictionary<string, Poly[]>();

        // 笔画粗细和玩家那套保持一致，免得两边观感差太多
        const float T = 0.078f;
        const float TT = 0.058f;

        var L = new List<Poly>();

        // ---- 构造器（坐标 0~1，y 向上，和玩家字库完全一样的写法）----
        System.Action<List<Poly>, float, float, float> H =
            (l, x0, x1, y) => l.Add(new Poly(new[] { new Vector2(x0, y), new Vector2(x1, y) }, T));

        System.Action<List<Poly>, float, float, float> V =
            (l, x, y0, y1) => l.Add(new Poly(new[] { new Vector2(x, y0), new Vector2(x, y1) }, T));

        System.Action<List<Poly>, float, float, float, float> D =
            (l, x0, y0, x1, y1) => l.Add(new Poly(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, T));

        System.Action<List<Poly>, float, float, float, float> DS =
            (l, x0, y0, x1, y1) => l.Add(new Poly(new[] { new Vector2(x0, y0), new Vector2(x1, y1) }, TT));

        System.Action<List<Poly>, float, float, float, float, float, float> Z =
            (l, x0, y0, x1, y1, x2, y2) => l.Add(new Poly(new[]
            {
                new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x2, y2)
            }, T));

        System.Action<string> Save = key =>
        {
            d[key] = L.ToArray();
            L = new List<Poly>();
            sets[buildingSet] = d;
        };

        // ============================================================
        //  在这里画敌人的字。
        //
        //  格式和玩家字库一模一样，照抄一个改坐标即可：
        //
        //  H(列表, x0, x1, y)              横线：从 x0 到 x1，高度 y
        //  V(列表, x, y0, y1)              竖线：在 x，从 y0 到 y1
        //  D(列表, x0, y0, x1, y1)         斜线/撇捺
        //  DS(列表, x0, y0, x1, y1)        细斜线（点、短撇）
        //  Z(列表, x0,y0, x1,y1, x2,y2)    折：三点折线
        //  P3/P4/P5(列表, 粗细, x0,y0, ...) 任意折线（3~5 个点）
        //  AddPoly(列表, 粗细, x0,y0, ...)  任意点数的折线（超过 5 个点用这个）
        //
        //  坐标范围 0~1，(0,0) 在**左下角**，y 向上。
        //  参考网格：竖线 x = 0.10 / 0.30 / 0.50 / 0.70 / 0.90，横线 y 同理。
        //  平行笔画间距建议 ≥ 0.12（笔宽 0.078，太近会糊在一起）。
        // ============================================================

        // （下面是你画的敌人字形 —— 用 Tools/字形编辑器 的「敌人」字库继续改）

        sets[buildingSet] = d;
        return d;
    }
}
