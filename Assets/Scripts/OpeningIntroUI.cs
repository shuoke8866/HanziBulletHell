using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 开场游戏介绍 —— 在教程层之前显示一屏文字，玩家按键继续。
///
/// ## 为什么单独一个组件
///
/// 教程层的提示牌（TutorialHint）是"走进某个房间才出现"的，
/// 而这个要在**玩家还没开始动**的时候就把世界观交代完。
/// 两者的触发时机、显示形式都不同，混在一起会让 TutorialHint 变得很难维护。
///
/// ## 文案怎么改
///
/// 全部在 <see cref="GameText.Intro.正文"/> 里，改完存盘立刻生效，
/// 不用重新生成场景。想加页就往那个数组里多加一条字符串。
///
/// ## 为什么用 Image + TextMeshPro 而不是 OnGUI
///
/// 项目其它 UI 都是 Canvas + TMP，跟着走才能和字体、缩放、层级保持一致。
/// </summary>
public class OpeningIntroUI : MonoBehaviour
{
    /// <summary>整个开场只显示一次（同一局游戏里不重复弹）</summary>
    static bool shownThisRun;

    Canvas canvas;
    TextMeshProUGUI bodyText;
    TextMeshProUGUI pageText;
    TextMeshProUGUI hintText;

    List<string> pages = new List<string>();
    int page;

    void Start()
    {
        // 只在教程层弹，而且一局只弹一次
        if (shownThisRun) { Destroy(this); return; }
        if (!GameData.inTutorial && GameData.currentFloor != 0)
        {
            // 直接进正式关卡时（开发调试）就不弹了
            Destroy(this);
            return;
        }

        pages = new List<string>(GameText.Intro.正文());
        if (pages.Count == 0) { Destroy(this); return; }

        Build();
        Show(0);

        shownThisRun = true;
    }

    void Build()
    {
        var go = new GameObject("IntroCanvas");
        go.transform.SetParent(transform, false);

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;      // 盖在所有其它 UI 之上

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();

        // 背景：全屏压暗，让文字读得清
        var bg = new GameObject("Bg");
        bg.transform.SetParent(go.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.03f, 0.035f, 0.07f, 0.97f);
        Stretch(bg.GetComponent<RectTransform>());

        // 正文
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(go.transform, false);
        bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        bodyText.fontSize = 34f;
        bodyText.alignment = TextAlignmentOptions.Center;
        bodyText.color = new Color(1f, 0.97f, 0.9f);
        bodyText.enableWordWrapping = true;

        var bodyRect = bodyGo.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.sizeDelta = new Vector2(900f, 320f);
        bodyRect.anchoredPosition = new Vector2(0f, 30f);

        // 页码 / 继续提示
        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(go.transform, false);
        hintText = hintGo.AddComponent<TextMeshProUGUI>();
        hintText.fontSize = 22f;
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.color = new Color(0.75f, 0.8f, 0.95f);

        var hintRect = hintGo.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0f);
        hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(900f, 40f);
        hintRect.anchoredPosition = new Vector2(0f, 60f);

        pageText = hintText;

        // 开场时把游戏暂停 —— 不然玩家在"看介绍"的时候角色已经能动了
        Time.timeScale = 0f;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void Show(int index)
    {
        page = Mathf.Clamp(index, 0, pages.Count - 1);
        bodyText.text = pages[page];

        string tip = page < pages.Count - 1
            ? GameText.Intro.继续()
            : GameText.Intro.开始();

        pageText.text = $"{tip}　　({page + 1}/{pages.Count})";
    }

    void Update()
    {
        // 用 unscaledDeltaTime 的思路：timeScale = 0 时 Input 仍然有效，
        // 所以这里不需要特殊处理。
        if (Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetMouseButtonDown(0))
        {
            Next();
        }
    }

    void Next()
    {
        if (page < pages.Count - 1)
        {
            Show(page + 1);
            Sfx.Play(Sfx.Id.Pickup, 0.3f);
            return;
        }

        Close();
    }

    void Close()
    {
        Time.timeScale = 1f;
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(this);
    }

    void OnDestroy()
    {
        // 兜底：不管怎么被销毁，都不能让 timeScale 停在 0
        // （那样游戏会彻底卡死，是最难查的一类 bug）
        Time.timeScale = 1f;
    }
}
