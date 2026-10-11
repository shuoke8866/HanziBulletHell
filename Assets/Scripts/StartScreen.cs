using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 开始页（极简版）。
///
/// 只有：游戏标题「相信字」+ 「开始游戏」按钮。
/// 不放玩法说明、键位表、字形展示之类的说明文案 ——
/// 那些内容对第一次打开的人反而是噪音，说明放 README 更合适。
///
/// 显示时机：本轮从第 1 层开始时出现一次。死亡重开后不再出现，
/// 否则每次重开都要多点一次按钮，节奏很烦。
/// </summary>
public class StartScreen : MonoBehaviour
{
    /// <summary>本轮是否已经显示过（避免重开后反复弹出）</summary>
    static bool seenThisRun;

    GameObject root;
    bool showing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (seenThisRun) return;
        if (GameData.currentFloor != 1) return;

        var go = new GameObject("~StartScreen");
        DontDestroyOnLoad(go);
        go.AddComponent<StartScreen>();
    }

    void Start()
    {
        Build();
        showing = true;
        shownAt = Time.unscaledTime;

        // 清掉上一次可能残留的暂停状态
        Time.timeScale = 1f;
    }

    float shownAt;

    void Build()
    {
        Canvas canvas = UIBuild.GetOrCreateCanvas();

        RectTransform full = UIBuild.FullScreen(canvas.transform, "StartScreen");
        root = full.gameObject;
        UIBuild.Background(full, new Color(0.03f, 0.04f, 0.09f, 0.96f));

        var group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;

        var title = UIBuild.Text(full, "Title", "相 信 字",
            100f, TextAlignmentOptions.Center, new Color(1f, 0.96f, 0.72f));
        UIBuild.AnchorTop(title.rectTransform, -420f, 1500f, 130f);

        MakeStartButton(full);
    }

    void MakeStartButton(RectTransform parent)
    {
        var go = new GameObject("StartButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(420f, 100f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.42f, 0.30f, 0.96f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(Dismiss);

        var label = UIBuild.Text(go.transform, "Label", "开 始 游 戏",
            42f, TextAlignmentOptions.Center, new Color(0.93f, 1f, 0.93f));
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (!showing) return;

        // 防抖：刚显示时忽略输入，否则玩家死亡重开时按着的方向键会立刻把本页关掉
        if (Time.unscaledTime - shownAt < 0.35f) return;

        // 键盘也能开始
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.Escape))
        {
            Dismiss();
        }
    }

    void Dismiss()
    {
        if (!showing) return;
        showing = false;
        seenThisRun = true;

        Sfx.Play(Sfx.Id.Menu, 0.6f);

        if (root != null) Destroy(root);
        Destroy(gameObject);
    }

    /// <summary>重开一轮时允许再次显示开始页</summary>
    public static void ResetForNewRun()
    {
        seenThisRun = false;
    }
}
