using UnityEngine;

/// <summary>
/// 笔画特点：合成的汉字里**含有**某种笔画时，玩家就获得对应能力。
///
/// 设计要点（按需求确定）：
///   - **开关式**，不按数量叠加。有横就穿透，有 3 个横也还是穿透，
///     但"子弹数量"仍然随笔画数增加（外观上能看出字更复杂）。
///   - 特点与笔画类型一一对应，玩家能从字形"读出"自己的能力。
///
/// 判定入口是 <see cref="Refresh"/>，在每次切换汉字后调用一次。
/// </summary>
public class StrokeTraits : MonoBehaviour
{
    public static StrokeTraits Instance { get; private set; }

    // ---- 当前生效的特点（供其它系统查询）----
    /// <summary>含横：所有弹幕穿透</summary>
    public bool Pierce { get; private set; }
    /// <summary>含竖：射击时获得护罩</summary>
    public bool ShieldOnFire { get; private set; }
    /// <summary>含撇或捺：获得近身斩击</summary>
    public bool MeleeSlash { get; private set; }
    /// <summary>含点：弹幕附带灼烧</summary>
    public bool Burn { get; private set; }
    /// <summary>含折：弹幕到射程末端折返（简化为射程延长）</summary>
    public bool Rebound { get; private set; }

    // ---- 特殊字的专属加成 ----
    /// <summary>穿透额外层数（王 / 皇）</summary>
    public int ExtraPierce { get; private set; }
    /// <summary>护罩可叠加层数（士）</summary>
    public int ShieldLayers { get; private set; }
    /// <summary>斩击范围倍率（工）</summary>
    public float SlashRangeMultiplier { get; private set; }
    /// <summary>全部效果增强倍率（皇）</summary>
    public float OverallMultiplier { get; private set; }

    void Awake()
    {
        Instance = this;
        ResetAll();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void ResetAll()
    {
        Pierce = false;
        ShieldOnFire = false;
        MeleeSlash = false;
        Burn = false;
        Rebound = false;

        ExtraPierce = 0;
        ShieldLayers = 1;
        SlashRangeMultiplier = 1f;
        OverallMultiplier = 1f;
    }

    /// <summary>
    /// 根据当前汉字的笔画重新计算所有特点。切换汉字后必须调用。
    /// </summary>
    public void Refresh(HanziDef def)
    {
        ResetAll();
        if (def == null || def.strokes == null) return;

        foreach (StrokeType s in def.strokes)
        {
            switch (s)
            {
                case StrokeType.Heng: Pierce = true; break;
                case StrokeType.Shu: ShieldOnFire = true; break;
                case StrokeType.Pie:
                case StrokeType.Na: MeleeSlash = true; break;
                case StrokeType.Dian: Burn = true; break;
                case StrokeType.Zhe: Rebound = true; break;
            }
        }

        ApplySpecialEffects(def.hanziName);
    }

    /// <summary>
    /// 指定汉字的专属效果。
    /// 这些加成叠加在笔画特点之上 —— 例如「工」本身不含撇捺，不会给斩击，
    /// 但一旦你通过其他字拿到斩击，「工」会让斩击范围更大。
    /// </summary>
    void ApplySpecialEffects(string name)
    {
        switch (name)
        {
            case "工":
                SlashRangeMultiplier = 1.5f;      // 斩击范围 +50%
                break;
            case "士":
                ShieldLayers = 2;                 // 护罩可叠 2 层
                break;
            case "王":
                ExtraPierce = 2;                  // 穿透 +2
                break;
            case "田":
                Burn = true;                      // 方形结构带灼烧
                Rebound = true;
                break;
            case "圭":
                ExtraPierce = 1;
                SlashRangeMultiplier = 1.25f;
                break;
            case "皇":
                OverallMultiplier = 1.5f;         // 终极形态：全部增强
                ExtraPierce = 3;
                ShieldLayers = 3;
                SlashRangeMultiplier = 1.6f;
                break;
        }
    }

    /// <summary>弹幕总穿透层数（1 = 不穿透）</summary>
    public int TotalPierceLayers
    {
        get
        {
            if (!Pierce) return 1;
            return Mathf.Max(1, 3 + ExtraPierce + Mathf.RoundToInt((OverallMultiplier - 1f) * 4f));
        }
    }
}
