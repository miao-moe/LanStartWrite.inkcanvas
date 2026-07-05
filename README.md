# LanStartWrite.Inkcanvas

基于 Jalium.UI 的屏幕批注工具，支持画笔、荧光笔、激光笔等多种墨迹工具。

## 简介

LanStartWrite.Inkcanvas 是一个屏幕批注模块，基于 Jalium.UI 框架构建，通过透明全屏覆盖层和浮动工具栏实现屏幕书写功能，适用于教学演示、会议讲解等场景。

## 技术栈

- .NET 10
- Jalium.UI 26.10.2
- JALXAML

## 功能

- 画笔 / 荧光笔 / 激光笔
- 多种颜色和粗细调节
- 橡皮擦
- 浮动工具栏，可拖拽
- 设置面板

## 构建

### 环境要求

- .NET 10 SDK
- Windows 10/11

### 编译运行

```bash
dotnet restore
dotnet run --project src/LanStartWrite.Inkcanvas
```

### 发布

```bash
dotnet publish src/LanStartWrite.Inkcanvas -c Release -r win-x64
```

## 项目结构

```
LanStartWrite.Inkcanvas/
├── src/LanStartWrite.Inkcanvas/    # 主项目
│   ├── Program.cs                   # 入口
│   ├── AnnotationToolbarWindow.*    # 工具栏窗口
│   ├── AnnotationOverlayWindow.*    # 批注覆盖层窗口
│   ├── PenSecondaryMenuWindow.*     # 笔次级菜单
│   ├── SettingsWindow.*             # 设置窗口
│   ├── PenKind.cs                   # 笔类型
│   ├── InkCanvasTuning.cs           # 墨迹调优
│   ├── InkRuntimeOptions.cs         # 运行时选项
│   └── RadioToolToggleButton.cs     # 工具切换按钮
├── tools/IconDump/                  # 图标导出工具
├── AGENTS.md                        # 开发规范
├── Design.MD                        # 设计基线
└── LanStartWrite.Inkcanvas.slnx     # 解决方案
```

## 相关文档

- [Design.MD](Design.MD) — UI 设计基线
- [AGENTS.md](AGENTS.md) — 开发规范

## 许可证

MIT
