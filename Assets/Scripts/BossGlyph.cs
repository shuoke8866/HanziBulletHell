using UnityEngine;

/// <summary>
/// 给 BOSS 画出它代表的那个汉字 —— 和普通敌人走**同一套**字形渲染。
///
/// ## 为什么需要它
///
/// 玩家反馈"第三层的 BOSS 是官但不是皇"。排查发现：
///
///   Boss_Huang.prefab 的子物体列表
///     Square, Square (9), Square (2), **guan**, Square (10), Boss_Huang, ...
///   Boss_Guan.prefab 的子物体列表
///     Square, Square (9), Square (2), **guan**, Square (10), Boss_Guan, ...
///
/// **两个预制体的子物体完全一样** —— Boss_Huang 是从 Boss_Guan 复制出来的，
/// 字形部分没换掉。所以第三关的关底 BOSS 顶着一张"官"的脸。
///
/// 而且那些 Square 用的贴图 guid（`311925a0...`）在项目里根本不存在，
/// 属于悬空引用，本来就渲染不出东西。
///
/// ## 解决方式：不再依赖预制体里的手工图形
///
/// 普通敌人（Enemy）的做法是：加一个 HanziRenderer 子物体，用它画出汉字。
/// BOSS 也照做 —— 字符直接写在组件上，不依赖任何预制体残留。
///
/// 挂上之后会自动：
///   1. 建 `HanziRenderer` 子物体，画出 <see cref="character"/>
///   2. **把预制体里那些手画的残缺图形全部隐藏**（Square / guan / 等等）
///
/// 这样"皇"就真的显示成「皇」了。
/// </summary>
public class BossGlyph : MonoBehaviour
{
    [Tooltip("这个 BOSS 显示的汉字。留空 = 不画。")]
    public string character = "皇";

    [Tooltip("字号缩放")]
    public float scale = 1.6f;

    [Tooltip("颜色")]
    public Color color = new Color(1f, 0.92f, 0.72f);

    [Tooltip("要不要隐藏预制体里原有的手画图形（Square / guan 之类的残留）")]
    public bool hideLegacyArt = true;

    private HanziRenderer glyph;

    void Awake()
    {
        // ---- **把碰撞体设成触发器** ----
        //
        // 玩家反馈"皇根本就不掉血"。排查发现三个 BOSS 预制体的碰撞体
        // 都是 `isTrigger = 0`（实体碰撞体）：
        //
        //     Bullet.prefab      : Rigidbody2D + isTrigger = 1
        //     Enemy_Bing.prefab  : 有 isTrigger = 1 的碰撞体  → 小怪能被打掉
        //     Boss_Shi 1.prefab  : isTrigger = 0            → 打不掉
        //     Boss_Guan.prefab   : isTrigger = 0            → 打不掉
        //     Boss_Huang.prefab  : isTrigger = 0            → 打不掉
        //
        // Unity 的规则：**两个实体碰撞体之间只产生物理碰撞，不产生触发事件**。
        // 子弹靠 `OnTriggerEnter2D` 打伤害，Boss 的碰撞体不是触发器，
        // 所以那个回调一次都不会调用 —— 伤害代码没错，是压根没被触发。
        //
        // 在代码里改（而不是手改预制体）：
        // 三个 BOSS 都走这一个组件，改一处就全对，也不会被以后重新生成预制体覆盖。
        EnsureTriggerCollider();
    }

    void Start()
    {
        Build();
    }

    public void Build()
    {
        if (string.IsNullOrEmpty(character)) return;

        // ============================================================
        //  **已经有手画图形 → 什么都不做**
        // ============================================================
        //
        // 三个 BOSS 的手画字形（`Square*` / `guan` / `shi` / `huang` 等
        // SpriteRenderer 子物体）是**玩家自己画的这个字的形状**，
        // 每个字由 3~11 个 SpriteRenderer 拼成。
        //
        // 我在 54867f3 那次清理里把它们当成"从别的预制体复制过来的残渣"
        // 删掉了 —— 这是错的。它们才是这几个 BOSS 该有的外观。
        //
        // 所以：**只要预制体上已经有 SpriteRenderer 子物体，就完全不碰它。**
        // 过程化字形只在"什么都没画"时才生成。
        // 防回归：手画图形是不是**真的**画着这个字？
        //
        // 踩过的坑：Boss_Huang.prefab 是从 Boss_Guan.prefab 复制来的，
        // 字形子物体一直画的是「官」。我从 git 历史恢复预制体时，
        // 又把这个错误的字形一起恢复了 —— 关底 BOSS 于是"变回"了官。
        //
        // 这里做一道自检：如果这个 BOSS 的字和它的手画图形对不上，就在
        // 控制台明确报出来，而不是让人盯着屏幕猜。
        VerifyArtMatchesCharacter();

        if (HasHandDrawnArt())
        {
            if (hideLegacyArt)
            {
                Debug.LogWarning(
                    $"[BOSS 字形] {name} 上有手画图形，hideLegacyArt 已自动关闭。" +
                    "手画的形状比过程化字形好，不应该被隐藏或删除。");
                hideLegacyArt = false;
            }
            return;
        }

        if (hideLegacyArt) HideLegacyArt();

        if (glyph != null) return;

        var go = new GameObject("BossGlyph");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        go.transform.localScale = Vector3.one * scale;

        glyph = go.AddComponent<HanziRenderer>();
        glyph.textureSize = 96;
        glyph.pixelsPerUnit = 32f;

        // ---- 字库：用【玩家字库】 ----
        //
        // 玩家反馈「第二层的官变成一」→ 我改成敌人字库 →
        // **第一关和教程层的「士」也变成一了**（我制造了回归）。
        //
        // 根因：EnemyGlyphs.BuildEnemyDefinitions() **现在故意返回空字典**
        // （设计上让敌人回退到玩家字库，等玩家自己在字形编辑器里画）。
        // 所以敌人字库里一个字都没有 —— 指定它等于把 BOSS 的字全屏蔽了。
        //
        // 正确做法：用玩家字库（HanziRenderer 的默认值本来就是它）。
        glyph.glyphSet = HanziRenderer.SetPlayer;

        // 缺字时**自动补一个占位字形**，而不是静默显示成别的字。
        //
        // HanziRenderer.SetCharacter 原来遇到缺字是「打警告 + 保持当前显示」，
        // 表现就是 BOSS 顶着上一个字（「一」）—— 玩家完全看不出是缺字。
        if (!HanziRenderer.IsAllowed(character, HanziRenderer.SetPlayer))
        {
            Debug.LogWarning(
                $"[BOSS 字形] 玩家字库里没有「{character}」，已用占位字形补上。" +
                "要显示正确的字，请在字形编辑器里画一个。");

            HanziRenderer.EnsurePlaceholder(character);
        }

        glyph.SetCharacter(character);
        glyph.SetStrokeColor(color);
    }

    /// <summary>
    /// 手画图形和 <see cref="character"/> 对不上时报警。
    ///
    /// 判据用**已知的错配组合**：`Boss_Huang` 的字形子物体如果叫 `guan`
    /// （从 Boss_Guan 复制来的残留），那它画的就不是「皇」。
    ///
    /// 这个检查很窄，但正好盖住实际发生过的那种错配 ——
    /// 与其做一个"能识别任意汉字图形"的通用识别器（做不到），
    /// 不如把已知的坑钉死。
    /// </summary>
    void VerifyArtMatchesCharacter()
    {
        if (character == "皇" && transform.Find("guan") != null)
        {
            Debug.LogError(
                "[BOSS 字形] Boss_Huang 的字形子物体还叫 guan —— " +
                "这个预制体是从 Boss_Guan 复制来的，画的是「官」不是「皇」。\n" +
                "修法：把字形子物体的坐标改成「皇」的几何（白 + 王），" +
                "或删掉它让 BossGlyph 用过程化字形。");
        }

        if (character == "官" && transform.Find("huang") != null)
        {
            Debug.LogError(
                "[BOSS 字形] Boss_Guan 的字形子物体叫 huang —— " +
                "两个 BOSS 的字形可能被搞反了。");
        }
    }

    /// <summary>
    /// 这个 BOSS 身上有没有**手画的图形**（SpriteRenderer 子物体）。
    ///
    /// 判据：子物体带 SpriteRenderer 且有 Sprite —— 那就是有人拼出来的字形。
    /// 有的话就完全不动它。
    /// </summary>
    bool HasHandDrawnArt()
    {
        foreach (Transform child in transform)
        {
            if (child == null) continue;
            if (child.name == "BossGlyph") continue;      // 自己生成的那个不算

            var sr = child.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return true;
        }
        return false;
    }

    /// <summary>
    /// 隐藏预制体里原有的手画图形。
    ///
    /// 判据：这个子物体**只有 Transform + SpriteRenderer**、没有别的东西 ——
    /// 那就是当年手拼图形的残渣。有脚本的一律不动（可能是功能部件）。
    ///
    /// 用 `enabled = false` 而不是 `SetActive(false)`：
    /// 保留对象、只是不显示，出问题时容易回滚。
    /// </summary>
    void HideLegacyArt()
    {
        foreach (Transform child in transform)
        {
            if (child == null) continue;
            if (child.name == "BossGlyph") continue;

            // 有脚本的不要碰
            if (child.GetComponents<MonoBehaviour>().Length > 0) continue;

            // 只处理"纯图形"对象
            var sr = child.GetComponent<SpriteRenderer>();
            if (sr == null) continue;

            sr.enabled = false;
        }
    }

    /// <summary>运行时换字（比如 BOSS 变身）</summary>
    public void SetCharacter(string ch)
    {
        character = ch;
        if (glyph != null)
        {
            glyph.SetCharacter(ch);
            glyph.SetStrokeColor(color);
        }
    }

    /// <summary>
    /// 保证 BOSS 身上有一个**触发器**碰撞体，否则子弹打不中它。
    ///
    /// 见 Awake 里的说明：三个 BOSS 预制体的碰撞体都是实体碰撞体，
    /// 而子弹靠 OnTriggerEnter2D 打伤害 —— 实体 vs 实体不产生触发事件。
    ///
    /// 做法：
    ///   · 已有的 Collider2D 全部改成 isTrigger = true
    ///   · 如果一个都没有，补一个 BoxCollider2D
    ///   · 顺便保证有个 Rigidbody2D（触发事件至少一方要有刚体才稳定）
    /// </summary>
    void EnsureTriggerCollider()
    {
        Collider2D[] cols = GetComponents<Collider2D>();

        int fixedCount = 0;
        foreach (Collider2D c in cols)
        {
            if (c == null || c.isTrigger) continue;
            c.isTrigger = true;
            fixedCount++;
        }

        if (cols.Length == 0)
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2.5f, 2.5f);
            box.isTrigger = true;

            Debug.LogWarning($"[BOSS] {name} 原本没有任何 Collider2D，已补一个 2.5×2.5 触发器" +
                             "（否则子弹打不中）");
        }
        else if (fixedCount > 0)
        {
            Debug.Log($"[BOSS] {name} 的 {fixedCount} 个碰撞体已设为触发器" +
                      "（原本是实体碰撞体，子弹的 OnTriggerEnter2D 不会触发 → 打不掉血）");
        }

        // 触发事件需要至少一方有 Rigidbody2D
        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;

            Debug.Log($"[BOSS] {name} 原本没有 Rigidbody2D，已补一个 Kinematic 的");
        }
    }
}
