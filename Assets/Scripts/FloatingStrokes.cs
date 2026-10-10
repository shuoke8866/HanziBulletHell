using UnityEngine;
using System.Collections.Generic;

public class FloatingStrokes : MonoBehaviour
{
    [Header("悬浮参数")]
    public float orbitRadius = 1.2f;     // 环绕半径
    public float orbitSpeed = 60f;       // 旋转速度（度/秒）
    public float shootInterval = 2.5f;   // 射击间隔

    [Header("子弹属性")]
    public float bulletDamage = 1f;      // 低伤害
    public float bulletSpeed = 8f;

    [Header("引用")]
    public GameObject floatingStrokePrefab;  // 悬浮笔画的小方块预制体
    public GameObject bulletPrefab;          // 玩家弹幕预制体

    private StrokeInventory inventory;
    private List<GameObject> orbiters = new List<GameObject>();
    private float nextShootTime = 0f;
    private float orbitAngle = 0f;

    void Start()
    {
        inventory = GetComponent<StrokeInventory>();

        // 自动获取子弹预制体
        if (bulletPrefab == null)
        {
            HanziSystem hs = GetComponent<HanziSystem>();
            if (hs != null) bulletPrefab = hs.bulletPrefab;
        }
    }

    void Update()
    {
        if (inventory == null) return;

        SyncOrbiters();
        RotateOrbiters();
        AutoShoot();
    }

    // 悬浮体数量 = 背包笔画数量
    void SyncOrbiters()
    {
        while (orbiters.Count < inventory.strokes.Count)
        {
            GameObject orb = Instantiate(floatingStrokePrefab, transform);
            orb.transform.localScale = new Vector3(0.35f, 0.35f, 1f);
            // 移除物理组件，避免影响玩家
            Collider2D col = orb.GetComponent<Collider2D>();
            if (col != null) Destroy(col);
            orbiters.Add(orb);
        }
        while (orbiters.Count > inventory.strokes.Count)
        {
            Destroy(orbiters[orbiters.Count - 1]);
            orbiters.RemoveAt(orbiters.Count - 1);
        }

        // 按笔画类型染色
        for (int i = 0; i < orbiters.Count; i++)
        {
            SpriteRenderer sr = orbiters[i].GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = GetStrokeColor(inventory.strokes[i]);
        }
    }

    void RotateOrbiters()
    {
        orbitAngle += orbitSpeed * Time.deltaTime;

        for (int i = 0; i < orbiters.Count; i++)
        {
            float angle = orbitAngle + i * (360f / Mathf.Max(1, orbiters.Count));
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = transform.position + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * orbitRadius;
            orbiters[i].transform.position = pos;
        }
    }

    void AutoShoot()
    {
        if (inventory.strokes.Count == 0) return;
        if (bulletPrefab == null) return;
        if (Time.time < nextShootTime) return;

        // 找最近的敌人
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length == 0) return;

        GameObject nearest = null;
        float bestDist = float.MaxValue;
        foreach (GameObject e in enemies)
        {
            float d = Vector2.Distance(transform.position, e.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = e;
            }
        }
        if (nearest == null) return;

        // 发射低伤害子弹
        Vector2 dir = (nearest.transform.position - transform.position).normalized;
        GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            b.SetDirection(dir);
            b.isPlayerBullet = true;
            b.Setup(bulletSpeed, (int)bulletDamage, 0.6f, false);
        }

        nextShootTime = Time.time + shootInterval;
    }

    // 按笔画类型返回颜色（与StrokePickup一致）
    Color GetStrokeColor(StrokeType s)
    {
        switch (s)
        {
            case StrokeType.Heng: return Color.green;
            case StrokeType.Shu: return Color.cyan;
            case StrokeType.Pie: return new Color(0.7f, 0.3f, 1f);
            case StrokeType.Na: return new Color(1f, 0.5f, 0f);
            case StrokeType.Dian: return Color.yellow;
            case StrokeType.Zhe: return new Color(1f, 0.2f, 0.8f);
            default: return Color.white;
        }
    }
}