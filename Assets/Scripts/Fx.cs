using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打击感特效总控：屏幕震动 + 粒子爆发 + 合成演出。
///
/// 设计取舍（为什么改颜色而不是做闪白）：
///   最初想让受击目标"闪白"，但敌人在蓄力/近战时会用 SetStrokeColor 改字形颜色
///   （见 Enemy.SetTint），两套逻辑会互相覆盖 —— 闪白结束恢复原色时会把
///   蓄力提示色一起抹掉。所以改成在**受击点生成白色粒子**：完全不碰目标状态，
///   观感同样明确，且没有耦合。
///
/// 自举方式与 Sfx 一致：[RuntimeInitializeOnLoadMethod] 自动创建，不需要挂场景。
/// </summary>
public class Fx : MonoBehaviour
{
    public static Fx Instance { get; private set; }

    // ---------------- 屏幕震动参数 ----------------
    [Header("屏幕震动")]
    public float maxOffset = 0.55f;
    public float maxAngle = 2.2f;
    public float decayPerSecond = 1.6f;
    public float frequency = 22f;

    const float KickDuration = 0.12f;

    float trauma;
    float seed;
    Vector2 kickDir;
    float kickTime;
    float kickAmount;

    // ---------------- 对象池 ----------------
    // 子弹地狱里特效调用极频繁，每次 Instantiate/Destroy 会产生 GC 峰值，
    // 在 WebGL 上表现为周期性卡顿。这里用固定大小的环状池复用粒子。
    const int ParticleCapacity = 220;
    readonly List<ParticleSystem> particlePool = new List<ParticleSystem>();
    int nextParticle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("~Fx");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<Fx>();
        Instance.BuildParticlePool();
        Instance.seed = Random.value * 100f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ================= 屏幕震动 =================

    /// <summary>
    /// 叠加震动。amount 参考值：
    /// 0.05 射击 / 0.15 命中 / 0.28 敌人死亡 / 0.40 玩家受伤 / 0.75 BOSS 出场
    /// </summary>
    public static void Shake(float amount)
    {
        if (Instance == null) Bootstrap();
        if (Instance == null) return;
        Instance.trauma = Mathf.Clamp01(Instance.trauma + amount);
    }

    /// <summary>带方向的震动冲击</summary>
    public static void Shake(float amount, Vector2 direction)
    {
        if (Instance == null) Bootstrap();
        if (Instance == null) return;

        Instance.trauma = Mathf.Clamp01(Instance.trauma + amount);

        if (direction.sqrMagnitude > 0.0001f)
        {
            Instance.kickDir = direction.normalized;
            Instance.kickTime = KickDuration;
            Instance.kickAmount = amount * 0.5f;
        }
    }

    /// <summary>
    /// 计算本帧的震动偏移，供 CameraFollow 在算完目标位置后叠加。
    ///
    /// 为什么由 CameraFollow 主动来取、而不是本类自己去写相机位置：
    /// 若本类也在 LateUpdate 里写 transform，就会和 CameraFollow 抢同一个值，
    /// 谁后执行取决于未定义的脚本顺序 —— 结果是震动幅度被 Lerp 稀释，表现成"抖不动"。
    /// 改成"本类只提供偏移、CameraFollow 负责应用"后，不存在顺序依赖。
    /// </summary>
    public static void ConsumeShake(out Vector3 offset, out float angle)
    {
        offset = Vector3.zero;
        angle = 0f;

        if (Instance == null) return;

        var self = Instance;
        bool shaking = self.trauma > 0f || self.kickTime > 0f;
        if (!shaking) return;

        float shake = self.trauma * self.trauma;   // 平方映射：小震动收敛快，大震动才明显

        float t = Time.unscaledTime * self.frequency;
        float nx = Mathf.PerlinNoise(self.seed, t) * 2f - 1f;
        float ny = Mathf.PerlinNoise(self.seed + 37f, t) * 2f - 1f;
        float na = Mathf.PerlinNoise(self.seed + 71f, t) * 2f - 1f;

        offset = new Vector3(nx, ny, 0f) * (self.maxOffset * shake);
        angle = na * self.maxAngle * shake;

        if (self.kickTime > 0f)
        {
            float k = self.kickTime / KickDuration;
            offset += (Vector3)(self.kickDir * (self.kickAmount * k));
            self.kickTime -= Time.unscaledDeltaTime;
        }

        self.trauma = Mathf.Max(0f, self.trauma - self.decayPerSecond * Time.unscaledDeltaTime);

        // 震动结束时把偏移归零，避免留下零头
        if (self.trauma <= 0f && self.kickTime <= 0f)
        {
            offset = Vector3.zero;
            angle = 0f;
        }
    }

    // ================= 粒子 =================

    void BuildParticlePool()
    {
        for (int i = 0; i < ParticleCapacity; i++)
        {
            var go = new GameObject($"FxParticle{i}");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(ps);
            ps.Stop();
            particlePool.Add(ps);
        }
    }

    static void ConfigureParticleSystem(ParticleSystem ps)
    {
        // 关掉不需要的模块，减少每帧开销
        var emission = ps.emission;
        emission.enabled = false;          // 手动 Emit，不自动发射

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        renderer.sortingOrder = 60;        // 盖在游戏物体之上

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;  // 世界坐标：粒子不会跟着父物体跑
        main.maxParticles = 64;
        main.startLifetime = 0.35f;
        main.startSpeed = 0f;              // 速度在 Emit 时逐粒子给定
        main.startSize = 0.2f;
        main.gravityModifier = 0f;

        // 颜色随生命周期淡出
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 尺寸随生命衰减
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
    }

    ParticleSystem TakeParticle()
    {
        if (particlePool.Count == 0) return null;

        // 环状复用：最老的粒子会被新的覆盖（特效本来就是短命的）
        var ps = particlePool[nextParticle];
        nextParticle = (nextParticle + 1) % particlePool.Count;
        return ps;
    }

    static void EmitBurst(Vector3 pos, int count, Sprite sprite, Color color,
                          float speedMin, float speedMax, float sizeMin, float sizeMax,
                          float lifetime)
    {
        if (Instance == null) Bootstrap();
        if (Instance == null) return;

        ParticleSystem ps = Instance.TakeParticle();
        if (ps == null) return;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material.mainTexture = sprite.texture;
        renderer.sortingOrder = 60;

        ps.transform.position = pos;

        var main = ps.main;
        main.startLifetime = lifetime;
        main.startColor = color;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ps.Clear();
        ps.Emit(count);
    }

    // ================= 具体特效 =================

    /// <summary>敌人被击中：白色小火花</summary>
    public static void HitSpark(Vector3 pos, Vector2 hitDirection, Color tint)
    {
        // 火花朝"被击退"的方向喷，方向感更明确
        EmitBurst(pos, 5, ProcTex.Dot, Color.Lerp(Color.white, tint, 0.45f),
            1.4f, 3.6f, 0.07f, 0.16f, 0.22f);
    }

    /// <summary>敌人死亡：碎屑爆开</summary>
    public static void DeathBurst(Vector3 pos, Color tint)
    {
        EmitBurst(pos, 12, ProcTex.Square, Color.Lerp(Color.white, tint, 0.6f),
            2.0f, 5.5f, 0.08f, 0.2f, 0.45f);
        EmitBurst(pos, 6, ProcTex.Dot, new Color(1f, 1f, 1f, 0.9f),
            0.5f, 2.0f, 0.14f, 0.34f, 0.3f);
    }

    /// <summary>玩家受伤：红色扩散</summary>
    public static void PlayerHit(Vector3 pos)
    {
        EmitBurst(pos, 10, ProcTex.Dot, new Color(1f, 0.35f, 0.35f, 1f),
            2.5f, 6f, 0.1f, 0.26f, 0.4f);
    }

    /// <summary>
    /// 汉字合成演出：白色光点向内收拢 + 一圈辉光。
    /// inward=true 时粒子速度为负（用极小速度 + 大寿命模拟汇聚感）。
    /// </summary>
    public static void SynthesisBurst(Vector3 pos, Color tint)
    {
        // 外圈辉光
        EmitBurst(pos, 14, ProcTex.Dot, Color.Lerp(Color.white, tint, 0.5f),
            3.0f, 7.0f, 0.1f, 0.3f, 0.55f);
        // 内层亮点
        EmitBurst(pos, 18, ProcTex.Streak, Color.white,
            1.0f, 4.0f, 0.06f, 0.18f, 0.7f);
    }

    /// <summary>拾取笔画：小幅上飘</summary>
    public static void PickupPop(Vector3 pos, Color tint)
    {
        EmitBurst(pos, 7, ProcTex.Dot, tint,
            0.8f, 2.4f, 0.08f, 0.2f, 0.35f);
    }

    /// <summary>清空房间：金色扩散环</summary>
    public static void RoomClearBurst(Vector3 pos)
    {
        EmitBurst(pos, 20, ProcTex.Dot, new Color(1f, 0.9f, 0.45f, 1f),
            3.5f, 8f, 0.12f, 0.34f, 0.65f);
    }
}
