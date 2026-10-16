using UnityEngine;

/// <summary>
/// 背景系统 —— **全程序生成，零贴图资源**。
///
/// ## 为什么不用贴图
///
/// 这个项目从字形、墙体、血条到弹幕全部是代码画的。背景也走同一条路：
///   · WebGL 包现在 50.9 MB，加背景图会明显变大（加载更慢）
///   · 风格统一 —— 不会出现"写实贴图 + 一堆几何图形"的割裂感
///   · 想调只改几个数（更暗/更密/换色调），不用重做美术
///
/// 以后想换成贴图也很容易：把 <see cref="BuildBackdrop"/> 里那几层
/// 换成 SpriteRenderer 就行，接口不用动。
///
/// ## 三层结构（从远到近）
///
///   1. 底色层    一大块纯色矩形铺满视野，跟随相机
///   2. 网格层    细线网格（呼应"汉字格子"的主题）
///   3. 星点层    缓慢漂移的小点，制造纵深和"活着"的感觉
///
/// 色调由 <see cref="FloorTheme"/> 按层数给 —— 越深的层越冷、越压抑。
/// </summary>
public class BackgroundFx : MonoBehaviour
{
    // ---------- 各层主题色 ----------
    public struct Theme
    {
        public Color baseColor;     // 底色
        public Color gridColor;     // 网格线
        public Color dotColor;      // 星点
        public float dotCount;      // 星点数量

        public Theme(Color b, Color g, Color d, float n)
        {
            baseColor = b; gridColor = g; dotColor = d; dotCount = n;
        }
    }

    /// <summary>
    /// 按层数取主题。
    ///
    /// 设计意图：教程层暖一些（放松、教学），越往后越冷越暗（压迫感递增）。
    /// 这是很常见的做法 —— 用色调代替文字告诉玩家"情况变糟了"。
    /// </summary>
    public static Theme ThemeForFloor(int floor)
    {
        switch (floor)
        {
            case 0:     // 教程层：暖灰蓝，最柔和
                return new Theme(
                    new Color(0.055f, 0.062f, 0.090f),
                    new Color(0.100f, 0.115f, 0.160f, 0.55f),
                    new Color(0.55f, 0.62f, 0.80f, 0.55f), 70f);

            case 1:     // 第一层：更深的蓝
                return new Theme(
                    new Color(0.040f, 0.048f, 0.078f),
                    new Color(0.085f, 0.100f, 0.150f, 0.60f),
                    new Color(0.50f, 0.58f, 0.78f, 0.55f), 90f);

            case 2:     // 第二层：偏紫，开始不安
                return new Theme(
                    new Color(0.048f, 0.038f, 0.078f),
                    new Color(0.105f, 0.085f, 0.155f, 0.65f),
                    new Color(0.62f, 0.52f, 0.85f, 0.60f), 110f);

            default:    // 第三层：暗红，最压抑
                return new Theme(
                    new Color(0.070f, 0.030f, 0.040f),
                    new Color(0.140f, 0.070f, 0.085f, 0.70f),
                    new Color(0.85f, 0.45f, 0.45f, 0.60f), 130f);
        }
    }

    // ---------- 参数 ----------
    const float GridSize = 2f;          // 网格格子边长（世界单位）
    const float GridLineWidth = 0.02f;  // 网格线粗细
    const float ParallaxFactor = 0.15f; // 星点漂移速度（相对相机）

    static BackgroundFx instance;

    SpriteRenderer backdrop;
    Transform gridRoot;
    Transform dotRoot;

    Camera cam;
    Vector3 lastCamPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;

        var go = new GameObject("BackgroundFx");
        instance = go.AddComponent<BackgroundFx>();
    }

    void Start()
    {
        cam = Camera.main;
        if (cam == null) { Destroy(gameObject); return; }

        // 相机的 clear color 设成透明/纯黑都行 —— 底色层会盖满视野
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        BuildBackdrop();

        // 网格层和星点层暂时不做 —— 玩家要求"先跟第 1、2 层一样，用一大块纯色"。
        // 代码留着（BuildGrid / BuildDots 还在下面），想要的时候把这两行取消注释即可。
        // BuildGrid();
        // BuildDots();

        lastCamPos = cam.transform.position;
    }

    // ============================================================
    //  1) 底色层
    // ============================================================

    void BuildBackdrop()
    {
        Theme t = ThemeForFloor(GameData.currentFloor);

        var go = new GameObject("Backdrop");
        go.transform.SetParent(transform, false);

        backdrop = go.AddComponent<SpriteRenderer>();
        backdrop.sprite = SolidSprite.White;
        backdrop.color = t.baseColor;
        backdrop.sortingOrder = -1000;      // 永远在最底层

        // 铺得比屏幕大得多（相机不会跑太远，但也留足余量）
        go.transform.localScale = new Vector3(400f, 300f, 1f);
    }

    // ============================================================
    //  2) 网格层
    // ============================================================

    void BuildGrid()
    {
        Theme t = ThemeForFloor(GameData.currentFloor);

        var root = new GameObject("Grid");
        root.transform.SetParent(transform, false);
        gridRoot = root.transform;

        // 相机视野有多大 + 一圈余量
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        // 余量取 6 个格子 —— 相机移动时不会看到网格边缘
        float margin = GridSize * 6f;
        int cols = Mathf.CeilToInt((halfW * 2f + margin * 2f) / GridSize);
        int rows = Mathf.CeilToInt((halfH * 2f + margin * 2f) / GridSize);

        // 竖线
        for (int i = 0; i <= cols; i++)
        {
            float x = -halfW - margin + i * GridSize;
            AddGridLine(root.transform, x, halfH + margin, true, t.gridColor);
        }

        // 横线
        for (int i = 0; i <= rows; i++)
        {
            float y = -halfH - margin + i * GridSize;
            AddGridLine(root.transform, y, halfW + margin, false, t.gridColor);
        }

        // 网格整层跟随相机（不然走到房间边缘网格就断了）
        gridRoot.localPosition = Vector3.zero;
    }

    void AddGridLine(Transform parent, float fixedCoord, float halfLen,
                     bool vertical, Color color)
    {
        var go = new GameObject(vertical ? "V" : "H");
        go.transform.SetParent(parent, false);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SolidSprite.White;
        sr.color = color;
        sr.sortingOrder = -990;

        if (vertical)
        {
            go.transform.localPosition = new Vector3(fixedCoord, 0f, 0f);
            go.transform.localScale = new Vector3(GridLineWidth, halfLen * 2f, 1f);
        }
        else
        {
            go.transform.localPosition = new Vector3(0f, fixedCoord, 0f);
            go.transform.localScale = new Vector3(halfLen * 2f, GridLineWidth, 1f);
        }
    }

    // ============================================================
    //  3) 星点层
    // ============================================================

    readonly System.Collections.Generic.List<Transform> dots =
        new System.Collections.Generic.List<Transform>();
    readonly System.Collections.Generic.List<float> dotSpeeds =
        new System.Collections.Generic.List<float>();

    void BuildDots()
    {
        Theme t = ThemeForFloor(GameData.currentFloor);

        var root = new GameObject("Dots");
        root.transform.SetParent(transform, false);
        dotRoot = root.transform;

        float halfH = cam.orthographicSize * 1.6f;
        float halfW = halfH * cam.aspect;

        int n = Mathf.RoundToInt(t.dotCount);
        var rng = new System.Random(20261016);      // 固定种子：每次进关卡星点分布一样

        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Dot");
            go.transform.SetParent(dotRoot, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidSprite.White;
            sr.sortingOrder = -980;

            float x = ((float)rng.NextDouble() * 2f - 1f) * halfW;
            float y = ((float)rng.NextDouble() * 2f - 1f) * halfH;
            go.transform.localPosition = new Vector3(x, y, 0f);

            // 大小和亮度都随机，做出纵深（远的暗而小，近的亮而大）
            float s = 0.02f + (float)rng.NextDouble() * 0.055f;
            go.transform.localScale = new Vector3(s, s, 1f);

            Color c = t.dotColor;
            c.a *= 0.35f + (float)rng.NextDouble() * 0.65f;
            sr.color = c;

            dots.Add(go.transform);
            dotSpeeds.Add(0.10f + (float)rng.NextDouble() * 0.16f);
        }
    }

    // ============================================================
    //  跟随相机
    // ============================================================

    void LateUpdate()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        Vector3 camPos = cam.transform.position;

        // 底色层和网格层**完全跟随**相机（等于无限大的背景）
        if (backdrop != null)
            backdrop.transform.position = new Vector3(camPos.x, camPos.y, 20f);

        if (gridRoot != null)
        {
            // 网格对齐到格子，避免滚动时线在像素间抖动
            float gx = Mathf.Round(camPos.x / GridSize) * GridSize;
            float gy = Mathf.Round(camPos.y / GridSize) * GridSize;
            gridRoot.position = new Vector3(gx, gy, 15f);
        }

        // 星点层**部分跟随**（视差）—— 相机动的时候它们慢慢漂，
        // 做出"背景离得很远"的感觉
        if (dotRoot != null)
        {
            dotRoot.position = new Vector3(
                camPos.x * ParallaxFactor,
                camPos.y * ParallaxFactor, 12f);

            // 让星点自己慢慢上浮，静止画面也有生气
            for (int i = 0; i < dots.Count; i++)
            {
                Transform d = dots[i];
                if (d == null) continue;

                Vector3 p = d.localPosition;
                p.y += dotSpeeds[i] * Time.deltaTime;
                if (p.y > cam.orthographicSize * 1.7f) p.y = -cam.orthographicSize * 1.7f;
                d.localPosition = p;
            }
        }

        lastCamPos = camPos;
    }
}
