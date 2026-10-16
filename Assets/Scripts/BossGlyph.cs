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

    void Start()
    {
        Build();
    }

    public void Build()
    {
        if (string.IsNullOrEmpty(character)) return;

        if (hideLegacyArt) HideLegacyArt();

        if (glyph != null) return;

        var go = new GameObject("BossGlyph");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        go.transform.localScale = Vector3.one * scale;

        glyph = go.AddComponent<HanziRenderer>();
        glyph.textureSize = 96;
        glyph.pixelsPerUnit = 32f;
        glyph.SetCharacter(character);
        glyph.SetStrokeColor(color);
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
}
