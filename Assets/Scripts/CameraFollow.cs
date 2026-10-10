using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("目标")]
    public Transform target;

    [Header("平滑速度")]
    public float smoothSpeed = 5f;

    [Header("偏移")]
    public Vector3 offset = new Vector3(0, 0, -10);

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
        transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);
    }
}