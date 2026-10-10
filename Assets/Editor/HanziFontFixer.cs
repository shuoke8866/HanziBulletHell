#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 从开源字体重新生成 TMP 中文字体图集，并把场景里所有文本换过去。
///
/// 背景：原项目用的是微软专有字体 SimSun，不能放进公开仓库（再分发侵权），
/// 而且旧的图集只烘焙了 20 来个字符，UI 中文全是"豆腐块"。
///
/// 现在改用 Noto Sans SC（SIL OFL 1.1，可自由再分发），并自动：
///   1. 扫描 Assets 下所有 .cs，收集游戏里真正会用到的每一个汉字
///   2. 生成 Dynamic 图集并预烘焙这些字
///   3. 设为 TMP 默认字体，替换所有场景里的 TMP 文本与默认字体
///
/// 无需手动调用：CI 构建（或你本地首次打开工程）时会自动执行一次。
/// 也可手动：Unity 菜单栏 → Tools → 重建中文字体（Noto Sans SC）
/// </summary>
[InitializeOnLoad]
public static class HanziFontFixer
{
    const string SourceFontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
    const string OutputAssetPath = "Assets/Fonts/NotoSansSC SDF.asset";
    const string OutputMaterialPath = "Assets/Fonts/NotoSansSC SDF Material.mat";

    // 标记文件：内容变了就重新烘焙
    const string MarkerPath = "Assets/Fonts/.font_baked";

    static HanziFontFixer()
    {
        // 等资源导入完成后再执行，避免在导入过程中改资源
        EditorApplication.delayCall += AutoRun;
    }

    static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // 已经烘焙过且源字体没变 → 跳过
        if (File.Exists(MarkerPath) &&
            File.Exists(OutputAssetPath) &&
            File.GetLastWriteTimeUtc(MarkerPath) >= File.GetLastWriteTimeUtc(SourceFontPath))
            return;

        Debug.Log("[HanziFontFixer] 检测到需要重建中文字体图集，开始…");
        Rebuild();
    }

    [MenuItem("Tools/重建中文字体（Noto Sans SC）")]
    public static void Rebuild()
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (source == null)
        {
            Debug.LogError($"[HanziFontFixer] 找不到源字体 {SourceFontPath}，" +
                           "请确认 Assets/Fonts/NotoSansSC-Regular.otf 存在。");
            return;
        }

        // 1) 生成动态图集
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
            source,
            90,                 // 采样点大小
            9,                  // 内边距
            GlyphRenderMode.SDFAA,
            2048, 2048,         // 图集尺寸
            AtlasPopulationMode.Dynamic,
            true);              // 启用多图集，防止溢出

        if (font == null)
        {
            Debug.LogError("[HanziFontFixer] CreateFontAsset 返回 null。");
            return;
        }

        font.name = "NotoSansSC SDF";

        // 2) 收集游戏里会用到的全部汉字
        string wanted = CollectCharacters();
        bool ok = font.TryAddCharacters(wanted);
        Debug.Log($"[HanziFontFixer] 预烘焙 {(ok ? "全部成功" : "有缺字")}，共请求 {wanted.Length} 个字符");

        // 3) 存为资产（贴图和材质要一起存，否则重建后会丢）
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath) != null)
            AssetDatabase.DeleteAsset(OutputAssetPath);

        if (font.material != null)
        {
            font.material.name = "NotoSansSC SDF Material";
        }

        AssetDatabase.CreateAsset(font, OutputAssetPath);

        if (font.atlasTextures != null)
        {
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                Texture2D tex = font.atlasTextures[i];
                if (tex == null) continue;
                tex.name = $"NotoSansSC SDF Atlas {i}";
                AssetDatabase.AddObjectToAsset(tex, font);
            }
        }
        if (font.material != null)
            AssetDatabase.AddObjectToAsset(font.material, font);

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HanziFontFixer] 图集已生成：{OutputAssetPath}");

        // 4) 设为默认字体，并把所有引用旧字体的文本修好
        ApplyToProject(font);

        File.WriteAllText(MarkerPath, System.DateTime.UtcNow.ToString("o"));
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 把项目里 TMP 文本的字体统一修好。分两步：
    ///   1. 把 Noto 挂成 LiberationSans 的 fallback —— 这样即使某个文本漏改，
    ///      运行时也会自动回退到 Noto，不会出现"豆腐块"。
    ///   2. 遍历构建场景，把字体为空、或指向已失效资产（旧专有字体被删后）的文本换成 Noto。
    /// </summary>
    static void ApplyToProject(TMP_FontAsset font)
    {
        // --- 步骤 1：设默认字体 + 挂 fallback ---
        if (TMP_Settings.instance != null)
        {
            var so = new SerializedObject(TMP_Settings.instance);

            SerializedProperty def = so.FindProperty("m_defaultFontAsset");
            if (def != null) def.objectReferenceValue = font;

            // 给默认字体挂上 Noto 作为兜底，避免漏网的文本显示方框
            SerializedProperty fallbacks = so.FindProperty("m_fallbackFontAssets");
            if (fallbacks != null)
            {
                fallbacks.ClearArray();
                fallbacks.InsertArrayElementAtIndex(0);
                fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = font;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(TMP_Settings.instance);
        }

        // 也给 LiberationSans 挂 fallback（它是 TMP 出厂默认字体）
        TMP_FontAsset liberation =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (liberation != null)
        {
            if (liberation.fallbackFontAssetTable == null)
                liberation.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!liberation.fallbackFontAssetTable.Contains(font))
            {
                liberation.fallbackFontAssetTable.Add(font);
                EditorUtility.SetDirty(liberation);
                Debug.Log("[HanziFontFixer] 已把 NotoSansSC 挂为 LiberationSans 的 fallback。");
            }
        }

        // --- 步骤 2：遍历构建场景替换文本字体 ---
        int replaced = ReplaceInOpenScenes(font);

        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == s.path) continue;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                s.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            replaced += ReplaceInOpenScenes(font);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        // --- 步骤 3：兜底 —— 直接改场景文件里指向"已删除字体资产"的引用 ---
        //
        // 步骤 2 依赖 FindObjectsOfType，而它跳过当前打开的场景，
        // 所以被打开的那个场景（通常是 Level1）会被漏掉。
        // 这里直接读场景文件文本，把任何"指向已不存在资产"的字体引用换成新字体，幂等且不依赖场景状态。
        replaced += RewriteStaleFontGuids(font);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HanziFontFixer] 已修正 {replaced} 处字体引用。");
    }

    /// <summary>
    /// 扫描构建设置里的场景文件，把指向已删除字体资产的 GUID 替换为新字体的 GUID。
    /// 直接操作文件的文本内容，因此不受"场景是否打开"影响。
    /// </summary>
    static int RewriteStaleFontGuids(TMP_FontAsset font)
    {
        string newGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(font));
        if (string.IsNullOrEmpty(newGuid))
        {
            Debug.LogWarning("[HanziFontFixer] 取不到新字体的 GUID，跳过兜底修复。");
            return 0;
        }

        int total = 0;
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;
            if (!File.Exists(s.path)) continue;

            string text = File.ReadAllText(s.path, Encoding.UTF8);
            int changed = 0;

            // 匹配所有 m_fontAsset 引用，逐个判断资产是否还存在
            text = Regex.Replace(text,
                @"m_fontAsset: \{fileID: (-?\d+), guid: ([0-9a-fA-F]{32}), type: \d+\}",
                m =>
                {
                    string guid = m.Groups[2].Value;
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    // 资产存在就保留
                    if (!string.IsNullOrEmpty(path)) return m.Value;

                    // 资产已删除 → 换成新字体
                    changed++;
                    return $"m_fontAsset: {{fileID: {m.Groups[1].Value}, guid: {newGuid}, type: 3}}";
                });

            if (changed > 0)
            {
                File.WriteAllText(s.path, text, new UTF8Encoding(false));
                Debug.Log($"[HanziFontFixer] {System.IO.Path.GetFileName(s.path)}: " +
                          $"修复 {changed} 处指向已删除字体的引用。");
                total += changed;
            }
        }

        if (total > 0) AssetDatabase.Refresh();
        return total;
    }

    static int ReplaceInOpenScenes(TMP_FontAsset font)
    {
        int n = 0;

        foreach (var t in Object.FindObjectsOfType<TextMeshProUGUI>(true))
        {
            if (NeedsFix(t.font))
            {
                t.font = font;
                if (font.material != null) t.fontSharedMaterial = font.material;
                EditorUtility.SetDirty(t);
                n++;
            }
        }

        foreach (var t in Object.FindObjectsOfType<TextMeshPro>(true))
        {
            if (NeedsFix(t.font))
            {
                t.font = font;
                if (font.material != null) t.fontSharedMaterial = font.material;
                EditorUtility.SetDirty(t);
                n++;
            }
        }
        return n;
    }

    /// <summary>字体为空，或指向已删除的资产（Unity 的"假 null"）→ 需要修</summary>
    static bool NeedsFix(TMP_FontAsset current)
    {
        // 被删除的资产在 C# 里不等于 null，但 == null 重载 + 名字为空可识别
        if (current == null) return true;
        return string.IsNullOrEmpty(current.name);
    }

    /// <summary>扫描所有脚本，收集里面出现过的每一个 CJK 字符</summary>
    static string CollectCharacters()
    {
        var set = new HashSet<char>();

        string[] files = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories);
        foreach (string f in files)
        {
            string text;
            try { text = File.ReadAllText(f, Encoding.UTF8); }
            catch { continue; }

            foreach (Match m in Regex.Matches(text, @"[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF]"))
                set.Add(m.Value[0]);
        }

        // 兜底：数字、拉丁字母、常用符号（UI 里一定会用到）
        foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz /|+-%×.:,()[]")
            set.Add(c);

        var sb = new StringBuilder();
        foreach (char c in set) sb.Append(c);
        return sb.ToString();
    }
}
#endif
