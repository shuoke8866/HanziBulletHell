using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 屏幕中央上方的提示条（拾取笔画 / 合成成功 / 获得词条 / 清房）。
/// 独立于背包 UI，即使背包打开（timeScale = 0）也能显示，所以用 unscaledDeltaTime。
/// </summary>
public class GameToast : MonoBehaviour
{
    public static GameToast Instance { get; private set; }

    [Header("外观")]
    public float displayDuration = 1.8f;
    public float fadeDuration = 0.35f;
    public float verticalOffset = -120f;
    public float fontSize = 34f;

    TextMeshProUGUI label;
    CanvasGroup group;
    float timer;

    // 消息队列：背包暂停时也不会丢消息
    static readonly Queue<string> pending = new Queue<string>();
    static readonly Queue<Color> pendingColors = new Queue<Color>();

    public static void Show(string message, Color color)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (Instance != null)
        {
            Instance.Enqueue(message, color);
        }
        else
        {
            pending.Enqueue(message);
            pendingColors.Enqueue(color);
        }
    }

    public static void Show(string message)
    {
        Show(message, Color.white);
    }

    // 从队列取一条显示（已显示完则取下一条）
    void Enqueue(string message, Color color)
    {
        pending.Enqueue(message);
        pendingColors.Enqueue(color);
    }

    void Awake()
    {
        Instance = this;
        BuildUI();
    }

    void Start()
    {
        // 把 Instance 出现之前积累的消息补上
        if (pending.Count > 0) Next();
        else Hide();
    }

    void BuildUI()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();

        GameObject root = new GameObject("GameToast (Runtime)");
        if (canvas != null)
            root.transform.SetParent(canvas.transform, false);

        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, verticalOffset);
        rt.sizeDelta = new Vector2(900f, 120f);

        group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);
        RectTransform trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.text = "";
    }

    void Update()
    {
        // 用 unscaled 时间：背包暂停（timeScale=0）时提示依然能淡出
        float dt = Time.unscaledDeltaTime;

        if (timer <= 0f) return;

        timer -= dt;

        if (timer <= 0f)
        {
            Next();
            return;
        }

        // 淡入淡出
        if (timer > displayDuration)
        {
            float t = (timer - displayDuration) / Mathf.Max(0.01f, fadeDuration);
            group.alpha = Mathf.Clamp01(t);
        }
        else if (timer < fadeDuration)
        {
            group.alpha = Mathf.Clamp01(timer / Mathf.Max(0.01f, fadeDuration));
        }
        else
        {
            group.alpha = 1f;
        }
    }

    void Next()
    {
        if (pending.Count == 0)
        {
            Hide();
            return;
        }

        string msg = pending.Dequeue();
        Color c = pendingColors.Count > 0 ? pendingColors.Dequeue() : Color.white;

        label.text = msg;
        label.color = c;
        group.alpha = 1f;
        timer = displayDuration + fadeDuration;
    }

    void Hide()
    {
        timer = 0f;
        if (group != null) group.alpha = 0f;
        if (label != null) label.text = "";
    }
}
