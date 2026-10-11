#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音频导入设置优化器。
///
/// 背景音乐占构建体积 5.7 MB（Vorbis 已是质量 1，但仍是立体声 48kHz 四分钟）。
/// 背景音乐转为单声道 + 降采样能显著减小体积，而对"背景音乐"这种用途
/// 几乎听不出区别（它不承载定位信息，也不需要高频细节）。
///
/// 用法：Unity 菜单栏 → Tools → 优化音频导入设置
/// </summary>
public static class AudioOptimizer
{
    const string MusicPath = "Assets/Audio/Dentaneosuchus Hunt.mp3";

    [MenuItem("Tools/优化音频导入设置")]
    public static void Optimize()
    {
        var importer = AssetImporter.GetAtPath(MusicPath) as AudioImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[AudioOptimizer] 找不到音频 {MusicPath}，跳过。");
            return;
        }

        var s = importer.defaultSampleSettings;

        // 单声道：背景音乐不需要立体声，Vorbis 下大约省一半
        importer.forceToMono = true;

        // 压缩在内存里：WebGL 不支持 Streaming，CompressedInMemory 是最省构建体积的选项
        s.loadType = AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.1f;                                   // 已是最低档附近

        // 降采样到 22050Hz：四分钟的音乐，高频细节对 BGM 意义不大
        s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        s.sampleRateOverride = 22050;

        importer.defaultSampleSettings = s;

        // WebGL 平台单独覆盖一份（Unity 会优先用平台设置）
        importer.SetOverrideSampleSettings("WebGL", s);

        importer.SaveAndReimport();
        Debug.Log("[AudioOptimizer] 已优化背景音乐导入设置：单声道 / 22050Hz / " +
                  "CompressedInMemory / Vorbis q0.1");
    }
}
#endif
