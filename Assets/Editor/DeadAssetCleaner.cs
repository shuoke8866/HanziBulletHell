#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 清理用不到的预制体 + 修掉会误导人的旧数据。
///
/// ## 背景
///
/// 玩家在排查 bug 时多次被"看起来有用、其实没人读"的数据误导，
/// 建议把用不到的删掉。这个工具做两件事：
///
///   1. 删掉**完全没人引用**的预制体（按 guid 在 .cs/.unity/.prefab 里查）
///   2. 保留在用的，但把里面**已经不被读取的旧字段**改成和数据库一致
///
/// 第 2 点很重要：`Enemy_Bing.prefab` 上写着 `maxHealth = 5`，
/// 而数据库里是 65 —— 虽然代码现在只读数据库、不读预制体，
/// 但任何人（包括我自己）看到那个 5 都会以为"小怪就是 5 血"。
/// 这种"看起来是真的、其实没人读"的值是最费时间的坑。
///
/// 菜单：Tools/清理无用资源
/// </summary>
public static class DeadAssetCleaner
{
    /// <summary>
    /// 确认没人引用的预制体。
    ///
    /// **这个名单是实测出来的**（查每个 prefab 的 guid 在项目里出现几次），
    /// 不是拍脑袋列的。加新条目之前请先重新跑一次 <see cref="AuditOnly"/>。
    /// </summary>
    static readonly string[] DeadPrefabs =
    {
        "Assets/Boss_Guan.prefab",
        "Assets/Boss_Huang.prefab",
        "Assets/Enemy_Kou.prefab",
        "Assets/Enemy_Ren.prefab",
        "Assets/Enemy_Shu.prefab",
        "Assets/Enemy_Yi.prefab",
        "Assets/FloatingStroke.prefab",
        "Assets/Square (1).prefab",
        "Assets/StrokePiece.prefab",
    };

    /// <summary>备份目录（放 Assets 外面，Unity 不会导入）</summary>
    static string BackupDir
    {
        get
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, "_removed_assets");
        }
    }

    [MenuItem("Tools/清理无用资源（先审计）")]
    public static void AuditOnly()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== 预制体引用审计 ==========");
        sb.AppendLine();

        // 把所有文本资源拼起来，查 guid 出现次数
        var all = new StringBuilder();
        string assets = Application.dataPath;
        foreach (string ext in new[] { "*.cs", "*.unity", "*.prefab", "*.asset" })
        {
            foreach (string f in Directory.GetFiles(assets, ext, SearchOption.AllDirectories))
                all.Append(File.ReadAllText(f));
        }
        string haystack = all.ToString();

        foreach (string f in Directory.GetFiles(assets, "*.prefab", SearchOption.AllDirectories))
        {
            string meta = f + ".meta";
            if (!File.Exists(meta)) continue;

            string guid = ReadGuid(meta);
            if (string.IsNullOrEmpty(guid)) continue;

            int n = CountOccurrences(haystack, guid);
            string rel = "Assets" + f.Substring(assets.Length).Replace('\\', '/');

            // 阈值是 **0**，不是 1。
            //
            // 第一版这里写的是 `n <= 1`，理由是"guid 在自己的 meta 里出现 1 次"，
            // 但 haystack 只扫了 .cs/.unity/.prefab/.asset —— **根本没包含 .meta**。
            // 于是在用的预制体（引用恰好 1 次）全被误判成"没人引用"。
            //
            // 教训：阈值依赖"扫描范围里包含什么"这个假设，
            // 假设一变阈值就错，而且错得很隐蔽（看着像对的）。
            sb.AppendLine($"  {(n == 0 ? "✗ 没人引用" : "✓ 在用    ")}  {rel}   ({n} 次)");
        }

        File.WriteAllText("F:/HanziBulletHell/_asset_audit.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[资源审计]\n" + sb.ToString());
    }

    [MenuItem("Tools/清理无用资源")]
    public static void Clean()
    {
        Directory.CreateDirectory(BackupDir);

        int moved = 0;

        foreach (string rel in DeadPrefabs)
        {
            string full = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, rel);

            if (!File.Exists(full))
            {
                Debug.LogWarning($"[清理] 找不到 {rel}，跳过");
                continue;
            }

            // 二次确认：真的没人引用才动手
            string guid = ReadGuid(full + ".meta");
            if (!string.IsNullOrEmpty(guid) && IsReferenced(guid, full))
            {
                Debug.LogError($"[清理] {rel} 其实**有人在引用**，拒绝删除。" +
                               "请先跑 Tools/清理无用资源（先审计）确认。");
                continue;
            }

            // 移到项目外的备份目录（不直接删，出问题还能拿回来）
            string dest = Path.Combine(BackupDir, Path.GetFileName(full));
            File.Move(full, dest);

            if (File.Exists(full + ".meta"))
                File.Move(full + ".meta", dest + ".meta");

            Debug.Log($"[清理] 已移出：{rel}");
            moved++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[清理] 共移出 {moved} 个无用预制体，备份在 {BackupDir}");
    }

    /// <summary>把预制体上"已经不被读取"的旧字段同步成数据库的值</summary>
    [MenuItem("Tools/清理无用资源/同步预制体上的旧血量")]
    public static void SyncStaleHealth()
    {
        int fixedCount = 0;

        foreach (string f in Directory.GetFiles(Application.dataPath, "*.prefab",
                                                SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            if (!name.StartsWith("Enemy_") && !name.StartsWith("Boss_")) continue;

            string text = File.ReadAllText(f);

            // prefab 里组件的序列化字段长这样：  maxHealth: 5
            var m = System.Text.RegularExpressions.Regex.Match(text, @"maxHealth: (\d+)");
            if (!m.Success) continue;

            int oldValue = int.Parse(m.Groups[1].Value);

            // 这个预制体对应数据库里哪个 id？靠 defId 找
            var idm = System.Text.RegularExpressions.Regex.Match(text, @"defId: (\w+)");
            if (!idm.Success) continue;

            EnemyDef def = EnemyDatabase.Get(idm.Groups[1].Value);
            if (def == null) continue;

            int want = Enemy.ScaledHealth(def.health);
            if (oldValue == want) continue;

            text = text.Substring(0, m.Index) + "maxHealth: " + want +
                   text.Substring(m.Index + m.Length);

            File.WriteAllText(f, text, new UTF8Encoding(false));

            Debug.Log($"[清理] {name}：预制体上的旧血量 {oldValue} → {want}" +
                      $"（和数据库一致，虽然代码已经不读它了，但留着会误导人）");
            fixedCount++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[清理] 同步了 {fixedCount} 个预制体的旧血量");
    }

    static string ReadGuid(string metaPath)
    {
        if (!File.Exists(metaPath)) return null;

        foreach (string line in File.ReadLines(metaPath))
            if (line.StartsWith("guid: ")) return line.Substring(6).Trim();

        return null;
    }

    /// <summary>
    /// 这个 guid 有没有被别的东西引用。
    ///
    /// **必须跳过该预制体自己的 .meta 文件** ——
    /// meta 里本来就写着 `guid: xxx`，不跳过的话每个预制体都会
    /// "引用自己"，检查永远返回 true，什么都删不掉。
    /// （第一版就踩了这个，结果 9 个里只删掉 5 个。）
    /// </summary>
    static bool IsReferenced(string guid, string selfPrefabPath)
    {
        string assets = Application.dataPath;
        string selfMeta = selfPrefabPath + ".meta";

        foreach (string ext in new[] { "*.cs", "*.unity", "*.prefab" })
        {
            foreach (string f in Directory.GetFiles(assets, ext, SearchOption.AllDirectories))
            {
                // 排除自己（虽然枚举用的是 .cs/.unity/.prefab，不含 .meta，
                // 但保留这层防护 —— 万一以后有人把 .meta 加进枚举）
                if (string.Equals(f, selfMeta, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.ReadAllText(f).Contains(guid)) return true;
            }
        }
        return false;
    }

    static int CountOccurrences(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        { n++; i += needle.Length; }
        return n;
    }
}
#endif
