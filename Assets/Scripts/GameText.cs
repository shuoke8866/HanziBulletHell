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

        /// <summary>
        /// 玩家**头顶**飘的那行字 —— 拾取笔画 / 开宝箱时用。
        ///
        /// 刻意写得短：飘字的空间小、只停 1 秒左右，
        /// 不像屏幕提示条那样能放长句子。
        /// </summary>
        public static string 获得笔画(string stroke) { return $"恭喜你获得了「{stroke}」"; }

        /// <summary>靠近宝箱时在宝箱上方飘的提示</summary>
        public static string 开宝箱() { return "按 F 打开宝箱"; }

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

        /// <summary>
        /// 标题画面底部的版权声明。
        ///
        /// 作用：任何录屏 / 截图 / 搬运都会带着这行字 —— 最低成本的"水印"。
        /// 年份动态取，跨年不用改代码。
        /// </summary>
        public static string 版权声明()
        {
            return $"© {System.DateTime.Now.Year} shuoke8866　保留所有权利";
        }

        /// <summary>
        /// 背景音乐署名。
        ///
        /// **这是 CC BY 4.0 的合规要求，不是可选装饰** ——
        /// 该许可允许自由使用（含商业用途），唯一义务就是署名。
        /// 删掉这行就不再合规。
        /// </summary>
        public static string 音乐署名()
        {
            return "BGM: \"Dentaneosuchus Hunt\" Kevin MacLeod (incompetech.com)　" +
                   "CC BY 4.0";
        }

        public static string 开局说明() { return "哦～很大胆的选择呢"; }

        /// <summary>
        /// 每个笔画在**开局六选一**里显示的特征句（玩家提供的文案）。
        ///
        /// 放在这里而不是硬编码进 UI —— 以后改文案不用碰 UI 代码。
        /// 没写的笔画会给一句中性说明，不会留空。
        /// </summary>
        public static string 笔画特点(StrokeType st)
        {
            switch (st)
            {
                case StrokeType.Heng:  return "平平无奇的笔画，但是非常好用的合成材料";
                case StrokeType.Shu:   return "在发射时提供护盾，这可能是你最重要的自保手段哦";
                case StrokeType.Dian:  return "好小的笔画哦，或许在后期会起到关键作用";
                case StrokeType.Pie:   return "弹道偏左的刺客";
                case StrokeType.Na:    return "弹道偏右的战士";
                case StrokeType.Zhe:   return "额……弹幕挺好看的……";
                default:               return "一种笔画";
            }
        }

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
    //  四·五、开场游戏介绍
    // ============================================================
    //
    //  **这是你要写的部分。**
    //
    //  在教程层之前显示一屏文字，玩家按空格/回车/点击继续。
    //  数组里有几条就显示几页（现在只有一条占位）。
    //
    //  写的时候注意：
    //    · 每条**不要太长**（4 行以内最好）—— 一屏放不下会挤
    //    · 想换行直接在里面写 \n
    //    · 改完存盘立刻生效，不用重新生成教程层
    //    · 想加页就往数组里多加一条字符串
    public static class Intro
    {
        /// <summary>
        /// 开场背景故事。**玩家看完才能开始游戏**（显示时 Time.timeScale = 0）。
        ///
        /// 数组里有几条就是几页，每页按 空格 / 回车 / 点击 翻下一页。
        /// 最后一条之后按键即开始游戏。
        ///
        /// 写作建议（想改的话可以参考）：
        ///   · 一页 3~5 行最舒服，太多会挤（字号 34，画布 900×320）
        ///   · 悬念留在**最后一句**，不要一开始就把"跌落的笔画"说透
        ///   · 换行直接写 \n
        /// </summary>
        public static string[] 正文()
        {
            return new[]
            {
                "汉字国建立的第四千九百年，新上任的皇不理朝政，一心钻研武艺。\n" +
                "他搜集民间群众汉字们的各类笔画，提炼其核心。",

                "一时间民不聊生，灾难四起。\n" +
                "民众身上布满被强行剥夺笔画后的癔症。",

                "然而我们的皇不思悔改，特立独行，\n" +
                "自以为无人可威胁他的统治。",

                "……然而他没想到，\n" +
                "在押送笔画的路上，有一笔蕴含民众心中反抗之力的笔画掉落其中，\n" +
                "生出了自己的灵智。",

                "更让高高在上的皇想不到的是，\n" +
                "这枚掉落的笔画，竟会在后来威胁他的统治……",
            };
        }

        public static string 继续() { return "空格 / 回车 / 点击 继续"; }
        public static string 开始() { return "空格 / 回车 / 点击 开始"; }
    }

    // ============================================================
    //  四·六、悬浮教学点（点 + 气泡）
    // ============================================================
    //
    //  取代原来"走进某个房间就弹一屏字"的做法。
    //  现在是一个小点飘在玩家右上方，玩家**做出对应行为**时才弹气泡。
    //
    //  ## 怎么写
    //
    //  每一步 = 一个触发条件 + 一组文案：
    //      new CoachStep(触发, "文案1", "文案2", ...)
    //
    //  触发条件（Trigger 枚举）：
    //      OnStart      一进教程层
    //      Moved        移动超过一定距离
    //      FirstShot    第一次开枪
    //      GotStroke    第一次拿到笔画（捡到 / 开箱）
    //      StrokeCount  当前字笔画数达到 N（用 Num 字段指定）
    //      OpenedBag    第一次按 E 开背包
    //      SwappedHanzi 第一次换字成功
    //      EnteredBoss  走进 BOSS 房
    //      BossKilled   BOSS 被打死
    //
    //  顺序就是显示顺序；每一步只弹一次。
    //  **改文案不用重新生成场景**，存盘立刻生效。
    public enum CoachTrigger
    {
        None,
        OnStart,
        Doubt,
        Moved,
        FirstShot,
        GotStroke,
        StrokeCount,
        OpenedBag,
        SwappedHanzi,
        EnteredBoss,
        BossKilled,

        /// <summary>
        /// 玩家进入 CoachStep.room 指定的房间时触发。
        ///
        /// 这是玩家要求的「按房间播字幕」—— 哪条字幕在哪个房间，
        /// 完全由 CoachStep.room 决定，不用改代码。
        /// </summary>
        InRoom,
    }

    public struct CoachStep
    {
        public CoachTrigger trigger;
        public int num;              // StrokeCount 用：需要几画
        public string[] lines;

        /// <summary>
        /// 在哪个房间播（只在 trigger == InRoom 时有意义）。
        /// 填**房间对象名**，比如 Room2_Collect。
        /// 一进那间房就播这条，和玩家在哪、做了什么无关。
        /// </summary>
        public string room;

        public CoachStep(CoachTrigger t, params string[] ls)
        {
            trigger = t; num = 0; lines = ls; room = null;
        }

        public CoachStep(CoachTrigger t, int n, params string[] ls)
        {
            trigger = t; num = n; lines = ls; room = null;
        }

        /// <summary>在指定房间播（InRoom 专用）。用法：new CoachStep("Room2_Collect", "...")</summary>
        public CoachStep(string roomName, params string[] ls)
        {
            trigger = CoachTrigger.InRoom;
            num = 0;
            lines = ls;
            room = roomName;
        }
    }

    /// <summary>
    /// 一条「字幕 → 门」规则。
    ///
    /// 玩家要的工具就是编辑这张表：规定说完哪句话之后开哪扇门。
    /// </summary>
    public struct 门规则
    {
        /// <summary>第几条字幕（0 开始，对应 教学步骤() 的下标）</summary>
        public int 字幕序号;

        /// <summary>要打开的门对象名（场景里的 Door 组件上的 GameObject 名）</summary>
        public string 门名;

        /// <summary>等多久再开（秒）。0.5~1.5 的比较自然。</summary>
        public float 延迟;

        public 门规则(int idx, string door, float delay)
        {
            字幕序号 = idx;
            门名 = door;
            延迟 = delay;
        }
    }

    /// <summary>
    /// 字幕 → 门 的规则表。
    ///
    /// **这是玩家要的那张表**：每条规则说"第几条字幕播完后开哪扇门"。
    /// 用 Tools/字幕房间开门协同工具 编辑，不用手改这里。
    ///
    /// 默认规则（对接现在的 5 房间布局）：
    ///   第 1 条说完（Room0 最后一句「先去把它们收起来」）→ 开 Door_0to1
    ///   第 3 条说完（Room1 最后一句「都是这六种拼出来的」）→ 开 Door_1to2
    ///   第 5 条说完（Room2 最后一句「意外收获？」）      → 开 Door_2to3
    ///   第 9 条说完（Room3 最后一句「实力越强？」）      → 开 Door_3to4
    /// </summary>
    public static 门规则[] 门规则表()
    {
        return new[]
        {
            // 字幕序号, 门名, 延迟（由「字幕 × 房间开门 协同工具」生成）
            new 门规则(1, "Door_0to1", 1f),
            new 门规则(2, "Door_1to2", 1f),
            new 门规则(3, "Door_2to3", 1f),
            new 门规则(8, "Door_3to4", 1f),
        };
    }

    public static CoachStep[] 教学步骤()
    {
        return new[]
        {
            // ---- 开场房：主角的疑虑 + 随行小点出现 ----
            //
            // 这一段是新加的：背景故事看完之后，玩家落在一个空房里，
            // 小点在这里"出现"，并表达主角的疑虑，然后指向笔画房。
            // 起点房
            new CoachStep("Room0_Doubt",
                "……",
                "这里是哪里？我为什么会在这里？"),

            new CoachStep("Room0_Doubt",
                "（一个小小的墨点从你身上浮起，悬在你身旁）",
                "喂，你能听懂我说话吗？",
                "先跟我过来吧",
                "前面那间屋子里散落着许多笔画 —— 先去把它们收起来。"),

            // ---- 进入笔画房之后 ----
            // 笔画展示房
            new CoachStep("Room1_Start",
                "通过键盘上的”↑ ↓← →“来控制射击方向吧",
                "地上这一排是汉字的基本笔画，共六种：横、竖、撇、捺、点、折"),

            // 收集房
            new CoachStep("Room2_Collect",
                "在这个世界内的汉字都是这六种拼出来的"),

            new CoachStep("Room2_Collect",
                "打倒一些特殊的敌人或许会有意外收获？"),

            // 换字房
            new CoachStep("Room3_Swap",
                "随着笔画增多，你可以按 E 打开背包进行更换"),

            new CoachStep("Room3_Swap",
                "在背包页面你可以通过 A / D 翻页，里面是你现在能拼出来的所有字"),

            new CoachStep("Room3_Swap",
                "通过数字键切换到你想要的汉字吧",
                "想换回来也可以哦，笔画不会丢，随时能换到目前能组成的所有汉字"),

            new CoachStep("Room3_Swap",
                "越复杂的汉字或许实力越强？"),

            // BOSS 房
            new CoachStep("Room4_Boss",
                "前面是。。。？帝国的走狗？好在他落单了",
                "打败他，让我们传送到正常世界"),

            new CoachStep("Room4_Boss",
                "可恶，受到某种影响，我们得从头开始了"),
        };
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
                "在这个世界内的汉字都是这六种拼出来的",
                "在这个世界内的汉字都是这六种拼出来的"
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
