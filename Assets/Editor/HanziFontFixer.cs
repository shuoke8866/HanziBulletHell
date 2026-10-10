#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从开源字体生成 TMP 中文字体图集，并把场景里所有文本换过去。
///
/// 背景：原项目用的是微软专有字体 SimSun，不能放进公开仓库（再分发侵权），
/// 已改用 Noto Sans SC（SIL OFL 1.1，可自由再分发）。
///
/// 字符来源（只收"真正会显示"的字，不再把中文注释也算进去）：
///   1. Assets 下所有 .cs 的**字符串字面量**（先剥掉注释和块注释）
///   2. HanziRenderer 里定义的全部汉字字形
///   3. 三个场景里 TMP 组件的 m_text 内容
///   4. 数字、拉丁字母与常用标点
///
/// 图集参数刻意压小：WebGL 首屏要把整张图集下载下来，
/// 90pt 采样 + 2048² 会产出 16 MB 贴图，是构建体积的大头。
///
/// 无需手动调用：CI 构建或首次打开工程时会自动执行。
/// 也可手动：Unity 菜单栏 → Tools → 重建中文字体（Noto Sans SC）
/// </summary>
[InitializeOnLoad]
public static class HanziFontFixer
{
    const string SourceFontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
    const string OutputAssetPath = "Assets/Fonts/NotoSansSC SDF.asset";
    const string MarkerPath = "Assets/Fonts/.font_baked";

    // ---- 图集参数（改这里就能调整体积/清晰度平衡）----
    /// <summary>采样点大小。越小图集越省，但大字号 UI 会糊</summary>
    const int SamplingPointSize = 48;
    /// <summary>字形内边距（SDF 需要留边避免溢出）</summary>
    const int AtlasPadding = 4;
    /// <summary>图集边长。1024² 的 RGBA32 贴图 = 4 MB，2048² 则是 16 MB</summary>
    const int AtlasWidth = 1024;
    const int AtlasHeight = 1024;

    static HanziFontFixer()
    {
        // 等资源导入完成后再执行，避免在导入过程中改资源
        EditorApplication.delayCall += AutoRun;
    }

    static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureBaked(false);
    }

    /// <summary>
    /// 确保图集与当前参数/源字体一致；不一致才重建。
    ///
    /// 供 WebGLBuilder 在构建前显式调用 —— 不能只依赖 [InitializeOnLoad]+delayCall，
    /// 因为 -batchmode -quit 会在 delayCall 触发前就构建完退出。
    /// </summary>
    public static void EnsureBaked(bool force)
    {
        if (!force && IsUpToDate()) return;

        Debug.Log("[HanziFontFixer] 字体图集需要重建（源字体或生成脚本有更新）。");
        Rebuild();
    }

    /// <summary>
    /// 标记文件比「源字体」和「本脚本」都新 → 无需重建。
    /// 把脚本自身也纳入比较，是为了让"改了图集参数"这件事能真正触发重建 ——
    /// 否则调了 SamplingPointSize 却不会生效，构建会悄悄沿用旧图集。
    /// </summary>
    static bool IsUpToDate()
    {
        if (!File.Exists(MarkerPath) || !File.Exists(OutputAssetPath)) return false;

        System.DateTime marker = File.GetLastWriteTimeUtc(MarkerPath);
        if (marker < File.GetLastWriteTimeUtc(SourceFontPath)) return false;

        const string SelfPath = "Assets/Editor/HanziFontFixer.cs";
        if (File.Exists(SelfPath) && marker < File.GetLastWriteTimeUtc(SelfPath)) return false;

        return true;
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

        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
            source,
            SamplingPointSize,
            AtlasPadding,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            AtlasWidth,
            AtlasHeight,
            AtlasPopulationMode.Dynamic,
            true);   // 允许多图集：万一字太多溢出到第二页也不会丢字

        if (font == null)
        {
            Debug.LogError("[HanziFontFixer] CreateFontAsset 返回 null。");
            return;
        }

        font.name = "NotoSansSC SDF";

        string wanted = CollectCharacters();
        bool ok = font.TryAddCharacters(wanted);
        Debug.Log($"[HanziFontFixer] 请求烘焙 {wanted.Length} 个字符，" +
                  $"{(ok ? "全部成功" : "部分缺字（缺的字会在运行时按需生成）")}；" +
                  $"采样 {SamplingPointSize}pt / 图集 {AtlasWidth}x{AtlasHeight}");

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputAssetPath) != null)
            AssetDatabase.DeleteAsset(OutputAssetPath);

        if (font.material != null) font.material.name = "NotoSansSC SDF Material";
        AssetDatabase.CreateAsset(font, OutputAssetPath);

        if (font.atlasTextures != null)
        {
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                Texture2D tex = font.atlasTextures[i];
                if (tex == null) continue;
                tex.name = $"NotoSansSC SDF Atlas {i}";
                AssetDatabase.AddObjectToAsset(tex, font);
                Debug.Log($"[HanziFontFixer] 图集 {i}: {tex.width}x{tex.height} " +
                          $"= {tex.width * tex.height * 4 / 1048576f:0.0} MB (RGBA32)");
            }
        }
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ApplyToProject(font);

        File.WriteAllText(MarkerPath, System.DateTime.UtcNow.ToString("o"));
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 收集游戏里真正会显示出来的字符。
    /// 关键：先剥掉注释再取字符串字面量 —— 否则源码里的中文注释会被当成"需要的字"烘焙进去。
    /// </summary>
    static string CollectCharacters()
    {
        var set = new HashSet<char>();

        // 1) 代码里的字符串字面量（剥掉注释后）
        foreach (string f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(f, Encoding.UTF8); }
            catch { continue; }

            text = Regex.Replace(text, @"//[^\n]*", "");       // 行注释
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline); // 块注释

            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                AddAll(set, UnescapeLiteral(m.Groups[1].Value));
        }

        // 2) HanziRenderer 定义的字形（Save("一") 形式）
        string rendererPath = "Assets/Scripts/HanziRenderer.cs";
        if (File.Exists(rendererPath))
        {
            string src = File.ReadAllText(rendererPath, Encoding.UTF8);
            foreach (Match m in Regex.Matches(src, "Save\\(\"(.)\"\\)"))
                set.Add(m.Groups[1].Value[0]);
            // 同时兜住 case "x" 这类写法
            foreach (Match m in Regex.Matches(src, "case\\s+\"(.)\""))
                set.Add(m.Groups[1].Value[0]);
        }

        // 3) 场景里 TMP 组件的 m_text（含 \uXXXX 转义）
        foreach (var bs in EditorBuildSettings.scenes)
        {
            if (!bs.enabled || string.IsNullOrEmpty(bs.path) || !File.Exists(bs.path)) continue;

            string scene = File.ReadAllText(bs.path, Encoding.UTF8);
            foreach (Match m in Regex.Matches(scene, @"(?m)^\s+m_text:\s*(.+)$"))
            {
                string raw = m.Groups[1].Value.Trim();
                if (raw.StartsWith("m_")) continue;

                raw = Regex.Replace(raw, @"\\u([0-9A-Fa-f]{4})",
                    mm => ((char)int.Parse(mm.Groups[1].Value,
                        System.Globalization.NumberStyles.HexNumber)).ToString());
                AddAll(set, raw);
            }
        }

        // 4) 数字、拉丁字母、常用标点与中文标点
        AddAll(set, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz " +
                    "/|+-%.:,()[]<>!?*#=~^&@$'\"");
        AddAll(set, "：·、。，！？（）「」【】《》…—～×→←↑↓√");

        var sb = new StringBuilder();
        foreach (char c in set) sb.Append(c);
        return sb.ToString();
    }

    static void AddAll(HashSet<char> set, string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        foreach (char c in s)
        {
            if (c == '\\' || c == '\n' || c == '\r' || c == '\t') continue;
            if (c < 0x20) continue;
            set.Add(c);
        }
    }

    /// <summary>
    /// 手工处理字符串字面量里的转义。
    /// 不能用 Regex.Unescape：它会把正则元字符（\s \d 等）当成非法转义序列抛异常，
    /// 而且对 \\ 的处理也不符合 C# 字面量语义。
    /// 这里只认 C# 常见转义，其余原样保留。
    /// </summary>
    static string UnescapeLiteral(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }

            char n = s[i + 1];
            switch (n)
            {
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case '0': sb.Append('\0'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case '"': sb.Append('"'); i++; break;
                case '\'': sb.Append('\''); i++; break;
                case 'u':
                    if (i + 5 < s.Length && TryParseHex(s.Substring(i + 2, 4), out char u))
                    { sb.Append(u); i += 5; }
                    else sb.Append(n);
                    break;
                default: sb.Append(n); i++; break;
            }
        }
        return sb.ToString();
    }

    static bool TryParseHex(string hex, out char value)
    {
        value = '\0';
        if (hex.Length != 4) return false;
        int v = 0;
        foreach (char c in hex)
        {
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
            else return false;
            v = v * 16 + d;
        }
        value = (char)v;
        return true;
    }

    /// <summary>
    /// 把项目里 TMP 文本的字体统一修好：
    ///   1. Noto 设为默认字体，并挂成 LiberationSans 的 fallback（漏改也不会显示豆腐块）
    ///   2. 遍历构建场景替换文本字体
    ///   3. 兜底：直接改场景文件里指向"已删除资产"的字体引用
    /// </summary>
    static void ApplyToProject(TMP_FontAsset font)
    {
        if (TMP_Settings.instance != null)
        {
            var so = new SerializedObject(TMP_Settings.instance);

            SerializedProperty def = so.FindProperty("m_defaultFontAsset");
            if (def != null) def.objectReferenceValue = font;

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

        TMP_FontAsset liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (liberation != null)
        {
            if (liberation.fallbackFontAssetTable == null)
                liberation.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!liberation.fallbackFontAssetTable.Contains(font))
            {
                liberation.fallbackFontAssetTable.Add(font);
                EditorUtility.SetDirty(liberation);
            }
        }

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

        replaced += RewriteStaleFontGuids(font);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HanziFontFixer] 已修正 {replaced} 处字体引用。");
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

    /// <summary>字体为空，或指向已删除资产 → 需要修</summary>
    static bool NeedsFix(TMP_FontAsset current)
    {
        if (current == null) return true;
        return string.IsNullOrEmpty(current.name);
    }

    /// <summary>
    /// 扫描构建设置里的场景文件，把指向"已不存在字体资产"的 GUID 换成新字体。
    /// 直接操作文件文本，因此不受"场景是否打开"影响（ReplaceInOpenScenes 会跳过当前场景）。
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
            if (!s.enabled || string.IsNullOrEmpty(s.path) || !File.Exists(s.path)) continue;

            string text = File.ReadAllText(s.path, Encoding.UTF8);
            int changed = 0;

            text = Regex.Replace(text,
                @"m_fontAsset: \{fileID: (-?\d+), guid: ([0-9a-fA-F]{32}), type: \d+\}",
                m =>
                {
                    string guid = m.Groups[2].Value;
                    if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        return m.Value;

                    changed++;
                    return $"m_fontAsset: {{fileID: {m.Groups[1].Value}, guid: {newGuid}, type: 3}}";
                });

            if (changed > 0)
            {
                File.WriteAllText(s.path, text, new UTF8Encoding(false));
                Debug.Log($"[HanziFontFixer] {Path.GetFileName(s.path)}: 修复 {changed} 处失效字体引用。");
                total += changed;
            }
        }

        if (total > 0) AssetDatabase.Refresh();
        return total;
    }
}
#endif
