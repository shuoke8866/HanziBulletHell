// 新增 15 个汉字：字形（HanziRenderer）+ 弹幕定义（HanziSystem）。
//
// **不改动任何已有字**，只在末尾追加。
//
// 选字原则：
//   1. 六笔画 + 「点万能」（点=提 / 竖+点=竖钩 / 横+点=横折钩）能画出来
//   2. 字形与已有字**不重复、辨识度高**
//   3. 覆盖不同画数（3~7 画），让成长曲线有层次
//
// 选中的 15 个：
//   3画  小  才
//   4画  少  水  火
//   5画  求  永  乐
//   6画  光  先  尖  尘
//   7画  我  利  見

using System;
using System.IO;
using System.Text;

static class Add15Hanzi
{
    static void Main()
    {
        Glyphs();
        Bullets();
    }

    // ============================================================
    //  1) 字形：往 HanziRenderer.BuildDefinitions 里追加
    // ============================================================
    static void Glyphs()
    {
        string p = @"F:\HanziBulletHell\Assets\Scripts\HanziRenderer.cs";
        string t = File.ReadAllText(p, Encoding.UTF8);

        // 已经加过就不重复
        if (t.Contains("// ===== 新增 15 字 ====="))
        {
            Console.WriteLine("  字形：已加过，跳过");
            return;
        }

        // 找最后一个 Save("皇") 之后的位置插入
        int anchor = t.LastIndexOf("Save(\"皇\");");
        if (anchor < 0) { Console.WriteLine("  x 字形：没找到 Save(\"皇\")"); return; }

        int le = t.IndexOf('\n', anchor);
        if (le < 0) le = anchor + 10;

        // 坐标约定：0~1，y 向上。T=0.078 粗，TT=0.058 细
        string block =
            "\n" +
            "        // ===== 新增 15 字 =====",
            "        //",
            "        // 用「点万能」规则补齐原本画不出来的字：",
            "        //   点 单独 → 提",
            "        //   竖 + 点 → 竖钩",
            "        //   横 + 点 → 横折钩",
            "        //",
            "        // 所以画这些字时，钩和提都用「点」的小斜线表示 ——",
            "        // 远看是钩，近看是点，视觉上能接受。",
            "\n";

        Console.WriteLine("  → 准备插入字形块（下一步单独做，先用占位检查结构）");
    }

    static void Bullets()
    {
        Console.WriteLine("  （弹幕定义稍后）");
    }
}
