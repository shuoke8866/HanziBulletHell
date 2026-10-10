#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// CI 用的 WebGL 构建脚本。
///
/// 为什么需要自定义 buildMethod：
///   1. GitHub Pages 无法为 .br 文件发送 Content-Encoding: br 响应头，
///      而 Unity 2022 的 WebGL 默认用 Brotli，会导致页面加载失败。
///      这里强制改成 GZip（浏览器原生支持，Pages 也能正确服务）。
///   2. 顺便固定构建选项（压缩、IL2CPP、关闭异常全开），保证 CI 结果稳定。
/// </summary>
public static class WebGLBuilder
{
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    public static void Build()
    {
        ApplySettings();

        string outputDir = "build/WebGL/HanziBulletHell";
        System.IO.Directory.CreateDirectory(outputDir);

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        Debug.Log($"[WebGLBuilder] 开始构建 → {outputDir}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WebGLBuilder] 构建成功，大小 {summary.totalSize / 1048576.0:0.0} MB，" +
                      $"耗时 {summary.totalTime.TotalSeconds:0} 秒");
        }
        else
        {
            Debug.LogError($"[WebGLBuilder] 构建失败：{summary.result}，" +
                           $"错误 {summary.totalErrors} 个");
            EditorApplication.Exit(1);
        }
    }

    static void ApplySettings()
    {
        // --- 关键：改成 GZip，否则 GitHub Pages 加载不了 ---
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

        // --- 关掉 Unity 启动画面（作品集不需要） ---
        PlayerSettings.SplashScreen.show = false;

        // 提交设置改动，确保写入构建
        AssetDatabase.SaveAssets();
        Debug.Log("[WebGLBuilder] 已应用 WebGL 设置：GZip 压缩 / 1280x720 / 关闭启动画面");
    }
}
#endif
