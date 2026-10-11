using UnityEngine;

/// <summary>
/// 相机跟随玩家，并叠加 Fx 提供的屏幕震动偏移。
///
/// 震动为什么在这里应用、而不是让 Fx 自己写 transform：
/// 两个脚本若都写相机位置，谁生效取决于未定义的脚本执行顺序 ——
/// 若 Fx 先跑，CameraFollow 的 Lerp 会立刻把震动覆盖掉，表现成"抖不动"。
/// 改成"Fx 只提供偏移、这里负责应用"后，就没有顺序依赖了。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("目标")]
    public Transform target;

    [Header("平滑速度")]
    public float smoothSpeed = 5f;

    [Header("偏移")]
    public Vector3 offset = new Vector3(0, 0, -10);

    /// <summary>
    /// 自己维护的"平滑跟随位置"。
    /// 不能拿 transform.position 当 Lerp 输入 —— 那个值包含震动偏移，
    /// 会把抖动反馈进跟随计算，表现为跟随变抖、震动被稀释。
    /// </summary>
    Vector3 smoothPos;
    bool smoothInitialized;

    void LateUpdate()
    {
        if (target == null)
        {
            // 自动找玩家
            GameObject player = PlayerRef.GameObject;
            if (player != null) target = player.transform;
            return;
        }

        Vector3 targetPos = target.position + offset;

        if (!smoothInitialized)
        {
            smoothPos = targetPos;
            smoothInitialized = true;
        }
        else
        {
            smoothPos = Vector3.Lerp(smoothPos, targetPos, smoothSpeed * Time.deltaTime);
        }

        // 在"跟随结果"之上叠加震动偏移
        Vector3 shakeOffset;
        float shakeAngle;
        Fx.ConsumeShake(out shakeOffset, out shakeAngle);

        transform.position = smoothPos + shakeOffset;

        if (shakeAngle != 0f)
            transform.rotation = Quaternion.Euler(0f, 0f, shakeAngle);
        else
            transform.rotation = Quaternion.identity;
    }
}
