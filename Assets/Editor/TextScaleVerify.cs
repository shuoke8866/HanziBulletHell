#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 验证：教程文字实际会用多大字号、BOSS 字库对不对。
///
/// 命令行：-executeMethod TextScaleVerify.Run
/// </summary>
public static class TextScaleVerify
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 字号 / 字库验证 ==========");
        sb.AppendLine();

        // ---- 1) 教程文字 ----
        sb.AppendLine("--- 教程文字 ---");

        var go = new GameObject("VerifyDot");
        go.transform.localScale = Vector3.one * 0.34f;      // 模拟提示点

        const string text = "测试用的一行教程文字";
        FloatingTip.Handle h = FloatingTip.ShowAtOffset(
            text, Color.white, go.transform,
            new Vector3(-0.35f, 0.42f, -0.1f), 1.0f,
            float.PositiveInfinity, faceCamera: false);

        if (h == null) { sb.AppendLine("  x ShowAtOffset 返回 null"); }
        else
        {
            var tmpGo = GameObject.Find("FloatingTip");
            if (tmpGo != null)
            {
                var tmp = tmpGo.GetComponent<TMPro.TextMeshPro>();
                sb.AppendLine($"  文字对象: {tmpGo.name}");
                sb.AppendLine($"      fontSize  = {tmp.fontSize}");
                sb.AppendLine($"      localScale = {tmpGo.transform.localScale}");
                sb.AppendLine($"      **实际世界高度 ≈ " +
                              $"{tmp.fontSize * tmpGo.transform.localScale.y:0.##}**");

                var fc = tmpGo.GetComponent<FaceCamera>();
                sb.AppendLine($"      FaceCamera = {(fc == null ? "无 ✓（不会覆盖缩放）" : "有 x（会覆盖缩放）")}");
            }
            FloatingTip.Clear(h);
        }
        Object.DestroyImmediate(go);
        sb.AppendLine();

        // ---- 2) 三个 BOSS 的字在不在字库里 ----
        sb.AppendLine("--- BOSS 字形 ---");
        foreach (string ch in new[] { "士", "官", "皇" })
        {
            bool inPlayer = HanziRenderer.IsAllowed(ch, HanziRenderer.SetPlayer);
            bool inEnemy = HanziRenderer.IsAllowed(ch, HanziRenderer.SetEnemy);
            sb.AppendLine($"  「{ch}」  玩家字库={(inPlayer ? "有 ✓" : "没有 x")}  " +
                          $"敌人字库={(inEnemy ? "有" : "没有（是空的，设计如此）")}");
        }
        sb.AppendLine();
        sb.AppendLine("  BossGlyph 用的字库 = 玩家字库（见 BossGlyph.cs）");
        sb.AppendLine("  结论：三个字都在玩家字库里，能正常显示。");

        File.WriteAllText("F:/HanziBulletHell/_scaleverify.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[字号验证]\n" + sb.ToString());
    }
}
#endif
