# LanStartWrite.Inkcanvas

基于 Jalium.UI 框架的屏幕批注组件，采用 GPU 加速渲染，提供流畅的墨迹书写体验。

## 项目概述

LanStartWrite.Inkcanvas 是一个使用 Jalium.UI 构建的屏幕批注模块。它通过分层窗口机制实现了透明全屏覆盖层与浮动工具栏的组合，支持画笔、荧光笔、激光笔等多种墨迹工具，适用于教学演示、会议讲解等场景。

## 技术栈

| 技术 | 版本 | 说明 |
|------|------|------|
| .NET | 10.0 | 运行时平台 |
| Jalium.UI | 26.10.2 | GPU 加速 UI 框架 |
| 渲染后端 | DirectX 12 | Windows 平台 GPU 渲染 |
| 标记语言 | JALXAML | 声明式 UI 定义 |
| 目标框架 | net10.0-windows | Windows 专属构建 |

## 项目结构

```
LanStartWrite.Inkcanvas/
├── src/LanStartWrite.Inkcanvas/        # 主项目
│   ├── Program.cs                       # 应用入口，初始化渲染上下文
│   ├── AnnotationToolbarWindow.*        # 浮动工具栏窗口
│   ├── AnnotationOverlayWindow.*        # 全屏批注覆盖层窗口
│   ├── PenSecondaryMenuWindow.*         # 笔次级菜单（颜色/粗细）
│   ├── SettingsWindow.*                 # 设置窗口
│   ├── SettingsNavPage.cs               # 设置导航页枚举
│   ├── PenKind.cs                       # 笔类型枚举
│   ├── InkCanvasTuning.cs               # InkCanvas 运行时调优
│   ├── InkRuntimeOptions.cs             # 墨迹运行时选项
│   ├── RadioToolToggleButton.cs         # 工具单选按钮控件
│   ├── app.manifest                     # 应用程序清单
│   └── *.jalxaml / *.cs                 # UI 定义与代码后置
├── tools/IconDump/                      # 图标导出工具
├── AGENTS.md                            # 开发规范（Jalium.UI 约定）
├── Design.MD                            # UI 设计基线
└── LanStartWrite.Inkcanvas.slnx         # 解决方案文件
```

## 核心模块

### 1. 入口与初始化

[Program.cs](src/LanStartWrite.Inkcanvas/Program.cs) 负责应用启动，按 Jalium.UI 推荐顺序执行：

- 初始化 GPU 渲染上下文（`RenderContext.GetOrCreateCurrent`），指定 Impeller 渲染引擎
- 设置全局主题键为 Light
- 应用 InkCanvas 启动默认调优参数
- 创建 `AnnotationToolbarWindow` 作为主窗口进入消息循环

### 2. 窗口分层架构

项目遵循「窗口分层原则」，使用两类独立窗口：

- **批注栏窗口**（AnnotationToolbarWindow）：自绘浮动窗口，透明宿主，仅渲染工具栏实体，支持拖拽、置顶
- **批注覆盖层窗口**（AnnotationOverlayWindow）：全屏透明窗口，承载 InkCanvas 画布，实时响应墨迹输入
- **设置窗口**（SettingsWindow）：标准系统窗口，使用正常标题栏与常规窗口行为

### 3. 墨迹引擎

基于 Jalium.UI 的 `InkCanvas` 控件，并进行了多项底层调优：

- **最小点距调优**（[InkCanvasTuning.cs](src/LanStartWrite.Inkcanvas/InkCanvasTuning.cs)）：通过反射降低 InkCanvas 最小采样点距，减轻快速书写时的采样丢弃
- **实时采样**：在 `PreviewPointerMove` 阶段直接采样指针位置，补偿标准事件的采样频率
- **运行时选项**（[InkRuntimeOptions.cs](src/LanStartWrite.Inkcanvas/InkRuntimeOptions.cs)）：支持压感、实时采样、抗锯齿、笔锋等选项的动态切换
- **激光笔机制**：通过 `DispatcherTimer` 实现激光笔划的渐变淡出动画

### 4. 工具系统

- **PenKind 枚举**：定义了 Pen（画笔）、Highlighter（荧光笔）、Laser（激光笔）三种笔类型
- **RadioToolToggleButton**：自定义互斥单选按钮，用于工具栏工具切换
- **次级菜单**：笔工具支持颜色（黑/红/蓝/绿）和粗细选择的弹出菜单

### 5. 设置系统

设置窗口采用 Win11 Fluent 设计风格，包含四个导航页面：

- **外观**：主题、强调色、透明度相关
- **墨迹**：压感、实时采样、抗锯齿、笔锋等渲染选项
- **交互**：工具栏置顶、窗口行为等
- **关于**：版本信息

实现上使用「左侧导航 + 右侧内容面板」结构，支持汉堡菜单折叠。

## 构建说明

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
dotnet publish src/LanStartWrite.Inkcanvas -c Release -r win-x64 \
    -p:PublishSingleFile=true -p:SelfContained=true
```

## 设计规范

UI 实现遵循 [Design.MD](Design.MD) 中的设计基线，包括：

- 窗口分层原则（浮动栏 vs 标准窗）
- Jalium.UI 实现约定（JALXAML + code-behind）
- Win11 Fluent 对齐规则（导航结构、间距节奏、文本层级）
- 控件语义选择规范（开关 vs 复选框等）

## 开发约定

项目开发需遵循 [AGENTS.md](AGENTS.md) 中的 Jalium.UI 框架规范，要点包括：

- 使用 JALXAML（`.jalxaml`）定义 UI，命名空间为 `https://schemas.jalium.dev/jalxaml/presentation`
- 启动顺序：`ThemeLoader.Initialize()` → `new Application()` → `app.Run(window)`
- 这是 Jalium.UI 项目，**不是 WPF / WinUI / Avalonia**

## 已知实现细节

- 项目使用自定义 MSBuild 目标移除 `.uic` 嵌入资源，强制走 `XamlReader` + 嵌入 `.jalxaml` 的加载路线，以避免 `x:Name` 丢失
- 调试构建下启用墨迹诊断事件处理（`InkDiag`）
- 全局固定使用 Light 主题，避免暗色下 TextPrimary 与浅色工具栏不协调

## 许可证

MIT
