using UnityEngine;

public class ElementSystem : MonoBehaviour
{
    [Header("当前元素")]
    public ElementType currentElement = ElementType.None;

    [Header("元素颜色")]
    public Color fireColor = new Color(1f, 0.35f, 0.2f);
    public Color waterColor = new Color(0.3f, 0.6f, 1f);
    public Color defaultColor = new Color(0.3f, 0.6f, 1f);

    // 应用元素
    public void SetElement(ElementType element)
    {
        currentElement = element;

        Color newColor = defaultColor;
        switch (element)
        {
            case ElementType.Fire:
                newColor = fireColor;
                GameLog.Info("🔥 获得火元素！弹幕将附带灼烧效果");
                break;
            case ElementType.Water:
                newColor = waterColor;
                GameLog.Info("💧 获得水元素！弹幕将附带减速效果");
                break;
            case ElementType.None:
                newColor = defaultColor;
                break;
        }

        // 改变主角笔画颜色
        HanziRenderer renderer = GetComponent<HanziRenderer>();
        if (renderer != null)
            renderer.SetStrokeColor(newColor);
    }

    // 给弹幕应用元素效果（在弹幕生成时由 HanziSystem 调用）
    public void ApplyElementToBullet(GameObject bullet)
    {
        Bullet b = bullet.GetComponent<Bullet>();
        if (b == null) return;

        SpriteRenderer sr = bullet.GetComponent<SpriteRenderer>();

        switch (currentElement)
        {
            case ElementType.Fire:
                b.hasFireBuff = true;
                b.fireDPS = 1.5f;
                b.fireDuration = 3f;
                if (sr != null) sr.color = new Color(1f, 0.3f, 0.1f);
                break;

            case ElementType.Water:
                b.hasSlowBuff = true;
                b.slowMultiplier = 0.5f;
                b.slowDuration = 3f;
                if (sr != null) sr.color = new Color(0.3f, 0.6f, 1f);
                break;
        }
    }
}