using UnityEngine;

/// <summary>
/// 让对象始终正对相机（世界空间文字用）。
///
/// 主要用于传送门上方的提示文字：传送门的精灵在自转，
/// 提示文字挂在它下面却不能被带着转，否则字会歪。
/// </summary>
public class FaceCamera : MonoBehaviour
{
    [Tooltip("是否抵消父级的旋转（父级自转时文字要保持水平就设为 true）")]
    public bool cancelParentRotation = true;

    [Tooltip("是否跟随父级缩放（父级有非 1 缩放时避免文字被拉伸）")]
    public bool cancelParentScale = true;

    Camera cam;
    Quaternion parentWorldRotationAtStart;
    Vector3 parentScaleAtStart;

    void Start()
    {
        cam = Camera.main;

        if (cancelParentRotation && transform.parent != null)
            parentWorldRotationAtStart = transform.parent.rotation;

        if (cancelParentScale && transform.parent != null)
            parentScaleAtStart = transform.parent.lossyScale;
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // 每帧把自身旋转设成"无旋转"，就自然正对正交相机
        if (cancelParentRotation)
            transform.rotation = Quaternion.identity;

        if (cancelParentScale && transform.parent != null)
        {
            Vector3 p = transform.parent.lossyScale;
            transform.localScale = new Vector3(
                p.x != 0f ? parentScaleAtStart.x / p.x : 1f,
                p.y != 0f ? parentScaleAtStart.y / p.y : 1f,
                1f);
        }
    }
}
