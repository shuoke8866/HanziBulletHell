#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 本地 WebGL 构建脚本。
///
/// 输出到仓库的 docs/ 目录，GitHub Pages 直接从 main 分支的 /docs 发布，
/// 所以不需要额外的 gh-pages 分支，也不需要 GitHub Actions。
///
/// 用法（两种都行）：
///   1. Unity 菜单栏 → Build → 构建 WebGL 到 docs/
///   2. 命令行：
///      Unity.exe -quit -batchmode -projectPath . -executeMethod WebGLBuilder.Build
///
/// 注意：需要先在 Unity Hub 给这个编辑器版本安装「WebGL Build Support」模块。
/// </summary>
public static class WebGLBuilder
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    /// <summary>构建输出目录（相对项目根目录）</summary>
    const string OutputFolder = "docs";

    [MenuItem("Build/构建 WebGL 到 docs/")]
    public static void BuildFromMenu()
    {
        if (BuildInternal()) EditorUtility.DisplayDialog("构建完成",
            $"WebGL 构建成功！\n\n产物位置：{OutputFolder}/\n\n" +
            "接下来：把 docs/ 目录提交并推送到 GitHub，\n" +
            "然后在仓库 Settings → Pages 选择 main 分支的 /docs 目录。",
            "好的");
    }

    /// <summary>供命令行调用（-executeMethod WebGLBuilder.Build）</summary>
    public static void Build()
    {
        bool ok = BuildInternal();

        // 批处理模式下需要显式退出码，CI/脚本才能判断成败
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildInternal()
    {
        if (!SanityCheck()) return false;

        ApplySettings();

        // 用绝对路径，避免不同工作目录导致输出错位
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDir = Path.Combine(projectRoot, OutputFolder);

        Directory.CreateDirectory(outputDir);

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        Debug.Log($"[WebGLBuilder] 项目根目录: {projectRoot}");
        Debug.Log($"[WebGLBuilder] 输出目录  : {outputDir}");
        Debug.Log($"[WebGLBuilder] 构建场景  : {string.Join(", ", Scenes)}");

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[WebGLBuilder] 构建失败：{summary.result}，" +
                           $"错误 {summary.totalErrors} 个。请查看 Console 上方第一条红色报错。");
            return false;
        }

        Debug.Log($"[WebGLBuilder] 构建成功，大小 {summary.totalSize / 1048576.0:0.0} MB，" +
                  $"耗时 {summary.totalTime.TotalSeconds:0} 秒");

        // 打印产物清单，方便确认 index.html 存在
        foreach (string f in Directory.GetFiles(outputDir))
        {
            if (f.EndsWith(".meta")) continue;
            Debug.Log($"[WebGLBuilder] 产物: {Path.GetFileName(f)} " +
                      $"({new FileInfo(f).Length / 1024} KB)");
        }

        Debug.Log($"[WebGLBuilder] 现在可以把 {OutputFolder}/ 提交并推送，" +
                  "再到仓库 Settings → Pages 选择 main 分支的 /docs 目录发布。");
        return true;
    }

    /// <summary>构建前自检：缺模块、缺场景都提前给出可读的报错</summary>
    static bool SanityCheck()
    {
        // 1) WebGL 模块是否安装
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError(
                "[WebGLBuilder] 当前 Unity 没有安装 WebGL 构建支持。\n" +
                "解决：打开 Unity Hub → Installs → 找到 " + Application.unityVersion +
                " → 右侧齿轮/三个点 → Add modules → 勾选「WebGL Build Support」并安装。");
            return false;
        }

        // 2) 场景文件是否存在
        var missing = new List<string>();
        foreach (string s in Scenes)
            if (!File.Exists(s)) missing.Add(s);

        if (missing.Count > 0)
        {
            Debug.LogError("[WebGLBuilder] 以下场景文件不存在，无法构建：\n  " +
                           string.Join("\n  ", missing));
            return false;
        }

        // 3) 场景是否在 Build Settings 里（不阻断，只提醒）
        foreach (string s in Scenes)
        {
            bool inBuild = false;
            foreach (EditorBuildSettingsScene bs in EditorBuildSettings.scenes)
                if (bs.path == s && bs.enabled) { inBuild = true; break; }

            if (!inBuild)
                Debug.LogWarning($"[WebGLBuilder] 场景 {s} 不在 Build Settings 的启用列表里" +
                                 "（本脚本会显式传入场景列表，所以仍会构建，但建议补上）。");
        }

        return true;
    }

    static void ApplySettings()
    {
        // --- 关键：GZip 压缩。GitHub Pages 无法为 .br 发送 Content-Encoding，
        //     用 Brotli 会导致页面加载失败 ---
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = false;

        // --- 网页端友好设置 ---
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.SetManagedStrippingLevel(
            UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);

        // --- 关掉 Unity 启动画面 ---
        PlayerSettings.SplashScreen.show = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[WebGLBuilder] 已应用设置：GZip 压缩 / 1280x720 / 关闭启动画面");
    }
}
#endif
