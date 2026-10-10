using UnityEngine;

public class Portal : MonoBehaviour
{
    [Header("视觉")]
    public SpriteRenderer portalSprite;
    public float rotateSpeed = 30f;

    void Start()
    {
        if (portalSprite == null)
            portalSprite = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // 旋转动画
        transform.Rotate(0, 0, rotateSpeed * Time.deltaTime);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            GameLog.Info("进入传送门！进入下一层...");
            GoToNextFloor();
        }
    }

    void GoToNextFloor()
    {
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.GoToNextFloor();
        }
        else
        {
            Debug.LogWarning("未找到 GameManager，无法传送到下一层。");
        }
    }
}