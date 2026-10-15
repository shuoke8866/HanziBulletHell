using System.Collections.Generic;
using UnityEngine;

// ============ 敌人行为 ============
public enum EnemyBehavior
{
    Chase,      // 直线追击
    ChaseShoot, // 追击 + 保持距离
    Orbit,      // 环绕玩家（不远不近）
    Charge,     // 冲刺撞击
    Turret      // 固定不动，只射击
}

// ============ 敌人外形 ============
/// <summary>
/// 敌人本体的底形。
///
/// 原来敌人**只有身上的汉字**（Enemy.ApplyDefinition 里把 SpriteRenderer 关掉了），
/// 于是外形只能在"体型大小 + 颜色 + 写什么字"里调，种类一多就分不清。
///
/// 现在加一层程序生成的底形垫在汉字后面 —— 不需要任何美术资源，
/// 用 ProcTex 现场画多边形。
/// </summary>
public enum EnemyBodyShape
{
    None,       // 只有汉字，不加底形（原来的样子）
    Circle,
    Square,
    Diamond,
    Triangle,
    Hexagon,
    Star,
    Ring        // 空心圆环
}

// ============ 弹幕形态 ============
public enum EnemyShotPattern
{
    None,
    Aimed,      // 朝玩家单发
    DoubleAimed,// 朝玩家双发（带夹角）
    Fan,        // 扇形散射
    Ring,       // 环形
    Spiral,     // 螺旋（每次旋转一个固定角度）
    Wall,       // 弹幕墙（一排平行弹）
    Twin,       // 上下对射
    Burst       // 朝玩家三连点射
}

// ============ 敌人定义（数据驱动）============
[System.Serializable]
public class EnemyDef
{
    public string id;                       // 唯一标识，用于生成
    public string displayName;              // 中文显示名
    public EnemyBehavior behavior;
    public EnemyShotPattern shotPattern;

    public int health = 3;
    public float moveSpeed = 2f;
    public float shootInterval = 2f;
    public float bulletSpeed = 7f;
    public int bulletDamage = 1;

    public float meleeDamage = 0f;          // >0 表示接触伤害

    public Vector3 bodyScale = new Vector3(0.8f, 0.8f, 1f);
    public Color bodyColor = new Color(1f, 1f, 1f, 1f);

    // ---------- 外形（程序生成，不需要美术资源）----------
    /// <summary>汉字后面的底形</summary>
    public EnemyBodyShape bodyShape = EnemyBodyShape.None;

    /// <summary>底形颜色（None 时无效）</summary>
    public Color shapeColor = new Color(0.35f, 0.38f, 0.5f, 0.9f);

    /// <summary>底形相对本体的尺寸倍率</summary>
    public float shapeScale = 1.35f;

    /// <summary>底形缓慢自转速度（度/秒），0 = 不转</summary>
    public float shapeSpin = 0f;

    // ---------- 弹幕：可选"复用玩家的形态原语" ----------
    /// <summary>
    /// 勾上之后不用 <see cref="shotPattern"/>，改用
    /// <see cref="shapeTags"/> + <see cref="shotCount"/> 描述弹幕 ——
    /// 和玩家汉字用的是**同一套原语**（见 BulletShapes）。
    ///
    /// 这样敌人弹幕和玩家弹幕共享一套实现，加新形态两边同时受益。
    /// 不勾就还是用老的 7 种固定形态。
    /// </summary>
    public bool usePlayerShapes = false;

    /// <summary>形态标签（straight / ring / scatter ... 见 BulletShapes.All）</summary>
    public List<string> shapeTags = new List<string>();

    /// <summary>一轮打几发（usePlayerShapes 时是权威总数）</summary>
    public int shotCount = 3;

    /// <summary>子弹尺寸倍率</summary>
    public float shotScale = 1f;

    public int scoreWeight = 1;             // 权重（用于随机抽取）
    public bool isElite = false;
    public bool canSummon = false;          // 召唤小怪
    public string summonId;                 // 召唤的目标 id
    public float summonInterval = 8f;

    public string glyph = "一";             // 身上显示的汉字

    /// <summary>
    /// 从第几层开始会刷出这种怪（1 = 第一层就有）。
    ///
    /// 加这个字段的原因：原来 PickRandom 是从**整个数据库**等权重抽，
    /// 于是第一层就可能刷出"弹幕墙怪"（24 血）和"精英·火"（45 血、冲锋），
    /// 难度完全随机、没有成长感。
    ///
    /// 现在按层分层引入：
    ///   第 1 层  横线怪 / 竖线怪 / 方块怪        —— 认弹幕、学会走位
    ///   第 2 层  + 冲刺怪 / 炮台怪 / 兵          —— 开始有位置压力
    ///   第 3 层  + 环绕怪 / 弹幕墙怪 / 精英      —— 真正的弹幕地狱
    /// </summary>
    public int minFloor = 1;

    /// <summary>
    /// 允许"越级"出现的概率。0 = 严格按 minFloor 过滤。
    ///
    /// 留一点随机性能避免每层敌人组合完全固定（那样第二周目就很无聊）。
    /// 默认 0.12：偶尔提前遇到强怪，但不至于第一层就被弹幕墙糊脸。
    /// </summary>
    public static float EarlyAppearChance = 0.12f;
}

/// <summary>
/// 敌人数据库：所有敌人类型集中定义，不再硬编码在 Enemy.cs 里。
/// 想加新怪只需在 Build() 里加一条，或调用 Register() 注册（含精英/变异倍率）。
/// </summary>
public static class EnemyDatabase
{
    static List<EnemyDef> all;
    static Dictionary<string, EnemyDef> byId;

    public static IReadOnlyList<EnemyDef> All
    {
        get { EnsureBuilt(); return all; }
    }

    public static void EnsureBuilt()
    {
        if (all != null) return;
        all = new List<EnemyDef>();
        byId = new Dictionary<string, EnemyDef>();
        Build();
    }

    public static void Register(EnemyDef def)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(def.id)) def.id = def.displayName;
        if (byId.ContainsKey(def.id))
        {
            // 覆盖旧定义（方便热改）
            int i = all.FindIndex(d => d.id == def.id);
            if (i >= 0) all[i] = def;
            byId[def.id] = def;
            return;
        }
        all.Add(def);
        byId[def.id] = def;
    }

    public static EnemyDef Get(string id)
    {
        EnsureBuilt();
        EnemyDef d;
        if (byId.TryGetValue(id, out d)) return d;
        Debug.LogWarning($"EnemyDatabase: 找不到敌人定义「{id}」，回退到第一个。");
        return all.Count > 0 ? all[0] : null;
    }

    // 按权重随机抽一个（可排除精英）
    //
    // 会按 GameData.currentFloor 过滤：只从 minFloor <= 当前层 的怪里抽。
    // 另有 EarlyAppearChance 的概率放行一只"越级"的强怪，避免每层组合完全固定。
    public static EnemyDef PickRandom(bool allowElite, float eliteChance = 0.15f)
    {
        EnsureBuilt();

        int floor = GameData.currentFloor;

        // 先按层数筛出可用池
        var allowed = all.FindAll(d => d.minFloor <= floor);

        // 兜底：如果这一层一个都没有，就退到"minFloor 最小的那批"，
        // **不能**直接放行全部 —— 那样等于分层完全失效。
        //
        // 踩过的坑：教程层的 floor 是 0，而所有怪的 minFloor 都 >= 1，
        // 过滤后必然为空；原来的 `if (allowed.Count == 0) allowed = all;`
        // 于是把 11 种怪（含精英和弹幕墙怪）全放进了新手教程。
        if (allowed.Count == 0)
        {
            int lowest = int.MaxValue;
            foreach (var d in all) if (d.minFloor < lowest) lowest = d.minFloor;

            allowed = all.FindAll(d => d.minFloor == lowest);
            if (allowed.Count == 0) allowed = all;   // 真的一个定义都没有才会走到
        }

        // 偶尔放行越级的怪（从"本来还不到时候"的那批里挑）。
        //
        // 教程层（floor <= 0）**完全不放行** —— 那一层是让新手学基本操作的，
        // 突然冒出一只精英或弹幕墙怪是很糟的体验。
        if (floor > 0 && allowed.Count < all.Count &&
            UnityEngine.Random.value < EnemyDef.EarlyAppearChance)
        {
            var future = all.FindAll(d => d.minFloor > floor);
            if (future.Count > 0) allowed.Add(future[UnityEngine.Random.Range(0, future.Count)]);
        }

        if (allowElite && UnityEngine.Random.value < eliteChance)
        {
            var elites = allowed.FindAll(d => d.isElite);
            if (elites.Count > 0) return elites[UnityEngine.Random.Range(0, elites.Count)];
        }

        var pool = allowed.FindAll(d => !d.isElite);
        if (pool.Count == 0) pool = allowed;

        int total = 0;
        foreach (var d in pool) total += Mathf.Max(1, d.scoreWeight);

        int pick = UnityEngine.Random.Range(0, total);
        foreach (var d in pool)
        {
            pick -= Mathf.Max(1, d.scoreWeight);
            if (pick < 0) return d;
        }
        return pool[pool.Count - 1];
    }

    // ---------- 定义区 ----------
    static void Build()
    {
        // ===== 基础怪 =====

        // 一：横线怪，直线追击 + 单发瞄准
        Register(new EnemyDef
        {
            id = "yi",
            minFloor = 1,
            displayName = "横线怪",
            glyph = "二",
            behavior = EnemyBehavior.Chase,
            shotPattern = EnemyShotPattern.Aimed,
            health = 24, moveSpeed = 2f, shootInterval = 2f,
            bulletSpeed = 7f, bulletDamage = 1,
            bodyScale = new Vector3(2f, 0.5f, 1f),
            bodyColor = new Color(0.9f, 0.9f, 0.9f),
            scoreWeight = 4
        });

        // 丨：竖线怪，追击 + 上下对射
        Register(new EnemyDef
        {
            id = "shu",
            minFloor = 1,
            displayName = "竖线怪",
            glyph = "川",
            behavior = EnemyBehavior.Chase,
            shotPattern = EnemyShotPattern.Twin,
            health = 24, moveSpeed = 2f, shootInterval = 2.2f,
            bulletSpeed = 7f, bulletDamage = 1,
            bodyScale = new Vector3(0.5f, 1f, 1f),
            bodyColor = new Color(0.7f, 0.95f, 1f, 1f),
            scoreWeight = 4
        });

        // 口：方块怪，慢速逼近 + 环形弹幕
        Register(new EnemyDef
        {
            id = "kou",
            minFloor = 1,
            displayName = "方块怪",
            glyph = "口",
            behavior = EnemyBehavior.ChaseShoot,
            shotPattern = EnemyShotPattern.Ring,
            health = 41, moveSpeed = 1f, shootInterval = 3f,
            bulletSpeed = 6f, bulletDamage = 1,
            bodyScale = new Vector3(1.5f, 1.5f, 1f),
            bodyColor = new Color(1f, 0.85f, 0.4f),
            scoreWeight = 3
        });

        // 人：快速怪，冲刺撞击
        Register(new EnemyDef
        {
            id = "ren",
            minFloor = 2,
            displayName = "冲刺怪",
            glyph = "人",
            behavior = EnemyBehavior.Charge,
            shotPattern = EnemyShotPattern.None,
            health = 36, moveSpeed = 4f, shootInterval = 1.5f,
            bulletSpeed = 8f, bulletDamage = 1,
            meleeDamage = 1f,
            bodyScale = new Vector3(0.8f, 0.8f, 1f),
            bodyColor = new Color(1f, 0.6f, 0.3f),
            scoreWeight = 4
        });

        // 十：炮台怪，固定不动 + 十字弹幕
        Register(new EnemyDef
        {
            id = "shi",
            minFloor = 2,
            displayName = "炮台怪",
            glyph = "丁",
            behavior = EnemyBehavior.Turret,
            shotPattern = EnemyShotPattern.Fan,
            health = 80, moveSpeed = 0f, shootInterval = 2.4f,
            bulletSpeed = 8f, bulletDamage = 1,
            bodyScale = new Vector3(1.1f, 1.1f, 1f),
            bodyColor = new Color(0.85f, 0.5f, 1f),
            scoreWeight = 2
        });

        // 木：环绕怪，保持中距离 + 螺旋弹幕
        Register(new EnemyDef
        {
            id = "mu",
            minFloor = 3,
            displayName = "环绕怪",
            glyph = "木",
            behavior = EnemyBehavior.Orbit,
            shotPattern = EnemyShotPattern.Spiral,
            health = 96, moveSpeed = 2.4f, shootInterval = 0.45f,
            bulletSpeed = 5.5f, bulletDamage = 1,
            bodyScale = new Vector3(1f, 1f, 1f),
            bodyColor = new Color(0.5f, 0.9f, 0.5f),
            scoreWeight = 2
        });

        // 田：弹幕墙怪，慢速 + 一排平行弹
        Register(new EnemyDef
        {
            id = "tian",
            minFloor = 3,
            displayName = "弹幕墙怪",
            glyph = "田",
            behavior = EnemyBehavior.ChaseShoot,
            shotPattern = EnemyShotPattern.Wall,
            health = 161, moveSpeed = 0.8f, shootInterval = 3.2f,
            bulletSpeed = 5f, bulletDamage = 1,
            bodyScale = new Vector3(1.6f, 1.6f, 1f),
            bodyColor = new Color(0.6f, 1f, 0.85f),
            scoreWeight = 2
        });

        // ===== 精英怪（同一套行为 + 强化数值）=====

        // 王：精英方块怪，召唤小怪
        Register(new EnemyDef
        {
            id = "elite_wang",
            minFloor = 3,
            displayName = "精英·王",
            glyph = "王",
            behavior = EnemyBehavior.ChaseShoot,
            shotPattern = EnemyShotPattern.Ring,
            health = 214, moveSpeed = 1.3f, shootInterval = 2.2f,
            bulletSpeed = 6.5f, bulletDamage = 1,
            bodyScale = new Vector3(2f, 2f, 1f),
            bodyColor = new Color(1f, 0.4f, 0.4f),
            isElite = true, canSummon = true, summonId = "ren", summonInterval = 6f,
            scoreWeight = 1
        });

        // 火：精英冲刺怪，三连点射
        Register(new EnemyDef
        {
            id = "elite_huo",
            minFloor = 3,
            displayName = "精英·火",
            glyph = "圭",
            behavior = EnemyBehavior.Charge,
            shotPattern = EnemyShotPattern.Burst,
            health = 188, moveSpeed = 4.5f, shootInterval = 2.5f,
            bulletSpeed = 9f, bulletDamage = 1,
            meleeDamage = 2f,
            bodyScale = new Vector3(1.1f, 1.1f, 1f),
            bodyColor = new Color(1f, 0.45f, 0.2f),
            isElite = true,
            scoreWeight = 1
        });

        // 水：精英环绕怪，双瞄准
        Register(new EnemyDef
        {
            id = "elite_shui",
            minFloor = 3,
            displayName = "精英·水",
            glyph = "水",
            behavior = EnemyBehavior.Orbit,
            shotPattern = EnemyShotPattern.DoubleAimed,
            health = 188, moveSpeed = 3f, shootInterval = 1.6f,
            bulletSpeed = 7.5f, bulletDamage = 1,
            bodyScale = new Vector3(1.2f, 1.2f, 1f),
            bodyColor = new Color(0.4f, 0.7f, 1f),
            isElite = true,
            scoreWeight = 1
        });

        // ===== 第三层：皇 的士兵 =====

        // 兵：被"皇"召唤出来的士兵，列队缓慢推进，只做近战接触伤害
        Register(new EnemyDef
        {
            id = "bing",
            minFloor = 2,
            displayName = "兵",
            glyph = "兵",
            behavior = EnemyBehavior.Chase,
            shotPattern = EnemyShotPattern.Aimed,
            health = 200, moveSpeed = 2.2f, shootInterval = 2.8f,
            bulletSpeed = 5.5f, bulletDamage = 1,
            meleeDamage = 1f,
            bodyScale = new Vector3(1f, 1f, 1f),
            bodyColor = new Color(0.85f, 0.85f, 0.95f, 1f),
            scoreWeight = 0            // 0 = 不会被随机池抽到，只能被召唤
        });
    }
}
