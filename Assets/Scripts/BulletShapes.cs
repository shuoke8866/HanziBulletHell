using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹幕形态原语 —— **纯计算，不碰场景**。
///
/// ## 为什么单独抽出来
///
/// 原来"算方向/位置"和"Instantiate 子弹"是写死在一起的
/// （HanziSystem.SpawnShape 里 switch 完直接 SpawnBullet）。
/// 结果是：
///   · 编辑器里想预览弹幕，没法算 —— 一算就会真的生成子弹
///   · 敌人想用同一套形态，得把那段 switch 复制一遍（然后两份必然漂移）
///
/// 现在拆成两层：
///   BulletShapes.Compute(...)  只算"每一发子弹从哪、往哪、多快、多大"
///   调用方自己决定怎么用这些结果（生成子弹 / 画到预览纹理 / 发给敌人）
///
/// ## 形态一览（数值与线上完全一致，改这里就是改游戏）
///
///   straight  单发直线
///   parallel  n 发平行，垂直方向铺开 ±0.4
///   cross     +90° 依次旋转（4 发成十字）
///   vshape    ±45° 交替
///   diagonal  30 / -30 / 150 / -150° 循环
///   scatter   −40°~+40° 均分
///   omni      360° 均分
///   ring      360° 均分，速度 ×0.7、尺寸 ×0.8（环形弹幕慢一点、小一点）
///   shotgun   −15°~+15° 均分，速度 ×0.6、尺寸 ×0.8（近距霰弹）
/// </summary>
public static class BulletShapes
{
    /// <summary>一发子弹的完整参数</summary>
    public struct Shot
    {
        public Vector2 position;
        public Vector2 direction;
        public float speed;
        public float scale;

        /// <summary>
        /// 延迟多少秒才发射（0 = 立刻）。
        ///
        /// 用途：螺旋、连发这类"一波一波打出去"的形态 ——
        /// 一次算好所有子弹，但让它们错开发射，看起来就是连续的。
        /// 调用方自己决定怎么处理（游戏里用协程，预览里直接跳过未到时间的）。
        /// </summary>
        public float delay;

        /// <summary>存活时间倍率（1 = 用默认值）。短命弹幕做"近距爆"很有用</summary>
        public float lifeScale;
    }

    /// <summary>所有可用的形态原语（编辑器下拉框用这个顺序）</summary>
    public static readonly string[] All =
    {
        // 基础（原来的 9 种）
        "straight", "parallel", "cross", "vshape", "diagonal",
        "scatter", "omni", "ring", "shotgun",
        // 新增：带时间维度的形态
        "spiral", "wave", "boom", "pincer", "random"
    };

    /// <summary>纯增益标签，不参与"怎么摆子弹"的分配</summary>
    public static bool IsModifier(string tag)
    {
        return tag == "pierce";
    }

    public static string DisplayName(string shape)
    {
        switch (shape)
        {
            case "straight": return "单发直线";
            case "parallel": return "平行齐射";
            case "cross": return "十字";
            case "vshape": return "V 形";
            case "diagonal": return "斜向四射";
            case "scatter": return "扇形散射";
            case "omni": return "全向 360°";
            case "ring": return "环形（慢·小）";
            case "shotgun": return "近距霰弹（慢·小）";
            case "spiral": return "螺旋（依次旋转发射）";
            case "wave": return "波浪（左右交错）";
            case "boom": return "爆散（近距全向·短命）";
            case "pincer": return "钳形（左右夹击）";
            case "random": return "随机散射";
            default: return shape;
        }
    }

    /// <summary>
    /// 算出某个形态的 n 发子弹各自的方向 / 位置 / 速度 / 尺寸。
    ///
    /// firePos / baseDir 是起点和基准朝向；speed / scale 是基础值，
    /// 各形态会在此基础上乘自己的系数（比如 ring 是 ×0.7 速度）。
    /// </summary>
    public static List<Shot> Compute(string shape, int n,
                                     Vector2 firePos, Vector2 baseDir,
                                     float speed, float scale)
    {
        var result = new List<Shot>();
        if (n <= 0) return result;

        if (baseDir.sqrMagnitude < 0.0001f) baseDir = Vector2.right;
        baseDir = baseDir.normalized;

        // 2D 垂直向量。注意不能用 Vector3.Cross(dir, forward) ——
        // 那两个向量的叉积 XY 分量为 0，会让所有子弹完全重叠。
        Vector2 side = new Vector2(-baseDir.y, baseDir.x);

        for (int i = 0; i < n; i++)
        {
            Vector2 dir = baseDir;
            Vector2 pos = firePos;
            float spd = speed;
            float sc = scale;
            float delay = 0f;
            float life = 1f;

            switch (shape)
            {
                case "straight":
                    break;

                case "parallel":
                    pos = firePos + side * ((i - (n - 1) / 2f) * 0.4f);
                    break;

                case "cross":
                    dir = Rotate(baseDir, 90f * (i % 4));
                    break;

                case "vshape":
                    dir = Rotate(baseDir, (i % 2 == 0) ? 45f : -45f);
                    break;

                case "diagonal":
                {
                    float[] diag = { 30f, -30f, 150f, -150f };
                    dir = Rotate(baseDir, diag[i % 4]);
                    break;
                }

                case "scatter":
                {
                    float t = n > 1 ? (float)i / (n - 1) : 0.5f;
                    dir = Rotate(baseDir, -40f + 80f * t);
                    break;
                }

                case "omni":
                    dir = Rotate(baseDir, 360f / n * i);
                    break;

                case "ring":
                    dir = Rotate(baseDir, 360f / n * i);
                    spd = speed * 0.7f;
                    sc = scale * 0.8f;
                    break;

                case "shotgun":
                {
                    float t = n > 1 ? (float)i / (n - 1) : 0.5f;
                    dir = Rotate(baseDir, -15f + 30f * t);
                    spd = speed * 0.6f;
                    sc = scale * 0.8f;
                    break;
                }

                // ================= 新增：带时间维度的形态 =================

                case "spiral":
                    // 每一发都比上一发多转一个固定角度，并且错开发射 ——
                    // 连起来看就是一条旋转的弹链。这是最经典的弹幕形态。
                    dir = Rotate(baseDir, i * 22f);
                    delay = i * 0.055f;
                    break;

                case "wave":
                    // 左右交错 + 错开时间，形成一道波浪线
                    dir = Rotate(baseDir, (i % 2 == 0) ? 14f : -14f);
                    pos = firePos + side * ((i % 2 == 0) ? 0.3f : -0.3f);
                    delay = i * 0.09f;
                    spd = speed * 0.9f;
                    break;

                case "boom":
                    // 近距离向四周炸开：全向但飞得慢、命短，逼玩家贴身躲
                    dir = Rotate(baseDir, 360f / n * i + 18f);
                    spd = speed * 0.45f;
                    sc = scale * 1.15f;
                    life = 0.45f;
                    break;

                case "pincer":
                    // 左右两侧各来一发，朝中间收拢 —— 玩家往哪边躲都难受
                    dir = Rotate(baseDir, (i % 2 == 0) ? 55f : -55f);
                    pos = firePos + side * ((i % 2 == 0) ? 0.5f : -0.5f);
                    delay = (i / 2) * 0.12f;
                    spd = speed * 0.85f;
                    break;

                case "random":
                    // 随机方向（但幅度受限，不会完全乱飞）
                    dir = Rotate(baseDir, Random.Range(-38f, 38f));
                    delay = Random.Range(0f, 0.14f);
                    break;

                default:
                    break;
            }

            result.Add(new Shot
            {
                position = pos,
                direction = dir,
                speed = spd,
                scale = sc,
                delay = delay,
                lifeScale = life
            });
        }

        return result;
    }

    /// <summary>这个形态是不是"带时间维度"的（有延迟发射）</summary>
    public static bool HasDelay(string shape)
    {
        return shape == "spiral" || shape == "wave" || shape == "pincer" || shape == "random";
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    /// <summary>
    /// 把总弹数按形态标签均分。
    ///
    /// **bulletCount 是权威总数**，标签只决定这些子弹怎么摆 ——
    /// 不能每个标签各自开火一轮（皇 声明 10 发却打出 18 发就是这么来的）。
    /// 第一个标签拿最多，余数依次分给后面的。
    /// </summary>
    public static int[] SplitQuota(int total, int shapeCount)
    {
        var quota = new int[Mathf.Max(0, shapeCount)];
        if (quota.Length == 0) return quota;

        int each = Mathf.Max(0, total) / quota.Length;
        int rest = Mathf.Max(0, total) % quota.Length;

        for (int i = 0; i < quota.Length; i++)
            quota[i] = each + (i < rest ? 1 : 0);

        return quota;
    }
}
