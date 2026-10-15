using UnityEngine;

/// <summary>
/// **所有显示给玩家的文案，集中在这里。**
///
/// ## 为什么要集中
///
/// 原来 46 条文案散落在 15 个文件里 —— 想改一句提示得先翻遍
/// QuickBarUI / BackpackUI / Room / BossHuang / Portal / StrokePickup ...
/// 而且改完很难确认"游戏里到底哪一句变了"。
///
/// ## 怎么用
///
/// 按情境分组，每组一个静态方法或字段：
///
///   GameText.Toast.切换汉字("十")        →  "切换到「十」"
///   GameText.Boss.皇降临                  →  "BOSS「皇」降临"
///   GameText.Hint.快捷栏                  →  "1-9 换字    Q 最强    E 背包"
///   GameText.Tutorial.走位                →  string[] 教程提示
///
/// 想改文案：直接改这个文件里的字符串；或者用 Tools/字幕编辑器 图形化地改。
///
/// ## 设计约定
///
/// · 带变量的用方法（参数名写清楚），固定的用字段
/// · 颜色**不放在这里** —— 颜色是表现，和文案分开，改文字时不用管颜色代码
/// · 教程提示用 string[] 返回，顺序就是显示顺序
/// </summary>
public static class GameText
{
    // ============================================================
    //  一、操作反馈（拾取 / 换字 / 房间）
    // ============================================================

    public static class Toast
    {
        public static string 切换汉字(string name) { return $"切换到「{name}」"; }
        public static string 已是该字(string name) { return $"已经是「{name}」了"; }
        public static string 切换失败(string name) { return $"「{name}」切换失败（笔画不足）"; }
        public static string 空号位(int index, int count) { return $"[{index}] 是空格子（本页只有 {count} 个字）"; }
        public static string 快捷栏空位(int index) { return $"{index} 号位是空的（仓库还拼不出这么多字）"; }
        public static string 选中最强(string name) { return $"已切到最强：「{name}」"; }
        public static string 没有更强的() { return "仓库里还拼不出别的字"; }

        public static string 拾取存背包(string stroke) { return $"拾取 {stroke}（存背包）"; }
        public static string 背包已满(string stroke) { return $"背包已满，「{stroke}」留在地上"; }

        public static string 房间清理完成() { return "房间清理完成！"; }
        public static string 获得词条(string name, string desc) { return $"获得词条：{name} —— {desc}"; }

        public static string 进入下层() { return "按 回车 进入下一层"; }

        public static string 选字超时() { return "超时 —— 自动选了第一个"; }
        public static string 选定汉字(string name) { return $"选定「{name}」"; }
    }

    // ============================================================
    //  二、BOSS 台词
    // ============================================================

    public static class Boss
    {
        public static string 皇降临() { return "BOSS「皇」降临"; }
        public static string 皇暴怒() { return "「皇」暴怒！"; }
        public static string 皇召唤(int n) { return $"「皇」召唤了 {n} 名士兵"; }
        public static string 皇被击败() { return "「皇」已被击败！"; }

        // 其它 BOSS 目前没有专属台词，用这一组通用的
        public static string 通用降临(string name) { return $"BOSS「{name}」出现！"; }
        public static string 通用被击败(string name) { return $"「{name}」已被击败！"; }
    }

    // ============================================================
    //  三、常驻提示条（屏幕底部 / 面板底部）
    // ============================================================

    public static class Hint
    {
        /// <summary>战斗界面底部的快捷栏提示</summary>
        public static string 快捷栏() { return "1-9 换字    Q 最强    E 背包（可翻页看全部）"; }

        /// <summary>快捷栏满员、没有更多候选字时</summary>
        public static string 快捷栏全部显示() { return "1-9 换字    Q 最强    E 背包（可翻页看全部）"; }

        /// <summary>快捷栏还有字没显示完时</summary>
        public static string 快捷栏还有更多(int rest) { return $"1-9 换字    Q 最强    E 背包（还有 {rest} 个可换，翻页查看）"; }

        public static string 背包() { return "1-9 选字     A / D 翻页     鼠标点击也可选     E / Esc 关闭"; }

        public static string 选字面板() { return "按 1-3 或点一下卡片。选过一次就会记住，以后自动沿用。"; }

        public static string 开局选笔画() { return "按 1-6 或点一下卡片"; }
    }

    // ============================================================
    //  四、面板标题与说明
    // ============================================================

    public static class Panel
    {
        public static string 开局标题() { return "选一支笔画，开始"; }
        public static string 开局说明() { return "哦～很大胆的选择呢"; }

        public static string 选字标题() { return "这几个字组成一样欸，你更想要哪个"; }

        public static string 背包当前汉字(string name) { return $"当前汉字「{name}」"; }
        public static string 背包可用笔画(string strokes) { return $"可用笔画 {strokes}"; }
        public static string 背包可换数量(int n) { return $"可换 {n} 个字"; }
        public static string 背包空仓() { return "仓库里还拼不出任何字 —— 先去捡笔画"; }
        public static string 背包页码(int page, int total, int count)
        {
            return $"第 {page} / {total} 页　　（共 {count} 个候选，按笔画数从多到少）";
        }
        public static string 背包无候选(string strokes) { return $"可切换的汉字：暂无 —— 可用笔画 {strokes} 拼不出任何字"; }
        public static string 背包候选标题(string strokes, int n) { return $"可切换的汉字（可用笔画 {strokes}，共 {n} 个）"; }

        public static string 当前汉字标签() { return "当前汉字："; }
        public static string 背包空() { return "背包：空"; }
    }

    // ============================================================
    //  五、新手教程提示（顺序 = 显示顺序）
    // ============================================================

    public static class Tutorial
    {
        /// <summary>起始房：认全六种笔画</summary>
        public static string[] 起始房()
        {
            return new[]
            {
                "用 W A S D 控制走位",
                "地上这一排是汉字的基本笔画，共六种：横、竖、撇、捺、点、折",
                "目前世界内的汉字都是这六种拼出来的",
                "试着通过键盘上的”↑ ↓← →“来控制射击方向吧"
            };
        }

        /// <summary>收集房：捡笔画与自动合成</summary>
        public static string[] 收集房()
        {
            return new[]
            {
                "打倒一些特殊的敌人或许会有意外收获？",
                "随着笔画增多，你可以按E打开背包进行更换",
                "在背包页面你可以通过A / D 翻页，里面是你现在能拼出来的所有字。"
            };
        }

        /// <summary>换字房：切换与背包</summary>
        public static string[] 换字房()
        {
            return new[]
            {
                "通过数字键切换到你想要的汉字吧",
                "想换回来也可以哦，笔画不会丢，随时能换到目前能组成的所有汉字",
                "越复杂的汉字或许实力越强？",
                ""
            };
        }

        /// <summary>BOSS 前：交代教程与实战的关系</summary>
        public static string[] 头目前()
        {
            return new[]
            {
                "前面是。。？帝国的走狗？好在他落单了",
                "打败他，让我们传送到世界中",
                "可恶，受到时空乱流影响，我们得从头开始了"
            };
        }
    }

    // ============================================================
    //  六、诊断 / 调试（一般玩家看不到，但也在这一起管）
    // ============================================================

    public static class Debug
    {
        public static string 没有能用的笔画() { return "没有能立刻使用的笔画"; }
        public static string 用不上该笔画(string stroke) { return $"「{stroke}」现在用不上，先留着"; }
    }
}
