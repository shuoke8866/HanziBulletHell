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
    bool movedFired, shotFired, strokeFired, bagFired, swapFired, bossFired, bossKillFired;
    readonly HashSet<string> done = new HashSet<string>();
    float killCheckTimer;

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

        BuildDot();

        ShowStep(0, true);      // 一进教程层就把第一条说了
    }

    // ============================================================
    //  建造
    // ============================================================

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

        CheckTriggers();
        TickDisplay();
    }

    // ============================================================
    //  触发检测
    // ============================================================

    void CheckTriggers()
    {
        Transform player = PlayerRef.Transform;
        if (player == null) return;

        // 移动
        if (!movedFired && Vector2.Distance(player.position, startPos) > 1.5f)
        {
            movedFired = true;

            // 开场房的"疑虑"先说完，再教笔画的事 ——
            // 两条文案挤在一起会互相打断，而且叙事顺序也不对。
            // 16 秒 ≈ 疑虑那 4 条文案（每条 3.4 秒 + 间隔）刚好走完。
            TryFire(GameText.CoachTrigger.Doubt);
            StartCoroutine(FireDelayed(GameText.CoachTrigger.Moved, 16f));
        }

        // 开枪（方向键）
        if (!shotFired &&
            (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
             Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)))
        {
            shotFired = true;
            TryFire(GameText.CoachTrigger.FirstShot);
        }

        // 拿到笔画
        if (!strokeFired && GameData.strokesCollected > 0)
        {
            strokeFired = true;
            TryFire(GameText.CoachTrigger.GotStroke);
        }

        // 开背包
        if (!bagFired && Input.GetKeyDown(KeyCode.E))
        {
            bagFired = true;
            TryFire(GameText.CoachTrigger.OpenedBag);
        }

        // 换字成功（formedHanzi 里有记录）
        if (!swapFired && GameData.formedHanzi != null && GameData.formedHanzi.Count >= 2)
        {
            swapFired = true;
            TryFire(GameText.CoachTrigger.SwappedHanzi);
        }

        // 笔画数（每帧检查，因为它是"达到 N 画"而不是"事件"）
        HanziSystem hs = PlayerRef.GameObject != null
            ? PlayerRef.GameObject.GetComponent<HanziSystem>() : null;
        int strokeCount = hs != null && hs.CurrentHanziDef != null && hs.CurrentHanziDef.strokes != null
            ? hs.CurrentHanziDef.strokes.Count : 0;

        if (strokeCount > 0)
            TryFire(GameText.CoachTrigger.StrokeCount, strokeCount);

        // BOSS 房 / BOSS 死亡（每隔一会儿查一次就够了，不用每帧扫全场）
        killCheckTimer += Time.unscaledDeltaTime;
        if (killCheckTimer > 0.4f)
        {
            killCheckTimer = 0f;

            if (!bossFired && InBossRoom(player.position))
            {
                bossFired = true;
                TryFire(GameText.CoachTrigger.EnteredBoss);
            }

            if (!bossKillFired && GameData.bossesKilled > 0)
            {
                bossKillFired = true;
                TryFire(GameText.CoachTrigger.BossKilled);
            }
        }
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
    System.Collections.IEnumerator FireDelayed(GameText.CoachTrigger trigger, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        TryFire(trigger);
    }

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
        currentStep = -1;
        currentLine = 0;
        HideLine();
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
