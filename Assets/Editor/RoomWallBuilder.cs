#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按统一规则重建房间墙体：**每个房间封成一整圈墙，门只是墙上切出来的一个小口。**
///
/// ## 规则
///
/// 房间的判定范围由它自己的 BoxCollider2D（offset + size）决定。
/// 假设房间占据 [minX,maxX] × [minY,maxY]，则：
///
///   · 左右墙：厚 1，贴在范围**外侧**（x = minX-0.5 / maxX+0.5）
///   · 上下墙：厚 1，贴在范围外侧（y = minY-0.5 / maxY+0.5）
///   · 四面墙首尾相接，不留缝、不重叠 —— 也就是"封成一整圈"
///   · 哪一面有门，就在那一面的**正中**切出一个宽 3 的口
///   · 门上下的墙用两段补齐
///
/// 这样从外面看是一间封好的屋子，门只是墙上的一个小缺口。
///
/// ## 为什么不用原来的拼法
///
/// 原来是"一条长横墙 + 两段竖墙"手拼出来的，于是：
///   · 墙的长度各不相同、位置靠手调，房间之间经常留出莫名其妙的缝
///   · 门做成 (0.5,10) —— 高 10，而房间才高 8~10，
///     等于一整面墙都是门，玩家根本看不出"这里有个门"
///
/// 现在门统一缩到 3 个单位（玩家约 0.8 宽，进出很宽松），
/// 其余部分全部是实墙。
/// </summary>
public static class RoomWallBuilder
{
    /// <summary>组成房间四壁的墙，厚度（沿墙的法线方向）</summary>
    public const float WallThickness = 1f;

    /// <summary>
    /// 门的长度（沿墙方向）。
    ///
    /// 玩家本体约 0.8 宽，3 个单位足够宽松地进出，
    /// 又不会大到"整面墙都是门"。想改门口大小只改这一个数。
    /// </summary>
    public const float DoorSpan = 3f;

    /// <summary>厚度方向的尺寸 = 门的另一个边（和墙厚一致，这样关上时严丝合缝）</summary>
    public const float DoorThickness = 1f;

    /// <summary>生成物名字前缀，用于识别"上次生成的"以便清理</summary>
    public const string Prefix = "_Wall_";

    /// <summary>墙的颜色（和原场景里的墙一致）</summary>
    static readonly Color WallColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    /// <summary>墙的贴图：和原场景里用的是同一张内置白方块</summary>
    static Sprite _square;

    /// <summary>墙/门用的方块贴图（先从场景里已有的墙上借，保证视觉一致）</summary>
    public static Sprite SquareSprite
    {
        get
        {
            if (_square != null) return _square;

            // 优先从已有墙体上借一张，保证视觉完全一致
            foreach (SpriteRenderer sr in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sr.gameObject.CompareTag("Wall") && sr.sprite != null)
                {
                    _square = sr.sprite;
                    return _square;
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Square (1).prefab");
            if (prefab != null)
            {
                SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
                if (sr != null) _square = sr.sprite;
            }

            // 最后兜底：场景里任意一张
            if (_square == null)
            {
                SpriteRenderer any = Object.FindObjectOfType<SpriteRenderer>();
                if (any != null) _square = any.sprite;
            }

            return _square;
        }
    }

    /// <summary>场景切换时要清掉，否则会拿到上一个场景已销毁的贴图</summary>
    public static void ResetStatics()
    {
        _square = null;
    }

    // ============================================================
    //  对外入口
    // ============================================================

    /// <summary>
    /// 重建场景里所有房间的墙，并把门缩到正确尺寸。
    ///
    /// 会先删掉所有 Tag=Wall 的旧墙和上次生成的墙，再按规则重铺。
    /// </summary>
    public static int RebuildAll(bool verbose = true)
    {
        ResetStatics();

        Room[] rooms = Object.FindObjectsOfType<Room>();
        if (rooms.Length == 0)
        {
            Debug.LogWarning("[墙] 场景里没有 Room，跳过");
            return 0;
        }

        RemoveOldWalls();

        int total = 0;

        foreach (Room room in rooms)
        {
            BoxCollider2D box = room.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogWarning($"[墙] 房间「{room.name}」没有 BoxCollider2D，跳过");
                continue;
            }

            Vector2 center = (Vector2)room.transform.position + box.offset;
            Vector2 half = box.size * 0.5f;

            float minX = center.x - half.x;
            float maxX = center.x + half.x;
            float minY = center.y - half.y;
            float maxY = center.y + half.y;

            // 把这一间房的门调整到正确尺寸，并按"门在哪一面"分组。
            //
            // 判定用门的位置离哪条边最近，而不是看 dx/dy 的归一化比值 ——
            // 后者在房间不是正方形时会判错。
            var leftGates = new List<Door>();
            var rightGates = new List<Door>();
            var bottomGates = new List<Door>();
            var topGates = new List<Door>();

            if (room.doors != null)
            {
                foreach (Door d in room.doors)
                {
                    if (d == null) continue;

                    Vector2 p = d.transform.position;

                    float dLeft = Mathf.Abs(p.x - minX);
                    float dRight = Mathf.Abs(p.x - maxX);
                    float dBottom = Mathf.Abs(p.y - minY);
                    float dTop = Mathf.Abs(p.y - maxY);

                    float best = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));

                    // 门必须贴在房间的某条边上，否则说明它根本不属于这间房
                    // （教程层曾经把门放在两间房中间 10 单位的空隙里，
                    //   结果墙上的门洞开在了房间外面）
                    if (best > WallThickness * 2f)
                    {
                        Debug.LogWarning($"[墙] 「{room.name}」的门「{d.name}」在 ({p.x},{p.y})，" +
                                         $"离最近的边还有 {best:0.##} —— 它不在这间房的墙上，已忽略");
                        continue;
                    }

                    if (best == dLeft) { ResizeDoor(d, true); AddOnce(leftGates, d); }
                    else if (best == dRight) { ResizeDoor(d, true); AddOnce(rightGates, d); }
                    else if (best == dBottom) { ResizeDoor(d, false); AddOnce(bottomGates, d); }
                    else { ResizeDoor(d, false); AddOnce(topGates, d); }
                }
            }

            // 四面墙都要铺，缺一面就补满 —— 房间是**封闭**的，只留门通行。
            //
            // 坐标一律用世界坐标。之前这里混用了"锚点 + 沿墙方向参数"两套体系，
            // 结果横向墙全部叠在了 x=0（anchor.x 传的是 0）。
            total += BuildSide(room, "L", true, minX - WallThickness * 0.5f,
                               minY, maxY, leftGates);
            total += BuildSide(room, "R", true, maxX + WallThickness * 0.5f,
                               minY, maxY, rightGates);
            total += BuildSide(room, "B", false, minY - WallThickness * 0.5f,
                               minX, maxX, bottomGates);
            total += BuildSide(room, "T", false, maxY + WallThickness * 0.5f,
                               minX, maxX, topGates);

            if (verbose)
                Debug.Log($"[墙] 「{room.name}」范围 x[{minX},{maxX}] y[{minY},{maxY}]  " +
                          $"门 左{leftGates.Count}/右{rightGates.Count}/下{bottomGates.Count}/上{topGates.Count}");
        }

        if (verbose) Debug.Log($"[墙] 重建完成，共生成 {total} 段墙");
        return total;
    }

    // ============================================================
    //  单面墙
    // ============================================================

    /// <summary>
    /// 铺好一面墙。
    ///
    /// vertical=true  竖直墙：固定在 x = <paramref name="fixedCoord"/>，沿 Y 从 a 铺到 b
    /// vertical=false 水平墙：固定在 y = <paramref name="fixedCoord"/>，沿 X 从 a 铺到 b
    ///
    /// a / b / fixedCoord **全部是世界坐标** —— 上一版混用了"锚点 + 沿墙参数"
    /// 两套体系，导致横向墙统统叠在了 x=0。
    ///
    /// gates 里的门会在自己的位置上切出一个口，口的宽度**等于门自己的长度**，
    /// 其余部分正常铺墙 —— 所以整面墙除了门洞之外没有缝。
    /// </summary>
    static int BuildSide(Room room, string tag, bool vertical, float fixedCoord,
                         float a, float b, List<Door> gates)
    {
        int count = 0;

        // 把门换算成"沿这一面的一维区间"
        var spans = new List<Vector2>();   // (中心, 半宽)
        foreach (Door d in gates)
        {
            if (d == null) continue;

            Vector3 sc = d.transform.localScale;
            float along = vertical ? Mathf.Abs(sc.y) : Mathf.Abs(sc.x);
            float c = vertical ? d.transform.position.y : d.transform.position.x;

            spans.Add(new Vector2(c, along * 0.5f));
        }
        spans.Sort((p, q) => p.x.CompareTo(q.x));

        float cursor = a;
        int idx = 0;

        foreach (Vector2 s in spans)
        {
            float gateMin = s.x - s.y;
            float gateMax = s.x + s.y;

            // 门洞靠下端/左端的那一段
            if (gateMin > cursor + 0.01f)
                count += AddSegment(room, $"{Prefix}{room.name}_{tag}{idx++}",
                                    vertical, fixedCoord, cursor, gateMin);

            cursor = Mathf.Max(cursor, gateMax);
        }

        // 最后一个门洞之后的那一段（没有门时就是整面墙）
        if (b > cursor + 0.01f)
            count += AddSegment(room, $"{Prefix}{room.name}_{tag}{idx}",
                                vertical, fixedCoord, cursor, b);

        return count;
    }

    // ============================================================
    //  验证：确认房间四周真的封住了
    // ============================================================

    struct Span
    {
        public float Lo, Hi;
        public Span(float lo, float hi) { Lo = lo; Hi = hi; }
    }

    /// <summary>
    /// 检查每个房间的四壁是不是真的封住了。
    ///
    /// 为什么需要：光看坐标很难判断"墙和门之间有没有缝"。
    /// 这里把每一面上"墙体覆盖的区间"和"关门时门覆盖的区间"合并起来，
    /// 再看合并后的区间里有没有宽度超过 <paramref name="maxGap"/> 的洞。
    ///
    /// 只有"开门时门不算墙"，所以要按两种情况分别检查：
    ///   · <paramref name="doorsClosed"/>=false（房间没在打）→ 门是开的，门那段就是通道，允许有洞
    ///   · <paramref name="doorsClosed"/>=true （房间在打）  → 门关着，必须是实心的
    ///
    /// 返回发现的问题条数。
    /// </summary>
    public static int ValidateWalls(float maxGap = 0.4f, bool doorsClosed = true, bool verbose = true)
    {
        int problems = 0;

        var wallsBySide = new Dictionary<string, List<Span>>();

        // 收集所有墙段，按"房间|面"分组
        foreach (GameObject go in UnityEngine.SceneManagement.SceneManager
                     .GetActiveScene().GetRootGameObjects())
        {
            CollectWallSpans(go.transform, wallsBySide);
        }

        foreach (Room room in Object.FindObjectsOfType<Room>())
        {
            BoxCollider2D box = room.GetComponent<BoxCollider2D>();
            if (box == null) continue;

            Vector2 c = (Vector2)room.transform.position + box.offset;
            Vector2 h = box.size * 0.5f;
            float minX = c.x - h.x, maxX = c.x + h.x;
            float minY = c.y - h.y, maxY = c.y + h.y;

            problems += CheckSide(room, "L", c.x - h.x - WallThickness * 0.5f,
                                  minY, maxY, true, wallsBySide, maxGap, doorsClosed, verbose);
            problems += CheckSide(room, "R", c.x + h.x + WallThickness * 0.5f,
                                  minY, maxY, true, wallsBySide, maxGap, doorsClosed, verbose);
            problems += CheckSide(room, "B", c.y - h.y - WallThickness * 0.5f,
                                  minX, maxX, false, wallsBySide, maxGap, doorsClosed, verbose);
            problems += CheckSide(room, "T", c.y + h.y + WallThickness * 0.5f,
                                  minX, maxX, false, wallsBySide, maxGap, doorsClosed, verbose);
        }

        if (verbose)
            Debug.Log($"[墙检] 完成，发现 {problems} 处漏洞" +
                      $"（判据：连续空隙 > {maxGap}，门关闭={doorsClosed}）");

        return problems;
    }

    static int CheckSide(Room room, string side, float coord, float a, float b, bool vertical,
                         Dictionary<string, List<Span>> wallsBySide, float maxGap,
                         bool doorsClosed, bool verbose)
    {
        var spans = new List<Span>();

        string key = $"{room.name}|{side}";
        List<Span> own;
        if (wallsBySide.TryGetValue(key, out own)) spans.AddRange(own);

        // 关门时，门本身也算一段实心
        if (doorsClosed && room.doors != null)
        {
            foreach (Door d in room.doors)
            {
                if (d == null) continue;

                Vector3 p = d.transform.position;
                float across = vertical ? p.x : p.y;
                if (Mathf.Abs(across - coord) > WallThickness) continue;   // 不是这一面的门

                Vector3 sc = d.transform.localScale;
                float alongCenter = vertical ? p.y : p.x;
                float alongHalf = (vertical ? Mathf.Abs(sc.y) : Mathf.Abs(sc.x)) * 0.5f;

                spans.Add(new Span(alongCenter - alongHalf, alongCenter + alongHalf));
            }
        }

        if (spans.Count == 0)
        {
            if (verbose)
                Debug.LogError($"[墙检] 「{room.name}」{side} 面**完全没有墙** —— 整面都通着");
            return 1;
        }

        // 合并区间
        spans.Sort((p, q) => p.Lo.CompareTo(q.Lo));
        float cur = spans[0].Lo, end = spans[0].Hi;
        var merged = new List<Span>();
        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i].Lo <= end + 0.001f) { end = Mathf.Max(end, spans[i].Hi); }
            else { merged.Add(new Span(cur, end)); cur = spans[i].Lo; end = spans[i].Hi; }
        }
        merged.Add(new Span(cur, end));

        string label = side == "L" ? "左" : side == "R" ? "右" : side == "B" ? "下" : "上";

        int bad = 0;

        // 两端有没有盖住
        if (merged[0].Lo > a + maxGap)
        {
            if (verbose)
                Debug.LogError($"[墙检] 「{room.name}」{label}面 靠下端有 {merged[0].Lo - a:0.##} 宽的洞" +
                               $"（{a}~{merged[0].Lo:0.##}）");
            bad++;
        }

        if (merged[merged.Count - 1].Hi < b - maxGap)
        {
            if (verbose)
                Debug.LogError($"[墙检] 「{room.name}」{label}面 靠上端有 {b - merged[merged.Count - 1].Hi:0.##} 宽的洞" +
                               $"（{merged[merged.Count - 1].Hi:0.##}~{b}）");
            bad++;
        }

        // 中间有没有缝
        for (int i = 1; i < merged.Count; i++)
        {
            float gap = merged[i].Lo - merged[i - 1].Hi;
            if (gap > maxGap)
            {
                if (verbose)
                    Debug.LogError($"[墙检] 「{room.name}」{label}面 中间有 {gap:0.##} 宽的缝" +
                                   $"（{merged[i - 1].Hi:0.##}~{merged[i].Lo:0.##}）");
                bad++;
            }
        }

        return bad;
    }

    static void CollectWallSpans(Transform t, Dictionary<string, List<Span>> map)
    {
        if (t == null) return;

        GameObject go = t.gameObject;
        string n = go.name;

        // 命名格式：_Wall_<房间名>_<面><序号>
        if (n.StartsWith(Prefix))
        {
            int tagIdx = n.LastIndexOf('_');
            if (tagIdx > Prefix.Length)
            {
                string body = n.Substring(Prefix.Length);           // <房间名>_<面><序号>
                int sep = body.LastIndexOf('_');
                if (sep > 0)
                {
                    string roomName = body.Substring(0, sep);
                    char side = body[sep + 1];
                    string key = $"{roomName}|{side}";

                    Vector3 p = t.position;
                    Vector3 s = t.localScale;

                    if (!map.ContainsKey(key)) map[key] = new List<Span>();

                    if (side == 'L' || side == 'R')
                        map[key].Add(new Span(p.y - s.y * 0.5f, p.y + s.y * 0.5f));
                    else
                        map[key].Add(new Span(p.x - s.x * 0.5f, p.x + s.x * 0.5f));
                }
            }
            return;
        }

        for (int i = 0; i < t.childCount; i++)
            CollectWallSpans(t.GetChild(i), map);
    }

    /// <summary>
    /// 铺一段墙。a / b 是世界坐标下"沿墙方向"的起止，fixedCoord 是墙中心的另一维坐标。
    /// </summary>
    static int AddSegment(Room room, string name, bool vertical,
                          float fixedCoord, float a, float b)
    {
        float length = b - a;
        if (length <= 0.01f) return 0;

        float alongCenter = (a + b) * 0.5f;

        Vector2 pos = vertical
            ? new Vector2(fixedCoord, alongCenter)
            : new Vector2(alongCenter, fixedCoord);

        Vector2 size = vertical
            ? new Vector2(WallThickness, length)
            : new Vector2(length, WallThickness);

        GameObject go = new GameObject(name);
        go.tag = "Wall";
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SquareSprite;
        sr.color = WallColor;
        sr.sortingOrder = 0;

        BoxCollider2D box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;

        return 1;
    }

    // ============================================================
    //  门
    // ============================================================

    /// <summary>
    /// 把门缩到"刚好填满门洞"的尺寸。
    ///
    /// 原来的门是 (0.5,10) / (16,0.5) —— 前者等于整面墙都是门
    /// （房间才高 8，门高 10），后者横跨半个房间。
    /// 玩家根本看不出"这里有个门"，也分不清哪段是墙哪段是门。
    ///
    /// 现在统一成：沿墙厚方向 = WallThickness，沿墙方向 = DoorSpan。
    /// </summary>
    static void ResizeDoor(Door d, bool vertical)
    {
        // 门是 1×3 还是 3×1，取决于它开在竖墙还是横墙上
        d.transform.localScale = vertical
            ? new Vector3(WallThickness, DoorSpan, 1f)
            : new Vector3(DoorSpan, WallThickness, 1f);

        BoxCollider2D box = d.GetComponent<BoxCollider2D>();
        if (box != null) box.size = Vector2.one;
    }

    static void AddOnce(List<Door> list, Door d)
    {
        if (!list.Contains(d)) list.Add(d);
    }

    // ============================================================
    //  清理
    // ============================================================

    /// <summary>删掉场景里所有作为墙用的对象（旧的 + 上次生成的）</summary>
    /// <summary>
    /// 删掉场景里所有作为墙用的对象（原场景的 + 我们上次生成的）。
    ///
    /// 必须**递归**遍历：原场景的墙不是根对象，而是嵌在
    /// `Room1_Walls` / `Room2_Walls` 这类容器下面的子对象
    /// （父级是那三个 _Walls 容器）。只扫根对象会一个都找不到 ——
    /// 这个坑直接导致第一次重建时新旧墙叠加在了一起。
    ///
    /// 同时把这些空的容器也删掉，否则场景里会留一堆没用的空物体。
    /// </summary>
    public static int RemoveOldWalls()
    {
        var doomed = new List<GameObject>();
        int byName = 0, byTag = 0;

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var roots = new List<GameObject>(scene.GetRootGameObjects());

        foreach (GameObject root in roots)
            CollectWalls(root.transform, doomed, ref byName, ref byTag);

        foreach (GameObject go in doomed)
            if (go != null) Object.DestroyImmediate(go);

        // 清掉只剩空壳的 _Walls 容器
        int containers = 0;
        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            if (root.GetComponent<Room>() != null) continue;
            if (root.transform.childCount > 0) continue;
            if (!root.name.Contains("Wall")) continue;

            Object.DestroyImmediate(root);
            containers++;
        }

        Debug.Log($"[墙] 删除 {doomed.Count} 段旧墙（按名字 {byName} / 按标签 {byTag}），" +
                  $"顺带清掉 {containers} 个空容器");
        return doomed.Count;
    }

    static void CollectWalls(Transform t, List<GameObject> outList,
                             ref int byName, ref int byTag)
    {
        if (t == null) return;

        GameObject go = t.gameObject;

        // 房间本体绝不能删
        if (go.GetComponent<Room>() == null)
        {
            // 原场景的墙叫 Wall_Left / Wall_Left (2)，我们生成的叫 _Wall_<房间>_<面>
            if (go.name.StartsWith("Wall") || go.name.StartsWith(Prefix))
            {
                outList.Add(go);
                byName++;
                return;                      // 整段删掉，不用再往下找
            }

            if (go.CompareTag("Wall"))
            {
                outList.Add(go);
                byTag++;
                return;
            }
        }

        for (int i = t.childCount - 1; i >= 0; i--)
            CollectWalls(t.GetChild(i), outList, ref byName, ref byTag);
    }
}
#endif
