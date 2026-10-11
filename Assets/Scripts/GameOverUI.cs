using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡结算页（极简版）。
///
/// 只保留：正在结束的提示 + 重开按钮。
/// 不再展示层数/击杀/笔画等长串统计，也不放"真肉鸽"之类的说明文案。
///
/// 死亡流程：PlayerHealth.Die() → 显示本页（暂停）→ 点重开 → 
/// 清空进度回到第 1 层。暂停是必要的，否则怪物会继续打已经死掉的玩家。
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
        Time.timeScale = 0f;
        waiting = true;

        // 背包可能还开着，先收掉，否则两个界面会互相压住
        if (BackpackUI.IsOpen)
        {
            var bp = FindObjectOfType<BackpackUI>();
            if (bp != null) bp.Close();
        }

        Canvas canvas = UIBuild.GetOrCreateCanvas();

        RectTransform full = UIBuild.FullScreen(canvas.transform, "GameOver");
        root = full.gameObject;
        UIBuild.Background(full, new Color(0.08f, 0.02f, 0.03f, 0.88f));

        var g = root.AddComponent<CanvasGroup>();
        g.blocksRaycasts = true;

        var title = UIBuild.Text(full, "Title", "游 戏 结 束",
            88f, TextAlignmentOptions.Center, new Color(1f, 0.6f, 0.55f));
        UIBuild.AnchorTop(title.rectTransform, -300f, 1400f, 130f);

        MakeRestartButton(full, restartAction);

        Sfx.Play(Sfx.Id.PlayerHurt, 0.9f);
        Fx.Shake(0.9f);
    }

    void MakeRestartButton(RectTransform parent, System.Action restartAction)
    {
        var go = new GameObject("RestartButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -80f);
        rt.sizeDelta = new Vector2(400f, 96f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.42f, 0.15f, 0.17f, 0.96f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Restart(restartAction));

        var label = UIBuild.Text(go.transform, "Label", "重 新 开 始",
            40f, TextAlignmentOptions.Center, new Color(1f, 0.93f, 0.9f));
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (!waiting) return;

        // 键盘也能重开。结算时 timeScale = 0，所以只响应按下事件即可
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Escape))
        {
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

        // 重开后回到第 1 层，让开始页再出现一次 ——
        // 否则"首次启动有开始页、重开却直接进游戏"，行为不一致。
        // StartScreen 自带 0.35s 输入防抖，不会因为玩家还按着键就立刻被关掉。
        StartScreen.ResetForNewRun();

        if (restartAction != null) restartAction();
    }
}
