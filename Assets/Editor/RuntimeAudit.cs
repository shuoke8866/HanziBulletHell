#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时实测：
///   1. BOSS 身上到底有几个字形对象、各显示什么字
///   2. BOSS 房生成出来的到底是不是 BossHuang
///   3. BOSS 能不能被打掉血
///   4. 玩家血条到底建出来没有、在哪、可见吗
///
/// ## 为什么这次不静态分析
///
/// 前几轮我靠"读场景文件 / 读预制体"做了很多推断，反复猜错。
/// 这次直接在编辑器里**实例化真实预制体、调用真实方法、查真实组件**。
///
/// 命令行：-executeMethod RuntimeAudit.Run
/// </summary>
public static class RuntimeAudit
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 运行时实测 ==========");
        sb.AppendLine();

        // ============================================================
        //  Level3 BOSS 房
        // ============================================================
        EditorSceneManager.OpenScene("Assets/Scenes/Level3.unity", OpenSceneMode.Single);
        GameData.currentFloor = 3;

        sb.AppendLine("===== Level3 BOSS 房 =====");

        Room bossRoom = null;
        foreach (Room r in Object.FindObjectsOfType<Room>())
            if (r.roomType == RoomType.Boss) bossRoom = r;

        if (bossRoom == null) { sb.AppendLine("  ❌ 找不到 BOSS 房"); }
        else
        {
            sb.AppendLine($"  房间 = {bossRoom.name}  预制体 {bossRoom.enemyPrefabs.Count} 个");
            sb.AppendLine($"  第 1 波数量 = {bossRoom.PreviewFirstWaveCount()}");
            sb.AppendLine();

            foreach (GameObject prefab in bossRoom.enemyPrefabs)
            {
                if (prefab == null) { sb.AppendLine("  ❌ 预制体是空引用"); continue; }

                sb.AppendLine($"  预制体 = {prefab.name}");

                // 实例化一份，模拟真实生成
                GameObject clone = Object.Instantiate(prefab);
                clone.transform.position = new Vector3(32f, 0f, 0f);

                sb.AppendLine("      --- 克隆上的组件 ---");
                foreach (Component c in clone.GetComponents<Component>())
                    sb.AppendLine($"          {(c == null ? "**MissingScript**" : c.GetType().Name)}");

                sb.AppendLine("      --- 所有子物体（含字形） ---");
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                {
                    if (t == clone.transform) continue;

                    var sr = t.GetComponent<SpriteRenderer>();
                    var hr = t.GetComponent<HanziRenderer>();
                    var tm = t.GetComponent<TMPro.TextMeshPro>();

                    var bits = new List<string>();
                    if (hr != null) bits.Add($"HanziRenderer(character={hr.character})");
                    if (tm != null) bits.Add($"TMP(\"{tm.text}\")");
                    if (sr != null) bits.Add(sr.enabled ? "SpriteRenderer(启用)" : "SpriteRenderer(停用)");

                    sb.AppendLine($"          {t.name}  [{string.Join(", ", bits.ToArray())}]");
                }

                // 调真实方法，确认字是什么
                var huang = clone.GetComponent<BossHuang>();
                if (huang != null)
                    sb.AppendLine($"      BossHuang.glyphChar = \"{huang.glyphChar}\"");

                // 手动跑一次 Start 的效果（模拟运行时）
                sb.AppendLine("      --- 模拟 Start ---");
                clone.SetActive(true);

                var glyph = clone.GetComponent<BossGlyph>();
                sb.AppendLine($"          挂 BossGlyph 之前 = {(glyph == null ? "无" : "有")}");

                // 直接调它的 Build 看会生成什么
                if (glyph == null) glyph = clone.AddComponent<BossGlyph>();
                if (huang != null) glyph.character = huang.glyphChar;
                glyph.Build();

                sb.AppendLine("      --- Build 之后所有子物体 ---");
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                {
                    if (t == clone.transform) continue;

                    var hr = t.GetComponent<HanziRenderer>();
                    var sr = t.GetComponent<SpriteRenderer>();

                    var bits = new List<string>();
                    if (hr != null) bits.Add($"HanziRenderer(character=\"{hr.character}\")");
                    if (sr != null) bits.Add(sr.enabled ? "Sprite(启用)" : "Sprite(停用)");

                    sb.AppendLine($"          {t.name}  [{string.Join(", ", bits.ToArray())}]");
                }

                // ---- 伤害测试 ----
                sb.AppendLine("      --- 伤害测试 ---");
                var dmg = clone.GetComponent<IDamageable>();
                if (dmg == null) sb.AppendLine("          ❌ 不是 IDamageable，打不掉血！");
                else
                {
                    sb.AppendLine($"          初始血量 = {dmg.CurrentHealth}/{dmg.MaxHealth}");
                    if (dmg.CurrentHealth <= 0)
                        sb.AppendLine("          ❌ **初始血量就是 0** —— Start 没跑（对象没激活过）");

                    dmg.TakeDamage(10, Vector2.zero);
                    sb.AppendLine($"          打 10 点之后 = {dmg.CurrentHealth}/{dmg.MaxHealth}");
                    if (dmg.CurrentHealth <= 0 && dmg.MaxHealth > 0)
                        sb.AppendLine("          ✓ 掉血正常");
                }

                Object.DestroyImmediate(clone);
                sb.AppendLine();
            }
        }

        // ============================================================
        //  血条
        // ============================================================
        sb.AppendLine("===== 玩家血条 =====");
        sb.AppendLine($"  场景里的 PlayerHealthBarUI = " +
                      $"{(Object.FindObjectOfType<PlayerHealthBarUI>() != null ? "有" : "无（运行时才建）")}");
        sb.AppendLine($"  场景里的 Canvas 数 = {Object.FindObjectsOfType<Canvas>(true).Length}");
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>(true))
            sb.AppendLine($"      {c.name}  order={c.sortingOrder}  " +
                          $"active={c.gameObject.activeInHierarchy}  " +
                          $"根={(c.transform.parent == null ? "是" : "否")}");

        // 找场景里所有 UI Image，看有没有可能盖住左上角
        sb.AppendLine();
        sb.AppendLine("  可能盖住左上角的 UI：");
        int found = 0;
        foreach (Image img in Object.FindObjectsOfType<Image>(true))
        {
            RectTransform rt = img.rectTransform;
            if (rt == null) continue;

            // 左上角区域大致是 anchor (0,1)
            if (rt.anchorMin.x < 0.3f && rt.anchorMax.y > 0.8f)
            {
                sb.AppendLine($"      {img.transform.name}  anchorMin={rt.anchorMin} " +
                              $"anchorMax={rt.anchorMax}  激活={img.gameObject.activeInHierarchy}");
                found++;
            }
        }
        if (found == 0) sb.AppendLine("      （没有）");

        File.WriteAllText("F:/HanziBulletHell/_runtimeaudit.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[运行时实测]\n" + sb.ToString());
    }
}
#endif
