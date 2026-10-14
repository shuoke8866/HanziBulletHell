using UnityEngine;

/// <summary>
/// 玩家护罩（来自"竖"笔画的特点）。
///
/// 最简版按需求约定实现：
///   - 每次射击获得 1 层护罩，层数上限由 StrokeTraits.ShieldLayers 决定
///   - 受到伤害时优先消耗 1 层护罩来免除这次伤害
///   - 一段时间没有射击会自然消散，避免"挂机攒盾"
///
/// 视觉上用玩家身上的一个半透明子物体表示，不做额外资源。
/// </summary>
public class PlayerShield : MonoBehaviour
{
    public static PlayerShield Instance { get; private set; }

    [Header("参数")]
    /// <summary>层数上限（会被 StrokeTraits.ShieldLayers 覆盖）</summary>
    public int maxLayers = 1;
    /// <summary>多久没有新护罩就全部消散（秒）</summary>
    public float decayAfter = 3.5f;

    public int Layers { get; private set; }

    float lastGainTime = -99f;
    Transform visual;

    void Awake()
    {
        Instance = this;
        BuildVisual();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildVisual()
    {
        var go = new GameObject("ShieldVisual");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ProcTex.Dot;
        sr.color = new Color(0.5f, 0.85f, 1f, 0.35f);
        sr.sortingOrder = 20;
        go.transform.localScale = Vector3.one * 2.2f;

        visual = go.transform;
        go.SetActive(false);
    }

    /// <summary>射击时调用：加一层护罩</summary>
    public void GainLayer()
    {
        int cap = 1;
        if (StrokeTraits.Instance != null)
            cap = Mathf.Max(1, StrokeTraits.Instance.ShieldLayers);

        maxLayers = cap;
        Layers = Mathf.Min(cap, Layers + 1);
        lastGainTime = Time.time;
        UpdateVisual();
    }

    /// <summary>
    /// 尝试用护罩抵消一次伤害。返回 true 表示伤害已被护罩吸收。
    /// </summary>
    public bool AbsorbHit()
    {
        if (Layers <= 0) return false;

        Layers--;
        lastGainTime = Time.time;   // 消耗后重新计时，避免立刻消散
        UpdateVisual();

        Sfx.Play(Sfx.Id.Menu, 0.5f);
        Fx.PickupPop(transform.position, new Color(0.5f, 0.85f, 1f));
        return true;
    }

    void Update()
    {
        if (Layers > 0 && Time.time - lastGainTime > decayAfter)
        {
            Layers = 0;
            UpdateVisual();
        }

        // 让护罩缓慢自转，看起来是"活的"
        if (visual != null && visual.gameObject.activeSelf)
            visual.Rotate(0f, 0f, 45f * Time.deltaTime);
    }

    void UpdateVisual()
    {
        if (visual == null) return;

        bool on = Layers > 0;
        if (visual.gameObject.activeSelf != on)
            visual.gameObject.SetActive(on);

        if (on)
        {
            float k = maxLayers > 1 ? (float)Layers / maxLayers : 1f;
            var sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = new Color(0.5f, 0.85f, 1f, 0.18f + 0.25f * k);
            visual.localScale = Vector3.one * (2.2f + 0.35f * (Layers - 1));
        }
    }
}
