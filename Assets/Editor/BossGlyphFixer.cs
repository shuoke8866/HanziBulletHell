#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 给三个 BOSS 预制体**预先**装好字形渲染。
///
/// ## 为什么要在预制体上做，而不是运行时 AddComponent
///
/// 原来是在 BOSS 的 Start() 里 `AddComponent&lt;BossGlyph&gt;()`。
/// 问题：
///   · 依赖运行时执行顺序（BossGlyph.Start 和 Boss.Start 谁先跑不保证）
///   · 场景里看不出 BOSS 会显示什么字，只能靠读代码
///   · 编辑器里预览不到
///
/// 现在直接改预制体：
///   1. 删掉预制体里那些**手拼的残留图形**（Square / guan 之类）
///      —— 它们的贴图 guid 早就失效了，而且是从别的 BOSS 复制过来的
///      （Boss_Huang 顶着「官」的脸就是这个原因）
///   2. 加一个 `BossGlyph` 组件，character 设成这个 BOSS 该显示的字
///
/// 不碰 BOSS 脚本 —— 那些残留图形删掉之后，"官"就不会再出现了。
///
/// 菜单：Tools/修 BOSS 字形
/// 命令行：-executeMethod BossGlyphFixer.Run
/// </summary>
public static class BossGlyphFixer
{
    /// <summary>预制体 → 该显示的字</summary>
    static readonly string[,] Targets =
    {
        { "Assets/Boss_Shi 1.prefab", "士" },
        { "Assets/Boss_Guan.prefab",  "官" },
        { "Assets/Boss_Huang.prefab", "皇" },
    };

    [MenuItem("Tools/修 BOSS 字形")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 修 BOSS 字形 ==========");
        sb.AppendLine();

        for (int i = 0; i < Targets.GetLength(0); i++)
        {
            string path = Targets[i, 0];
            string ch = Targets[i, 1];

            if (!File.Exists(path)) { sb.AppendLine($"  ✗ 找不到 {path}"); continue; }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine($"  ✗ 打不开 {path}"); continue; }

            sb.AppendLine($"  【{Path.GetFileName(path)}】要显示的字 = 「{ch}」");

            // ---- 1) 删掉手拼的残留图形 ----
            //
            // **只删明确是残留的名字**，不用"凡是只有 SpriteRenderer 就删"这种宽条件。
            //
            // 第一版就是这么写的，结果连预制体的结构对象一起删了，
            // 报 MissingReferenceException（删掉父物体之后子物体的引用就失效了）。
            //
            // 残留的特征（从实际文件里读出来的）：
            //   · 名字叫 "Square" / "Square (2)" / "Square (9)" ... —— 手拼方块
            //   · 名字叫 "guan" —— 从 Boss_Guan 复制过来时忘删的字形
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;

                string n = t.name;
                bool isResidue = n == "guan" || n == "huang" || n == "shi" ||
                                 n == "Square" || n.StartsWith("Square (");

                if (isResidue) doomed.Add(t.gameObject);
            }

            foreach (GameObject go in doomed)
            {
                if (go == null) continue;
                sb.AppendLine($"      删除残留图形：{go.name}");
                Object.DestroyImmediate(go);
            }
            if (doomed.Count == 0) sb.AppendLine("      （没有残留图形）");

            // ---- 2) 装 BossGlyph ----
            BossGlyph glyph = root.GetComponent<BossGlyph>();
            if (glyph == null)
            {
                glyph = root.AddComponent<BossGlyph>();
                sb.AppendLine("      挂上 BossGlyph");
            }
            else sb.AppendLine("      已有 BossGlyph，只改字");

            glyph.character = ch;
            glyph.hideLegacyArt = true;

            // ---- 3) 确保根上有碰撞体（不然打不中）----
            if (root.GetComponent<Collider2D>() == null)
            {
                var box = root.AddComponent<BoxCollider2D>();
                box.size = new Vector2(2.5f, 2.5f);
                box.isTrigger = true;
                sb.AppendLine("      ⚠ 原本没有 Collider2D，补了一个 2.5×2.5 触发器");
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            sb.AppendLine();
        }

        AssetDatabase.Refresh();

        File.WriteAllText("F:/HanziBulletHell/_bossglyph_fix.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[BOSS 字形]\n" + sb.ToString());
    }
}
#endif
