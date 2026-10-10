using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化音效合成器。
///
/// 为什么不用音频文件：
///   - 素材来源不明会有版权风险（这个项目要公开给 HR 看）
///   - 每个音效文件在 WebGL 首屏都要下载，几十 KB × 十几个音效是实打实的体积
///   - 波形是代码生成的，改参数就能调手感，不需要重新找素材
///
/// 全部音效在启动时一次性合成好存成 AudioClip，播放时零分配。
/// 音效总量很小（总时长不到 2 秒，单声道 22050Hz，合计约几十 KB 内存）。
/// </summary>
public class Sfx : MonoBehaviour
{
    public enum Id
    {
        Shoot,      // 玩家射击
        Hit,        // 子弹命中敌人
        EnemyDie,   // 敌人死亡
        PlayerHurt, // 玩家受伤
        Pickup,     // 拾取笔画
        Combine,    // 合成汉字
        RoomClear,  // 清空房间
        Boon,       // 获得词条
        Menu,       // 界面操作
        Error       // 操作无效
    }

    const int SampleRate = 22050;   // 音效够用，省一半内存
    const int VoiceCount = 10;      // 同时可播放的音效数

    static Sfx instance;
    static readonly Dictionary<Id, AudioClip> clips = new Dictionary<Id, AudioClip>();

    readonly List<AudioSource> voices = new List<AudioSource>();
    int nextVoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;

        var go = new GameObject("~Sfx");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Sfx>();
        instance.BuildVoices();
        BuildClips();
    }

    void BuildVoices()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;      // 2D
            src.volume = 0.5f;
            voices.Add(src);
        }
    }

    // ================= 合成 =================

    static void BuildClips()
    {
        if (clips.Count > 0) return;

        // 参数：基频、终止频率、时长、波形、音量、是否加噪声
        Add(Id.Shoot,     900f,  380f, 0.075f, Wave.Square, 0.30f, 0f);
        Add(Id.Hit,      1500f,  850f, 0.050f, Wave.Square, 0.26f, 0.35f);
        Add(Id.EnemyDie,  380f,  110f, 0.220f, Wave.Saw,    0.32f, 0.25f);
        Add(Id.PlayerHurt,420f,  110f, 0.300f, Wave.Square, 0.38f, 0.45f);
        Add(Id.Pickup,    620f, 1180f, 0.090f, Wave.Sine,   0.30f, 0f);
        Add(Id.Combine,   480f, 1500f, 0.260f, Wave.Sine,   0.34f, 0f);
        Add(Id.RoomClear, 440f, 1320f, 0.360f, Wave.Sine,   0.32f, 0f);
        Add(Id.Boon,      700f, 1400f, 0.300f, Wave.Sine,   0.32f, 0f);
        Add(Id.Menu,      950f,  950f, 0.035f, Wave.Square, 0.20f, 0f);
        Add(Id.Error,     220f,  160f, 0.130f, Wave.Square, 0.26f, 0.2f);
    }

    enum Wave { Sine, Square, Saw }

    static void Add(Id id, float f0, float f1, float dur, Wave wave, float amp, float noise)
    {
        clips[id] = Synthesize(f0, f1, dur, wave, amp, noise);
    }

    static AudioClip Synthesize(float f0, float f1, float dur, Wave wave, float amp, float noise)
    {
        int n = Mathf.Max(1, (int)(SampleRate * dur));
        var data = new float[n];

        // 相位要积分累计，不能直接用 f*t（频率在变，否则会有爆音）
        double phase = 0;
        var rnd = new System.Random(12345);

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2.0 * Mathf.PI * f / SampleRate;

            float s;
            switch (wave)
            {
                case Wave.Square: s = Mathf.Sin((float)phase) >= 0f ? 1f : -1f; break;
                case Wave.Saw:    s = 2f * (float)(phase / (2.0 * Mathf.PI) % 1.0) - 1f; break;
                default:          s = Mathf.Sin((float)phase); break;
            }

            // 噪声层：让打击感更"实"
            if (noise > 0f)
                s = Mathf.Lerp(s, (float)(rnd.NextDouble() * 2.0 - 1.0), noise);

            // 包络：极短淡入（消爆音）+ 指数衰减
            float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));
            float decay = Mathf.Pow(1f - t, 2.2f);
            data[i] = s * amp * attack * decay;
        }

        var clip = AudioClip.Create($"sfx_{f0:0}_{dur:0.###}", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ================= 播放 =================

    public static void Play(Id id, float volumeScale = 1f)
    {
        if (instance == null)
        {
            Bootstrap();
            if (instance == null) return;
        }

        AudioClip clip;
        if (!clips.TryGetValue(id, out clip) || clip == null) return;

        AudioSource src = instance.voices[instance.nextVoice];
        instance.nextVoice = (instance.nextVoice + 1) % instance.voices.Count;

        src.pitch = 1f;
        src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    /// <summary>带随机音高的播放，避免连续同类音效听起来像机关枪</summary>
    public static void PlayVaried(Id id, float volumeScale = 1f, float pitchJitter = 0.08f)
    {
        if (instance == null)
        {
            Bootstrap();
            if (instance == null) return;
        }

        AudioClip clip;
        if (!clips.TryGetValue(id, out clip) || clip == null) return;

        AudioSource src = instance.voices[instance.nextVoice];
        instance.nextVoice = (instance.nextVoice + 1) % instance.voices.Count;

        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    /// <summary>全局音量（可在设置里接）</summary>
    public static void SetVolume(float v)
    {
        if (instance == null) return;
        foreach (var s in instance.voices) s.volume = Mathf.Clamp01(v);
    }
}
