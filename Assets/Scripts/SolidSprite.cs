using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 程序生成的纯色 1×1 贴图工具 —— 给血条用。
///
/// 项目没有美术资源，血条就是"两个纯色矩形叠在一起"，
/// 所以只要一张 1×1 白贴图 + SpriteRenderer/Image 的 color 就能画出来，
/// 不需要任何图片文件。
///
/// 缓存起来复用：血条要频繁创建（每只小怪一个），
/// 每次都 new Texture2D 会产生 GC 峰值。
/// </summary>
public static class SolidSprite
{
    static Sprite white;

    /// <summary>1×1 白色贴图（用 color 上色）</summary>
    public static Sprite White
    {
        get
        {
            if (white != null) return white;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();

            white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            white.name = "SolidWhite";
            return white;
        }
    }
}
