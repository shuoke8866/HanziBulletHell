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
    /// <summary>
    /// 要构建的场景 —— **直接读 Unity 的官方配置，不在这里硬编码。**
    ///
    /// 踩过的坑：这里原来手写 `{ Level1, Level2, Level3 }`。
    /// 后来加了新手教程层 Level0、也把它写进了 Build Settings，
    /// 但这个手写列表没同步 —— 结果构建出来的包里**没有教程层**，
    /// 玩家点开直接进第一层，完全看不到教程。
    ///
    /// 两处都记着"要构建哪些场景"就是两个数据源，迟早对不上。
    /// 现在只认 <see cref="EditorBuildSettings.scenes"/> 这一处
    /// （也就是 File → Build Settings 里那个列表）。
    /// 用 Tools/更新构建场景列表 维护它即可。
    /// </summary>
    static string[] Scenes
    {
        get
        {
            var list = new System.Collections.Generic.List<string>();

            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s == null || !s.enabled) continue;
                if (string.IsNullOrEmpty(s.path)) continue;
                if (!s.path.EndsWith(".unity")) continue;
                list.Add(s.path);
            }

            // 兜底：Build Settings 空着的时候别构建出个空包
            if (list.Count == 0)
            {
                Debug.LogWarning("[WebGLBuilder] Build Settings 里没有启用的场景，" +
                                 "回退到默认三个关卡");
                list.Add("Assets/Scenes/Level1.unity");
                list.Add("Assets/Scenes/Level2.unity");
                list.Add("Assets/Scenes/Level3.unity");
            }

            return list.ToArray();
        }
    }

    /// <summary>构建输出目录（相对项目根目录11）</summary>
    const string OutputFolder = "docs";

    [MenuItem("Build/构建 WebGL 到 docs/", false, 1000)]
    public static void BuildFromMenu()
    {
        if (BuildInternal()) EditorUtility.DisplayDialog("构建完成",
            $"WebGL 构建成功！\n\n产物位置：{OutputFolder}/\n\n" +
            "接下来：把 docs/ 目录提交并推送到 GitHub，\n" +
            "然后在仓库 Settings → Pages 选择 main 分支的 /docs 目录。",
            "好的");
    }

    /// <summary>
    /// 在原生「File → Build Settings」窗口底部插一个按钮。
    /// 中文版 Unity 的主菜单会被本地化，自定义顶级菜单不易找到，
    /// 所以这里额外提供一个一定看得见的入口。
    /// </summary>
    public class BuildSettingsButton
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuildClicked);
        }

        static void OnBuildClicked(BuildPlayerOptions options)
        {
            // 如果用户选的目标就是 WebGL，走我们的构建流程
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                if (BuildInternal()) EditorUtility.DisplayDialog("构建完成",
                    $"WebGL 构建成功！\n\n产物位置：{OutputFolder}/", "好的");
                return;
            }

            // 其他平台交给 Unity 默认流程
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }
    }

    /// <summary>供命令行调用（-executeMethod WebGLBuilder.Build）</summary>
    public static void Build()
    {
        bool ok = false;
        try
        {
            ok = BuildInternal();
        }
        catch (Exception e)
        {
            // 未捕获异常会让 Unity 以非 0 退出，但调用方拿不到明确原因。
            // 这里显式记下来，保证失败一定会被看见。
            Debug.LogError($"[WebGLBuilder] 构建抛出异常，失败：\n{e}");
            ok = false;
        }

        // 批处理模式下需要显式退出码，CI/脚本才能判断成败
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildInternal()
    {
        if (!SanityCheck()) return false;

        // 字体图集必须在构建前确保是最新的。
        // 不能只依赖 HanziFontFixer 的 [InitializeOnLoad] + delayCall：
        // 在 -batchmode -quit 下构建结束得比 delayCall 早，字体不会被重烘焙，
        // 结果就是图集参数改了却完全不生效（构建悄悄用了旧图集）。
        HanziFontFixer.EnsureBaked(false);

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
            options = BuildOptions.DetailedBuildReport
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

    /// <summary>
    /// 真加载每个场景，确认它们不是"空场景"。
    ///
    /// 曾经踩过的坑：场景文件头部被脚本写坏了一行，YAML 解析失败，
    /// Unity 把场景当成空的打包进去。构建成功、体积正常，一层都看不出来，
    /// 直到线上"传送到第二关画面空白"才发现。所以这里必须实测。
    /// </summary>
    static bool ValidateScenesLoad()
    {
        string originalScene = UnityEngine.SceneManagement.SceneManager
            .GetActiveScene().path;

        bool allOk = true;

        foreach (string s in Scenes)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                s, UnityEditor.SceneManagement.OpenSceneMode.Single);

            if (scene.rootCount == 0)
            {
                Debug.LogError(
                    $"[WebGLBuilder] 场景「{s}」加载后没有任何根对象 —— 它很可能已损坏，\n" +
                    "  构建出来的会是一个空关卡（进去以后画面空白）。\n" +
                    "  常见原因：场景 YAML 头部被写入了非法内容。\n" +
                    "  请用 Unity 打开该场景确认能否正常编辑，必要时从版本库恢复。");
                allOk = false;
                continue;
            }

            // 关底场景必须能刷出 BOSS，否则流程会断在传送门之前
            var rooms = UnityEngine.Object.FindObjectsOfType<Room>();
            int bossRooms = 0;
            foreach (var r in rooms)
                if (r.roomType == RoomType.Boss) bossRooms++;

            bool hasPlayer = PlayerRef.GameObject != null;
            bool hasGameManager = UnityEngine.Object.FindObjectOfType<GameManager>() != null;

            Debug.Log($"[WebGLBuilder] 场景校验 {s}: 根对象 {scene.rootCount}、" +
                      $"房间 {rooms.Length}（Boss 房 {bossRooms}）、" +
                      $"玩家 {(hasPlayer ? "有" : "无")}、GameManager {(hasGameManager ? "有" : "无")}");

            if (!hasGameManager)
            {
                Debug.LogError($"[WebGLBuilder] 场景「{s}」里没有 GameManager，" +
                               "传送门与层数推进都无法工作。");
                allOk = false;
            }

            if (!hasPlayer)
            {
                Debug.LogError($"[WebGLBuilder] 场景「{s}」里没有 Player，无法游玩。");
                allOk = false;
            }
        }

        // 还原最初打开的场景，避免影响后续构建流程
        if (!string.IsNullOrEmpty(originalScene) && File.Exists(originalScene))
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                originalScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        return allOk;
    }

    /// <summary>构建前自检：缺模块、场景损坏、缺 GameManager/Player 都提前报错并阻断构建</summary>
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

        // 3) 场景能否真正加载 —— 这一项必须阻断构建。
        //
        // 背景：曾经因为一次脚本 bug，Level2.unity 的文件头部被塞进了一行无关文本，
        // YAML 解析失败，场景被打包成"空场景"（0 个根对象）。构建本身完全成功、
        // 体积也正常，所以问题一直到线上"传送到第二关后画面空白"才暴露。
        // 静态检查文件存在是远远不够的，这里真的把场景加载一遍并统计根对象。
        if (!ValidateScenesLoad()) return false;

        // 4) 场景是否在 Build Settings 里（不阻断，只提醒）
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
        // --- 关键：关闭压缩 ---
        //
        // 原因：Unity 的 WebGL 加载器是用 <script> 标签直接注入 framework 文件的，
        // 而浏览器不会自动解压 <script> 加载的 gzip/brotli 内容 —— 它依赖服务器
        // 发送 Content-Encoding: br / gzip 响应头。
        // GitHub Pages 对静态文件不会发送这类响应头，所以用 Brotli 或 GZip 都会报：
        //   "Unable to parse Build/xxx.framework.js.gz!"
        //
        // 关闭压缩后产出普通的 .js/.wasm/.data 文件，任何静态服务器都能直接跑。
        // 代价是体积变大（约 30-35 MB），但换来的是零服务器配置要求。
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        // --- 网页端友好设置 ---
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;

        // --- 减小 wasm 体积（首屏下载量的大头）---

        // 托管代码剥离：High 会砍掉未使用的托管代码。
        // 注意：若项目里有用反射/字符串动态访问的类型，可能被误剥离，
        // 所以 link.xml 里对需要保留的程序集做了声明（见 Assets/link.xml）。
        PlayerSettings.SetManagedStrippingLevel(
            UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

        // 剥离未使用的引擎代码（引擎模块级裁剪）
        PlayerSettings.stripEngineCode = true;

        // IL2CPP 用 Master 配置编译，产出更小更快（发布构建本来就该这样）
        PlayerSettings.SetIl2CppCompilerConfiguration(
            UnityEditor.Build.NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Master);

        // --- 关掉 Unity 启动画面 ---
        PlayerSettings.SplashScreen.show = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[WebGLBuilder] 已应用设置：不压缩（兼容 GitHub Pages）/ 1280x720 / 关闭启动画面");
    }
}
#endif
