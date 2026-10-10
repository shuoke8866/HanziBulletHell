# WebGL 构建产物目录

这个目录存放 WebGL 构建结果，GitHub Pages 会从这里发布。

## 如何生成

在 Unity 中：**菜单栏 → Build → 构建 WebGL 到 docs/**

或者命令行：

```
Unity.exe -quit -batchmode -projectPath . -executeMethod WebGLBuilder.Build
```

## 为什么放在 docs/

GitHub Pages 支持直接从仓库的 `docs/` 目录发布（Settings → Pages → Branch: main / docs）。
这样不需要额外的 gh-pages 分支，也不需要 GitHub Actions，构建产物随代码一起版本化。
