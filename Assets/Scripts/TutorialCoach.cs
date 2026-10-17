using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 悬浮教学点 —— 一个小圆点飘在玩家**右上方**，玩家做出对应行为时弹气泡。
///
/// ## 为什么换成这种做法
///
/// 原来是在每个房间摆一块触发区（TutorialHint），走进房间就弹一屏字。三个问题：
///   · 触发时机和"玩家在干什么"无关 —— 可能正在躲弹幕，根本没空看
///   · 触发盒盖住出生点时 OnTriggerEnter2D 不会触发（字幕不出现）
///   · 一个房间只有一组字，中间的过程教不到
///
/// 现在改成**行为驱动**：玩家做到了，才教下一步。这也是现在主流游戏的做法
/// （有个引导角色/图标跟着你，你做出动作它就说话）。
///
/// ## 结构
///
///   点（Dot）    常驻的小圆点，在玩家右上方**较远处**飘着
///   文字用 FloatingTip（和宝箱提示同一套）挂在提示点的**左上角**
///
/// 两者都是**世界空间**的，所以跟着玩家走 —— 玩家一看就知道"这是跟我说话的"。
///
/// ## 文案与触发
///
/// 全在 <see cref="GameText.教学步骤"/> 里，改文案不用重新生成场景。
/// </summary>
public class TutorialCoach : MonoBehaviour
{
    // ---------- 位置：玩家右上方较远处 ----------
    const float OffsetX = 2.6f;
    const float OffsetY = 2.0f;

    // ---------- 外观 ----------
    const float DotSize = 0.34f;
    // ---------- 文字 ----------
    /// <summary>文字相对提示点的偏移：**左上角**（玩家要求）</summary>
    static readonly Vector3 TextOffset = new Vector3(-0.35f, 0.42f, -0.1f);

/// <summary>
    /// 文字缩放（FloatingTip 的基准字号 4.6 × 这个值）。
    ///
    /// **1.0 = 和拾取飘字一样大** —— 那个大小是玩家确认过能看清的。
    ///
    /// 原来设的 0.62 是为了配合那个小圆点的大小，结果实际字号只有
    /// 4.6 × 0.62 = 2.85，玩家反馈「教程提示句太小了」。
    /// 当时是按「看起来协调」调的，没有对照一个已知能看清的基准 ——
    /// 现在直接和拾取飘字对齐。
    /// </summary>
    const float TextScale = 1.0f;

    /// <summary>
    /// 文字颜色：亮黄白。
    ///
    /// 配合 FloatingTip 里的**深色描边**，不管压在什么底色上都读得清。
    /// 这是浮字的稳定做法 —— 光调颜色会在不同底色上失效
    /// （教程气泡反复出问题就是因为它靠手绘底板 + 调颜色）。
    /// </summary>
    static readonly Color TextColor = new Color(1f, 0.95f, 0.72f);

    // ---------- 节奏 ----------
    /// <summary>一条文案停留多久（最后一条之后气泡收起）</summary>
    const float LineDuration = 3.4f;

    /// <summary>两条文案之间空多久</summary>
    const float GapBetweenSteps = 0.6f;

    /// <summary>全局最短间隔 —— 防止连续触发把气泡刷爆</summary>
    const float MinIntervalBetweenSteps = 1.2f;

    // ---------- 运行时 ----------
    static TutorialCoach instance;

    Transform dot;
    SpriteRenderer dotSprite;
    /// <summary>当前显示的那行教程文字（FloatingTip 的句柄）</summary>
    FloatingTip.Handle textHandle;

    GameText.CoachStep[] steps;
    int currentStep = -1;         // 正在显示的步骤（-1 = 空闲）
    int currentLine;
    float timer;
    float lastStepTime = -999f;

    // ---------- 触发用的记录 ----------
    Vector3 startPos;
    readonly HashSet<string> done = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // 每个场景都建一个，**具体启用与否交给 Start() 判断**。
        //
        // 为什么不在这里判断 inTutorial：
        // RuntimeInitializeOnLoadMethod(AfterSceneLoad) 和场景里脚本的 Start()
        // 谁先跑没有保证 —— 在这里读 GameData.inTutorial 可能读到还没设好的值。
        // 放到 Start() 里就没有这个时序问题。
        if (instance != null) return;

        var go = new GameObject("TutorialCoach");
        instance = go.AddComponent<TutorialCoach>();
    }

    void Start()
    {
        // 只在教程层启用 —— 正式关卡里玩家已经会了，再飘个点很烦
        if (!GameData.inTutorial)
        {
            instance = null;
            Destroy(gameObject);
            return;
        }

        steps = GameText.教学步骤();
        if (steps == null || steps.Length == 0)
        {
            instance = null;
            Destroy(gameObject);
            return;
        }

        Transform player = PlayerRef.Transform;
        if (player == null)
        {
            instance = null;
            Destroy(gameObject);
            return;
        }

        // 挂在场景根下（不挂在玩家身上 —— 玩家的 transform 会水平翻转，
        // 挂上去会把气泡镜像。位置每帧手动跟）
        startPos = player.position;

        // 门控：说完话之前不给开门（见 Door.cs 的 HoldOpen）
        // 逐房间门控：开局所有房间都「没说」→ 门全拦着
        finishedRooms.Clear();
        pendingOpenRoom = null;
        HoldDoorsUntilTaught = true;
        RegisterDoorGates();

        BuildDot();

        // 不再在这里播第一条 —— 教程改成**按房间播**：
        // 玩家真正走进某个房间时，CheckRoomEntry 会触发那间房的字幕。
        // （原来这里无条件播第 0 条，所以「第一条总是出现在出生房」，
        //   和玩家实际所在房间无关 —— 这就是玩家说的「房间错了」。）
    }
    /// <summary>
    /// 教学层第一间房的教学做完之前，先别放玩家走。
    ///
    /// 玩家要求：「在教学层的第一间完成教学之后再开门」—— 就这一件事。
    ///
    ///     开局 = true              → 教学层的门先都别开
    ///     第一间房那一步说完 = false → 放行
    ///     正式关卡 = 一直是 false    → 门完全不受影响
    ///
    /// 上一版用的是「玩家在某间房里 && 那间房没说过话」，
    /// 结果正式关卡的每一扇门都可能被拦住 —— 那是过度设计。
    /// 需求只有一句话，实现也该只有一件事。
    /// </summary>
    public static bool HoldDoorsUntilTaught;

    /// <summary>
    /// 已经「说完话」的房间名。**每间房各有一道门控。**
    ///
    /// 玩家要的规则（逐房间推进）：
    ///   第 1 间房播完最后一句话 → 过 1 秒开门 → 可以进第 2 间
    ///   第 2 间房播完 → 开门 → 进第 3 间
    ///   以此类推。
    ///
    /// 所以不能用「一把总闸」（HoldDoorsUntilTaught），那样一进
    /// 第 1 间房说完就全开了。
    /// </summary>
    static readonly HashSet<string> finishedRooms = new HashSet<string>();

    /// <summary>说完了、但还在等开门的那间房（用于 1 秒延迟）</summary>
    static string pendingOpenRoom;

    /// <summary>玩家上一次所在的房间（用来检测刚进了一间新房）</summary>
    Room lastRoom;

    /// <summary>已经因进房间播过的房间（每间房只播一次）</summary>
    readonly HashSet<Room> roomsAnnounced = new HashSet<Room>();


    // ============================================================
    //  建造
    // ============================================================

    // ============================================================
    //  门控
    // ============================================================

    /// <summary>
    /// 给场景里每扇门挂上拦截条件。
    ///
    /// 条件 = 「玩家正在某间房里，而那间房的话还没说完」。
    /// 用**玩家位置**判断，而不是「门属于哪间房」——
    /// 玩家在哪间房里，就该听哪间房的话。
    /// </summary>
    void RegisterDoorGates()
    {
        int n = 0;

        foreach (Door d in Object.FindObjectsOfType<Door>(true))
        {
            if (d == null) continue;

            d.HoldOpen = ShouldHoldDoors;
            n++;
        }

        GameLog.Info($"[教程] 已给 {n} 扇门挂上「教学未完先别开」的条件");
    }

    /// <summary>
    /// 现在要不要拦着门。
    ///
    /// **逐房间判定**：玩家当前所在的那间房说完了，就放行。
    /// 正式关卡永远返回 false（那些门没挂这个委托）。
    /// </summary>
    static bool ShouldHoldDoors()
    {
        if (!GameData.inTutorial) return false;   // 正式关卡：不拦

        Room here = RoomAt(PlayerRef.Transform);
        if (here == null) return false;           // 站在门外，不拦

        return !finishedRooms.Contains(here.name);
    }

    /// <summary>这间房是否配过字幕（没配的房间不该拦门）</summary>
    bool RoomHasSteps(string roomName)
    {
        if (steps == null) return false;

        foreach (GameText.CoachStep s in steps)
            if (s.trigger == GameText.CoachTrigger.InRoom && s.room == roomName)
                return true;

        return false;
    }

    void BuildDot()
    {
        var go = new GameObject("Dot");
        go.transform.SetParent(transform, false);

        dotSprite = go.AddComponent<SpriteRenderer>();
        dotSprite.sprite = ProcTex.Dot;      // 柔和圆点，边缘带淡出
        dotSprite.color = new Color(0.55f, 0.85f, 1f, 0.95f);
        dotSprite.sortingOrder = 150;

        go.transform.localScale = Vector3.one * DotSize;
        dot = go.transform;
    }

    void LateUpdate()
    {
        Transform player = PlayerRef.Transform;
        if (player == null) { Destroy(gameObject); return; }

        Vector3 basePos = player.position + new Vector3(OffsetX, OffsetY, 0f);

        // 平滑跟随（直接设位置会很死板，稍微滞后一点更像"飘着"）
        transform.position = Vector3.Lerp(transform.position, basePos,
                                          Time.unscaledDeltaTime * 6f);

        if (dot != null)
        {
            // 点轻微上下浮动，让它"活着"
            float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * 0.07f;
            dot.localPosition = new Vector3(0f, bob, 0f);

            // 有点话要说时点会放大一点，提示玩家看这边
            float target = textHandle != null ? DotSize * 1.25f : DotSize;
            dot.localScale = Vector3.Lerp(dot.localScale,
                                          Vector3.one * target,
                                          Time.unscaledDeltaTime * 8f);
        }

        // 文字挂在 coach 下面（不是圆点），所以手动跟随圆点的世界位置。
        // 这样文字不受圆点缩放影响，字号才是所见即所得。
        if (dot != null && textHandle != null && textHandle.go != null)
        {
            textHandle.go.transform.position = dot.position + TextOffset;
        }

        CheckRoomEntry();
        CheckTriggers();
        TickDisplay();
    }

    // ============================================================
    //  触发检测
    // ============================================================

    /// <summary>
    /// 检测玩家刚进了哪间房，并触发对应的 InRoom 字幕。
    ///
    /// 这是玩家要求的「按房间播字幕」：哪条字幕在哪个房间播，
    /// 由 GameText.教学步骤() 里每条步骤的 room 字段决定 ——
    /// 改文案和改房间都在字幕编辑器里做，不用碰代码。
    /// </summary>
    void CheckRoomEntry()
    {
        Room here = RoomAt(PlayerRef.Transform);
        if (here == lastRoom) return;        // 没换房间

        lastRoom = here;

        // 走出房间（here == null）不算进入，不播
        if (here == null) return;

        if (!roomsAnnounced.Add(here)) return;   // 这间房已经播过

        // 这间房如果**没配字幕**，别拦着玩家 —— 直接标记说完，门正常开。
        // 否则玩家会被一道永远打不开的门卡住（因为不会有 EndStep 来放行）。
        if (!RoomHasSteps(here.name))
        {
            finishedRooms.Add(here.name);
            return;
        }

        TryFireRoom(here.name);
    }

    /// <summary>某个 Transform 现在在哪间房里（不在任何房间就返回 null）</summary>
    static Room RoomAt(Transform who)
    {
        if (who == null) return null;

        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            var box = r.GetComponent<Collider2D>();
            if (box == null) continue;
            if (box.bounds.Contains(who.position)) return r;
        }
        return null;
    }

    /// <summary>按房间名触发一条 InRoom 字幕</summary>
    /// <summary>
    /// 触发某间房的全部字幕 —— **按顺序连着播**。
    ///
    /// 之前这里找到第一条就 return，导致同一房间的多条步骤
    /// 只播第一条（Room0_Doubt 有 2 条、Room3_Swap 有 4 条都不会播完）。
    ///
    /// 现在改成排队：把这间房所有步骤按顺序塞进 _queue，
    /// 前一条说完自动接下一条。
    /// </summary>
    void TryFireRoom(string roomName)
    {
        if (steps == null || string.IsNullOrEmpty(roomName)) return;

        _roomQueue.Clear();

        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i].trigger != GameText.CoachTrigger.InRoom) continue;
            if (steps[i].room != roomName) continue;

            string key = i + ":room:" + roomName;
            if (done.Contains(key)) continue;

            done.Add(key);
            _roomQueue.Enqueue(i);
        }

        GameLog.Info($"[教程] 进入「{roomName}」，排入 {_roomQueue.Count} 条字幕");

        PlayNextInQueue();
    }

    /// <summary>本房间待播的步骤</summary>
    readonly Queue<int> _roomQueue = new Queue<int>();

    /// <summary>播放队列里的下一条（队列空就什么都不做）</summary>
    void PlayNextInQueue()
    {
        if (_roomQueue.Count == 0) return;

        int idx = _roomQueue.Dequeue();
        if (idx < 0 || idx >= steps.Length) return;

        ShowStep(idx, true);   // immediate: 同一房间的多条要连着播，不要被间隔卡掉
    }

    /// <summary>
    /// 旧的事件触发器（Moved / FirstShot / GotStroke / ...）。
    ///
    /// **现在不再使用** —— 教程改成按房间播之后，步骤里一条都没用这些 trigger，
    /// 继续保留这套检测只会误触发（比如「一移动就播 Moved 那条」，
    /// 而那条现在根本不存在，只会走到 TryFire 里空转）。
    ///
    /// 保留这个空方法是为了以后想加「按键教一次」之类的**一次性提示**时
    /// 有个地方放，不用重新接 LateUpdate。
    /// </summary>
    void CheckTriggers()
    {
        // 目前所有教程步骤都是 InRoom 触发，见 CheckRoomEntry()。
    }

    static bool InBossRoom(Vector3 pos)
    {
        foreach (Room r in Object.FindObjectsOfType<Room>())
        {
            if (r.roomType != RoomType.Boss) continue;
            // 房间中心 ±6 单位以内就算进来了（房间本身不一定是 12 宽，宽松点没关系）
            if (Mathf.Abs(pos.x - r.transform.position.x) < 8f &&
                Mathf.Abs(pos.y - r.transform.position.y) < 6f)
                return true;
        }
        return false;
    }

    /// <summary>某个触发条件满足了 —— 如果对应步骤还没弹过，就弹它</summary>
    void TryFire(GameText.CoachTrigger trigger, int num = 0)
    {
        if (steps == null) return;

        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i].trigger != trigger) continue;

            // StrokeCount 还要看笔画数够不够
            if (trigger == GameText.CoachTrigger.StrokeCount && num < steps[i].num)
                continue;

            string key = i + ":" + trigger;
            if (done.Contains(key)) continue;

            done.Add(key);
            ShowStep(i, false);
            return;
        }
    }

    /// <summary>等一会儿再弹某一步 —— 用来把两段叙事错开，避免互相打断</summary>

    // ============================================================
    //  显示节奏
    // ============================================================

    void ShowStep(int index, bool immediate)
    {
        if (index < 0 || index >= steps.Length) return;

        // 正在说别的就先不打断（除非是开场那句）
        if (!immediate && currentStep >= 0) return;

        // 全局最短间隔
        if (!immediate && Time.unscaledTime - lastStepTime < MinIntervalBetweenSteps) return;

        currentStep = index;
        currentLine = 0;
        timer = 0f;
        lastStepTime = Time.unscaledTime;

        ApplyLine();
    }

    void ApplyLine()
    {
        if (currentStep < 0 || currentStep >= steps.Length) return;

        string[] lines = steps[currentStep].lines;
        if (lines == null || lines.Length == 0 || currentLine >= lines.Length)
        {
            EndStep();
            return;
        }

        ShowLine(lines[currentLine]);
    }

    void EndStep()
    {
        // 这一步说完了 → 把**玩家当前所在的这间房**标记为说完。
        //
        // 不是全部放行，而是只放行这一间 —— 玩家要的是逐房间推进：
        // 第 1 间说完开第 1 道门，第 2 间说完开第 2 道，以此类推。
        // 这间房还有没说完的 → 先接着说，**先不要开门**
        if (_roomQueue.Count > 0)
        {
            currentStep = -1;
            currentLine = 0;
            HideLine();

            PlayNextInQueue();
            return;
        }

        Room here = RoomAt(PlayerRef.Transform);
        if (here != null)
        {
            // **过 1 秒再开门**（玩家要求的节奏）。
            // 直接开会给人一种「话还没落音门就开了」的突兀感，
            // 留 1 秒让玩家读完最后一句。
            pendingOpenRoom = here.name;
            CancelInvoke(nameof(FinishRoom));
            Invoke(nameof(FinishRoom), 1.0f);
        }

        currentStep = -1;
        currentLine = 0;
        HideLine();
    }

    /// <summary>延迟 1 秒后真正放行那间房的门</summary>
    void FinishRoom()
    {
        if (string.IsNullOrEmpty(pendingOpenRoom)) return;

        finishedRooms.Add(pendingOpenRoom);
        GameLog.Info($"[教程] 「{pendingOpenRoom}」说完，门已放行");

        // 主动开一次门：Door 只在 pendingOpen 时每帧检查，
        // 现在立刻通知它条件变了，玩家不用再等一帧。
        foreach (Door d in Object.FindObjectsOfType<Door>(true))
        {
            if (d == null) continue;
            if (d.isOpen) continue;
            if (d.HoldOpen != null && d.HoldOpen()) continue;
            d.Open();
        }

        pendingOpenRoom = null;
    }

    /// <summary>
    /// 显示一行教程文字 —— **和宝箱提示是同一套**（FloatingTip）。
    ///
    /// 位置在提示点的**左上角**（玩家要求）。
    /// </summary>
    void ShowLine(string text)
    {
        if (dot == null || string.IsNullOrEmpty(text)) return;

        // 换行就重建一条（对象很少，不值得做增量更新）
        FloatingTip.Clear(textHandle);
        // **faceCamera: false** —— 不能让 FaceCamera 覆盖缩放。
        //
        // 教程文字挂在圆点下面，而圆点在"说话"时会放大到 1.25 倍。
        // FaceCamera 每帧执行 `transform.localScale = 初始父缩放 / 当前父缩放`，
        // 于是它会**把这里设的 TextScale 写回去** ——
        // 我把 TextScale 从 0.62 改成 1.0，字号却一点没变，就是这个原因。
        //
        // 关掉 FaceCamera 之后 TextScale 才是真正生效的字号。
        // **锚点是 coach 自己，不是圆点。**
        //
        // 玩家反馈「教程字还是小」。根因是**父级缩放**：
        //   文字挂在圆点下面，圆点 localScale = 0.34（说话时 0.425）
        //   → 文字的实际世界缩放 = 0.34 × 1.0 = 0.34
        //   → 实际字号 = 4.6 × 0.34 ≈ 1.56
        //
        // 上次只验证了文字自己的 localScale = 1.0，**没算父级**，
        // 所以表现是「改了却没用」。
        //
        // 现在挂在 coach 名下（localScale = 1，整条链路没有缩放），
        // 位置在 LateUpdate 里跟随圆点。
        textHandle = FloatingTip.ShowAtOffset(text, TextColor, transform, TextOffset, TextScale,
                                              float.PositiveInfinity, faceCamera: false);
    }

    void HideLine()
    {
        FloatingTip.Clear(textHandle);
        textHandle = null;
    }

    void OnDestroy()
    {
        // 对象被销毁时把文字也收掉 —— 不然会留一条孤儿提示在场景里
        HideLine();
    }

    void TickDisplay()
    {
        if (currentStep < 0) return;

        timer += Time.unscaledDeltaTime;

        float need = LineDuration + (currentLine < steps[currentStep].lines.Length - 1
            ? GapBetweenSteps : 0f);

        if (timer < need) return;

        timer = 0f;
        currentLine++;

        if (currentLine >= steps[currentStep].lines.Length) EndStep();
        else ApplyLine();
    }
}
