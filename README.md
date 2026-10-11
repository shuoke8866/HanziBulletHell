<div align="center">

# 相信字 · Trust Hanzi

**用汉字笔画拼出你的武器 —— 一款以「造字」为核心机制的 2D 肉鸽弹幕游戏**

[![Play Now](https://img.shields.io/badge/▶_在线试玩-GitHub_Pages-2ea44f?style=for-the-badge)](https://shuoke8866.github.io/trust-hanzi/)
![Unity](https://img.shields.io/badge/Unity-2022.3.62f3c1-000000?style=for-the-badge&logo=unity)
![C#](https://img.shields.io/badge/C%23-100%25-239120?style=for-the-badge&logo=csharp)

<!-- 建议在这里放一张游戏截图，例如 docs/screenshot.png，能显著提升第一印象 -->

</div>

---

## 这是什么

一款**中国汉字主题的肉鸽弹幕游戏**。核心创意是：**玩家不捡武器，而是捡「笔画」**。

`一` → 加一横变 `二` → 再加一横变 `三` → 加一竖变 `干` → 再加一横变 `王` → 加一点变 `主`

**每一次合成都会实时改变角色的外形和弹幕形态**，越复杂的汉字火力越强。汉字不再是贴图或文字，而是**真正的武器系统**。

## 🎮 在线试玩

> **https://shuoke8866.github.io/trust-hanzi/**
>
> 纯浏览器运行（WebGL），无需下载安装。建议用 Chrome / Edge。

### 操作

| 按键 | 功能 |
|---|---|
| `WASD` | 移动 |
| `方向键` | 射击（上下左右） |
| `F` | 开宝箱 / 拾取笔画 |
| `1-6` | 快捷使用背包笔画 |
| `Q` | 自动使用第一个可用笔画 |
| `E` | 打开背包（暂停，可多选笔画合成） |

**背包内**：`1-6` 或鼠标左键勾选笔画 → `回车`/右键 合成 → `C` 清空选择

## 玩法循环

```
捡笔画 → 合成更大的汉字 → 变强 → 清房间 / 打 BOSS → 拿词条和笔画 → 更深一层
```

- **3 层关卡**，逐层变难（敌人血量 / 速度 / 数量随层数成长）
- **BOSS**：「官」「士」「皇」——其中**「皇」会召唤士兵「兵」**，血量过半进入暴怒
- **词条系统**：清房随机获得（射速 / 移速 / 伤害 / 弹速 / 生命）
- **真肉鸽**：死亡清空所有笔画，回到第一层

---

## 技术亮点

### 1. 汉字完全由代码生成，不依赖任何字体

这是整个项目最核心的技术点。常规做法是用 TextMeshPro + 中文字体渲染汉字，但会带来三个问题：字体图集巨大（一个中文字符集动辄 10-20 MB）、需要预先烘焙字形、缺字就显示"豆腐块"。

**本项目自己实现了字形光栅化器**（[`HanziRenderer.cs`](Assets/Scripts/HanziRenderer.cs)）：

```csharp
// 每个笔画是一条折线；渲染时先按 miter 方式
// 把折线展开成"两端宽度可不同"的线段，再逐像素做距离场判定
struct Poly { Vector2[] pts; float t; }   // 笔画 = 2~3 个点的折线
struct Seg  { float x0,y0,x1,y1,t0,t1; }  // 展开后：带宽度线段（两端可异宽）

// 关键：用点到线段距离判定像素是否在笔画内
float half = Mathf.Lerp(s.t0, s.t1, t) * 0.5f;
if (ex*ex + ey*ey <= half*half) on = true;
```

**收益**：
- 支持任意角度笔画（撇、捺、点、折），靠 miter 让折画拐角正确接合
- **零字体依赖** —— 游戏内 47 个汉字全部运行时生成，永远不会缺字
- Sprite 带缓存，字形只光栅化一次

同时 UI 文字用 **Noto Sans SC**（SIL OFL 开源许可），并在 CI 里自动扫源码收集用到的字符、重建 TMP 图集（[`HanziFontFixer.cs`](Assets/Editor/HanziFontFixer.cs)）。

### 2. 合成系统：按笔顺的「字形包含」判定

简单地把笔画当"无序集合"匹配会崩掉 —— `天`(一横一横一撇一捺) 和 `本`(一横一竖一撇一捺) 的多重集**完全相同**，无法区分。

本项目用**有序子序列 + 严格 +1 笔**双规则（[`HanziSystem.cs`](Assets/Scripts/HanziSystem.cs)）：

```csharp
// 规则 1：目标字 = 当前字 +1 笔（强制一步一步长，杜绝跨级乱跳）
if (def.strokes.Count != cur.Count + 1) continue;
// 规则 2：当前字的笔顺必须是目标字的有序子序列（允许新笔画插在中间）
if (MatchPrefixEnd(cur, def.strokes) < 0) continue;
```

**这两条缺一不可**，我在开发中实测过：
- 只用规则 1 → `二 + 竖` 永远长不成 `王`（王比二多 2 笔），玩家卡死
- 只用规则 2 → `一 + 横` 会直接跳到 `王`，反而堵死 `三 + 竖` 这条正路

另外用**递归深度前瞻**避免把玩家送进死路：

```csharp
// 二 + 横 的 5 个候选（三/士/干/工/土）下一步都只有 1 个去向，
// 但 三 走一步就断，士/干/工/土 还能一路长到 王 —— 所以要比"最远能走几步"
int depth = MaxGrowthDepth(g, 0);
```

### 3. 数据驱动的敌人系统

敌人属性、行为、弹幕形态全部集中在 [`EnemyDatabase.cs`](Assets/Scripts/EnemyDatabase.cs)，加一种怪只需加一条定义：

```csharp
Register(new EnemyDef {
    id = "bing", displayName = "兵", glyph = "兵",
    behavior = EnemyBehavior.Chase,          // 追击/中距/环绕/冲刺/固定
    shotPattern = EnemyShotPattern.Aimed,    // 瞄准/双发/扇形/环形/螺旋/弹幕墙/对射/三连
    health = 10, moveSpeed = 2.2f, meleeDamage = 1f
});
```

- **5 种行为** + **8 种弹幕形态**
- 精英怪用同一套定义 + 倍率，不需要新建预制体
- **每个敌人显示自己的汉字**，且做了镜像补偿（本体翻转时字形保持正向）

### 4. 关卡与数值

- **波次系统** + **房间类型**（起始 / 战斗 / 精英 / 宝箱 / BOSS）
- **引用计数门**（[`DoorProbe.cs`](Assets/Scripts/DoorProbe.cs)）：场景里每扇门被两个房间共用，旧逻辑会互相锁死玩家。改成"谁要关就 Claim，清房就 Release，没人要求才真正打开"
- **数值曲线**：单发伤害只跟笔画数挂钩，弹数越多单发越轻（`1 + 弹数/5` 摊薄），避免"弹数 × 伤害"平方级膨胀。全部常量集中在 [`GameBalance.cs`](Assets/Scripts/GameBalance.cs)

---

## 项目结构

```
Assets/
├── Scripts/
│   ├── HanziRenderer.cs     # 汉字字形光栅化器（核心）
│   ├── HanziSystem.cs       # 合成规则 + 弹幕发射
│   ├── EnemyDatabase.cs     # 敌人定义（数据驱动）
│   ├── GameBalance.cs       # 数值配置集中处
│   ├── Room.cs              # 房间 / 波次 / 清房奖励
│   ├── BackpackUI.cs        # 暂停式背包（多选笔画合成）
│   ├── QuickBarUI.cs        # 战斗常驻快捷栏
│   └── ...
├── Editor/
│   ├── HanziFontFixer.cs    # 自动重建 TMP 中文字体图集
│   └── WebGLBuilder.cs      # CI 构建脚本（GZip 压缩）
├── Scenes/                  # Level1 / Level2 / Level3
└── Fonts/                   # Noto Sans SC（SIL OFL）
```

## 本地运行

1. 用 **Unity 2022.3.62f3c1**（或同系列 2022.3.x LTS）打开本仓库根目录
2. 打开 `Assets/Scenes/Level1.unity`
3. 点 Play

> 首次打开时编辑器脚本会自动重建中文字体图集，Console 会打印进度。

## 构建 WebGL

构建产物输出到 `docs/` 目录，GitHub Pages 直接从 `main` 分支的 `/docs` 发布 ——
不需要额外的 `gh-pages` 分支，也不需要 GitHub Actions。

**前提**：Unity Hub → Installs → 对应版本 → Add modules → 勾选 **WebGL Build Support**。

**方式一（编辑器内一键构建）**：菜单栏 → **Build → 构建 WebGL 到 docs/**

**方式二（命令行）**：

```powershell
& "<Unity 安装路径>\Editor\Unity.exe" -quit -batchmode -nographics `
  -projectPath "<本项目路径>" `
  -executeMethod WebGLBuilder.Build `
  -logFile -
```

构建脚本（[`Assets/Editor/WebGLBuilder.cs`](Assets/Editor/WebGLBuilder.cs)）会自动：

- **关闭压缩**。Unity 的 WebGL 加载器用 `<script src>` 注入 framework 文件，
  而浏览器不会自动解压 `<script>` 加载的 gzip/brotli 内容 —— 它依赖服务器发送
  `Content-Encoding` 响应头，而 GitHub Pages 对静态文件不发这个头。
  用 Brotli/GZip 会直接报 `Unable to parse Build/xxx.framework.js.gz!`
- 关闭 Unity 启动画面、设置 1280×720、开启数据缓存
- 托管代码剥离 High + 剥离引擎代码 + IL2CPP Master 编译（压缩 wasm）
- 构建前自检（WebGL 模块是否安装、场景是否存在），缺什么就打印可读的报错

构建完成后提交并推送 `docs/` 即可更新线上版本。

## 第三方资源与许可

| 资源 | 许可 |
|---|---|
| **Noto Sans SC** | [SIL Open Font License 1.1](https://scripts.sil.org/OFL) —— 允许自由再分发 |
| TextMesh Pro | Unity 官方包 |
| 代码 | 见 [LICENSE](LICENSE) |

> 本项目**不包含**任何专有字体。早期版本使用过 SimSun，已因再分发许可问题移除。

---

<div align="center">

**如果这个项目对你有帮助，欢迎点个 Star ⭐**

</div>
