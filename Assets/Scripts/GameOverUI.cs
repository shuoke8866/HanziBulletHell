using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡结算页。
///
/// 为什么需要：原来 PlayerHealth.Die() 直接 LoadScene 重开，玩家看不到任何
/// "这轮做了什么"的反馈，缺少完成感；对作品集来说也浪费了一次展示数据的机会。
///
/// 现在死亡流程：Die() → 显示结算（暂停）→ 玩家点重开 → 清空进度回到第 1 层。
/// 结算数据来自 GameData 的本轮统计。
/// </summary>
public class GameOverUI : MonoBehaviour
{
    static GameOverUI instance;

    GameObject root;
    bool waiting;

    /// <summary>显示结算页。restartAction 由调用方传入（通常是 GameManager.RestartRun）。</summary>
    public static void Show(System.Action restartAction)
    {
        if (instance != null) return;

        var go = new GameObject("~GameOverUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameOverUI>();
        instance.Build(restartAction);
    }

    void Build(System.Action restartAction)
    {
        // 结算时暂停，避免怪物继续打
        Time.timeScale = 0f;
        waiting = true;

        // 允许背包 UI 的暂停状态被复位，否则界面会互相压住
        if (BackpackUI.IsOpen)
        {
            var bp = FindObjectOfType<BackpackUI>();
            if (bp != null) bp.Close();
        }
        Canvas canvas = UIBuild.GetOrCreateCanvas();

        RectTransform full = UIBuild.FullScreen(canvas.transform, "GameOver");
        root = full.gameObject;
        UIBuild.Background(full, new Color(0.10f, 0.02f, 0.03f, 0.90f));

        var g = root.AddComponent<CanvasGroup>();
        g.blocksRaycasts = true;

        // ---- 标题 ----
        var title = UIBuild.Text(full, "Title", "本 轮 结 束",
            86f, TextAlignmentOptions.Center, new Color(1f, 0.55f, 0.5f));
        UIBuild.AnchorTop(title.rectTransform, -140f, 1400f, 120f);

        // ---- 成绩 ----
        int floor = GameData.currentFloor;
        string stats =
            $"到达层数      <color=#ffd97a>{floor}</color>\n" +
            $"击杀小怪      <color=#ffd97a>{GameData.enemiesKilled}</color>\n" +
            $"击败 BOSS     <color=#ffd97a>{GameData.bossesKilled}</color>\n" +
            $"收集笔画      <color=#ffd97a>{GameData.strokesCollected}</color>\n" +
            $"最终汉字      <color=#9fe8a0>{CurrentHanziName()}</color>";

        var statText = UIBuild.Text(full, "Stats", stats,
            40f, TextAlignmentOptions.Left, Color.white);
        UIBuild.AnchorTop(statText.rectTransform, -320f, 860f, 340f);
        statText.lineSpacing = 22f;

        // ---- 最终字形展示 ----
        Sprite sp = HanziRenderer.CreateSpriteFor(CurrentHanziName(), 128);
        if (sp != null)
        {
            var img = UIBuild.GlyphImage(full, "FinalGlyph", sp, 190f,
                new Color(1f, 0.92f, 0.72f));
            img.rectTransform.anchoredPosition = new Vector2(340f, 10f);
        }

        // ---- 提示 ----
        var hint = UIBuild.Text(full, "Hint",
            "真肉鸽：所有笔画已清空，将从第 1 层重新开始",
            32f, TextAlignmentOptions.Center, new Color(0.75f, 0.78f, 0.88f));
        UIBuild.AnchorBottom(hint.rectTransform, 210f, 1400f, 50f);

        MakeRestartButton(full, restartAction);

        Sfx.Play(Sfx.Id.PlayerHurt, 0.9f);
        Fx.Shake(0.9f);
    }

    static string CurrentHanziName()
    {
        // 死亡时玩家身上的汉字存在 GameData.currentStrokes 里
        var hs = Object.FindObjectOfType<HanziSystem>();
        if (hs != null && !string.IsNullOrEmpty(hs.currentHanziName))
            return hs.currentHanziName;

        return GameData.currentStrokes != null && GameData.currentStrokes.Count > 0
            ? "一" : "—";
    }

    void MakeRestartButton(RectTransform parent, System.Action restartAction)
    {
        var go = new GameObject("RestartButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 300f);
        rt.sizeDelta = new Vector2(460f, 100f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.45f, 0.16f, 0.18f, 0.96f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Restart(restartAction));

        var label = UIBuild.Text(go.transform, "Label", "重 新 开 始",
            42f, TextAlignmentOptions.Center, new Color(1f, 0.92f, 0.9f));
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (!waiting) return;

        // 键盘也能重开（结算时 timeScale = 0，所以用 unscaledTime 无关，这里只要按键）
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Escape))
        {
            // Escape 也用来重开：结算状态下没有别的含义
            var btn = root != null ? root.GetComponentInChildren<Button>() : null;
            if (btn != null) btn.onClick.Invoke();
        }
    }

    void Restart(System.Action restartAction)
    {
        if (!waiting) return;
        waiting = false;

        Sfx.Play(Sfx.Id.Menu, 0.6f);

        // 关键：重开前必须复位 timeScale，否则新场景会卡在暂停状态
        Time.timeScale = 1f;

        if (root != null) Destroy(root);
        instance = null;
        Destroy(gameObject);

        if (restartAction != null) restartAction();
    }
}
