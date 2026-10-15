#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹幕预览渲染器 —— 在编辑器里把弹幕"画"出来，**不生成任何真实子弹**。
///
/// ## 怎么工作
///
///   1. 用 <see cref="BulletShapes.Compute"/> 算出每一发的方向/位置/速度
///   2. 在一个虚拟竞技场里逐帧推进它们的位置（带墙反弹）
///   3. 每帧画到一张 Texture2D 上（拖尾衰减 → 有运动感）
///
/// 因为只是"算 + 画"，所以可以在编辑器窗口里每帧重画、随便调参数立刻看到效果。
///
/// ## 为什么不用真实的 Bullet 预制体
///
/// 预览窗口不是 Play 模式，实例化 GameObject 会污染当前打开的场景
/// （编辑器的 Edit 模式下 Instantiate 出来的东西会留在场景里）。
/// 而且真实子弹要跑物理、要 Time.deltaTime，截图式的预览反而不直观。
/// 自己算 + 画到纹理上，可控、干净、快。
/// </summary>
public class BulletPreview
{
    // ---------- 虚拟竞技场 ----------
    /// <summary>虚拟场地半径（游戏单位）</summary>
    public float arenaRadius = 9f;

    /// <summary>预览纹理边长（像素）</summary>
    public int resolution = 320;

    /// <summary>拖尾衰减速度（0~1，越小拖尾越长）</summary>
    public float fadePerStep = 0.10f;

    /// <summary>每步模拟多少秒</summary>
    public float stepSeconds = 1f / 60f;

    /// <summary>子弹画出来的半径（像素）</summary>
    public float dotRadius = 2.6f;

    // ---------- 运行时 ----------
    class Sim
    {
        public Vector2 pos;
        public Vector2 dir;
        public float speed;
        public float scale;
        public Color color;
    }

    readonly List<Sim> sims = new List<Sim>();
    Texture2D tex;
    Color[] px;

    public Texture2D Texture { get { return tex; } }

    // ============================================================
    //  构建 / 重置
    // ============================================================

    void EnsureTexture()
    {
        if (tex == null || tex.width != resolution)
        {
            if (tex != null) Object.DestroyImmediate(tex);

            tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            px = new Color[resolution * resolution];
            Clear();
            tex.SetPixels(px);
            tex.Apply();
        }
    }

    public void Clear()
    {
        EnsureTexture();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(0.06f, 0.07f, 0.11f, 1f);
    }

    // ============================================================
    //  装配一轮弹幕
    // ============================================================

    /// <summary>
    /// 按形态标签装配一轮弹幕（和游戏里 SpawnByTags 的分配规则一致）。
    ///
    /// shapes       形态标签列表（顺序即优先级，第一个拿最多子弹）
    /// total        总弹数（权威值）
    /// speed/scale  基础速度与尺寸
    /// strokes      用来决定每发子弹颜色的笔画序列（可空，空则统一白）
    /// </summary>
    public void Setup(List<string> shapes, int total,
                      float speed, float scale,
                      List<StrokeType> strokes)
    {
        EnsureTexture();
        sims.Clear();

        // 过滤掉纯增益标签
        var real = new List<string>();
        if (shapes != null)
        {
            foreach (string s in shapes)
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (BulletShapes.IsModifier(s)) continue;
                real.Add(s);
            }
        }

        Vector2 origin = Vector2.zero;
        int strokeCursor = 0;

        if (real.Count == 0)
        {
            // 没有形态标签 → 单发直线
            AddShot(origin, Vector2.right, speed, scale, ColorOf(strokes, 0));
            return;
        }

        int[] quota = BulletShapes.SplitQuota(Mathf.Max(1, total), real.Count);

        for (int s = 0; s < real.Count; s++)
        {
            int n = quota[s];
            if (n <= 0) continue;

            var shots = BulletShapes.Compute(real[s], n, origin, Vector2.right, speed, scale);

            for (int i = 0; i < shots.Count; i++)
            {
                AddShot(shots[i].position, shots[i].direction,
                        shots[i].speed, shots[i].scale,
                        ColorOf(strokes, strokeCursor + i));
            }

            strokeCursor += n;
        }
    }

    void AddShot(Vector2 pos, Vector2 dir, float speed, float scale, Color color)
    {
        sims.Add(new Sim
        {
            pos = pos,
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right,
            speed = speed,
            scale = scale,
            color = color
        });
    }

    static Color ColorOf(List<StrokeType> strokes, int index)
    {
        if (strokes == null || strokes.Count == 0) return Color.white;
        return StrokeUtil.ColorOf(strokes[index % strokes.Count]);
    }

    // ============================================================
    //  推进 + 渲染
    // ============================================================

    /// <summary>推进一帧并把结果画到纹理上</summary>
    public void Step()
    {
        EnsureTexture();

        // 1) 整体压暗（拖尾）
        float keep = 1f - Mathf.Clamp01(fadePerStep);
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            px[i] = new Color(c.r * keep, c.g * keep, c.b * keep, 1f);
        }

        // 2) 移动 + 画
        float dt = stepSeconds;
        float worldToPx = (resolution * 0.5f) / arenaRadius;

        foreach (Sim s in sims)
        {
            s.pos += s.dir * s.speed * dt;

            // 撞到场地边界就反弹，让预览一直有东西看（不然子弹飞出去就空了）
            float r = s.pos.magnitude;
            if (r > arenaRadius)
            {
                Vector2 nrm = s.pos / r;
                s.dir = Vector2.Reflect(s.dir, nrm);
                s.pos = nrm * arenaRadius;
            }

            Vector2 screen = new Vector2(
                resolution * 0.5f + s.pos.x * worldToPx,
                resolution * 0.5f + s.pos.y * worldToPx);

            DrawDot(screen, dotRadius * Mathf.Max(0.6f, s.scale), s.color);
        }

        tex.SetPixels(px);
        tex.Apply();
    }

    /// <summary>画一个带柔边的亮点（比硬像素点好看很多）</summary>
    void DrawDot(Vector2 center, float radius, Color color)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1f));
        int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.x + radius + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1f));
        int y1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.y + radius + 1f));

        float r2 = radius * radius;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float d2 = dx * dx + dy * dy;

                if (d2 > r2 * 4f) continue;       // 只画柔边范围内

                // 内圈实心，外圈渐隐
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(d2) / (radius * 2f));
                float a = t * t;

                int idx = y * resolution + x;
                Color c = px[idx];
                px[idx] = new Color(
                    Mathf.Lerp(c.r, color.r, a),
                    Mathf.Lerp(c.g, color.g, a),
                    Mathf.Lerp(c.b, color.b, a),
                    1f);
            }
        }
    }

    /// <summary>
    /// 一路快进 n 步再返回纹理 —— 用来做"静态缩略图"（窗口刚打开时先给一帧有内容的图）。
    /// </summary>
    public void WarmUp(int steps)
    {
        for (int i = 0; i < steps; i++) Step();
    }

    public void Dispose()
    {
        if (tex != null)
        {
            Object.DestroyImmediate(tex);
            tex = null;
            px = null;
        }
        sims.Clear();
    }
}
#endif
