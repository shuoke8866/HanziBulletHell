using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 在玩家**正上方**飘一行字，然后淡出。
///
/// 用途：拾取笔画时告诉玩家"我刚才捡到了什么"。
///
/// 为什么不用 GameToast：那个是屏幕角落的提示条，
/// 玩家眼睛盯着角色，很容易错过。飘在角色头顶的文字是**视线焦点内的反馈**，
/// 这在动作游戏里是标准做法。
///
/// 实现要点：
///   · 世界空间 TextMeshPro（不是 Canvas UI）—— 这样它能跟着玩家在世界里移动
///   · 用 <see cref="FaceCamera"/> 抵消父物体的旋转/缩放，保证文字永远正对镜头
///   · 和 Portal 的提示用的是同一套做法，观感一致
/// </summary>
public static class FloatingTip
{
    /// <summary>飘字往上升多高</summary>
    const float RiseHeight = 1.1f;

    /// <summary>停留时长（秒）</summary>
    const float Lifetime = 1.1f;

    /// <summary>同时最多几个，防止刷屏</summary>
    const int MaxLive = 4;

    static int liveCount;

    /// <summary>
    /// 在某个世界坐标上方飘一行字。
    ///
    /// <paramref name="above"/> 传 null 时用玩家当前位置 ——
    /// 这是最常用的用法（"在我头上提示"）。
    /// </summary>
    public static void Show(string text, Color color, Transform above = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (liveCount >= MaxLive) return;

        Transform anchor = above;
        Vector3 pos;

        if (anchor != null)
        {
            pos = anchor.position;
        }
        else
        {
            Transform p = PlayerRef.Transform;
            if (p == null) return;      // 没有玩家就不显示（比如编辑器里）
            anchor = p;
            pos = p.position;
        }

        // 生成一个临时对象，挂在玩家身上 —— 这样玩家走动时字会跟着走
        var go = new GameObject("FloatingTip");

        // 挪到玩家上方一点点（1.0 世界单位约等于一个身位）
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = new Vector3(0f, 1.0f, -0.5f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 3.4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.sortingOrder = 120;          // 盖住大部分东西
        tmp.fontStyle = FontStyles.Bold;

        // 抵消父物体的旋转与缩放（玩家的 transform 会水平翻转，不抵消字会镜像）
        var face = go.AddComponent<FaceCamera>();
        face.cancelParentRotation = true;
        face.cancelParentScale = true;

        liveCount++;
        go.AddComponent<FloatingTipRunner>().Init(tmp, pos);
    }

    /// <summary>实际驱动动画的小组件（用组件而不是静态协程，方便随对象销毁一起清理）</summary>
    class FloatingTipRunner : MonoBehaviour
    {
        TextMeshPro tmp;
        Vector3 startLocal;
        float t;

        public void Init(TextMeshPro text, Vector3 unusedWorldPos)
        {
            tmp = text;
            startLocal = transform.localPosition;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;

            float k = Mathf.Clamp01(t / Lifetime);

            // 先快后慢地升起（缓出），比匀速好看
            float rise = RiseHeight * (1f - (1f - k) * (1f - k));
            transform.localPosition = startLocal + new Vector3(0f, rise, 0f);

            // 前 60% 完全不透明，之后淡出
            float alpha = k < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, k);
            if (tmp != null)
            {
                Color c = tmp.color;
                c.a = alpha;
                tmp.color = c;
            }

            if (k >= 1f)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            // 计数只在这里还回去 —— 不管是自然到期还是被别处销毁，
            // 都会走 OnDestroy，所以不会漏、也不会重复减。
            //
            // （一开始我在 Update 里也减了一次，那样会重复计数、
            //   飘字会越来越少直到完全不显示。）
            liveCount = Mathf.Max(0, liveCount - 1);
        }
    }
}
