# LanStartWrite.Inkcanvas

> 基于 Jalium.UI 的 GPU 加速屏幕批注工具，灵感来自 InkCanvasForClass Community Edition (ICCE)。

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![Jalium.UI](https://img.shields.io/badge/Jalium.UI-26.10.2-purple.svg)](http://jaliumui.top)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)](#)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## 简介

LanStartWrite.Inkcanvas 是一款轻量级屏幕批注工具，适用于教学演示、会议讲解、在线授课等场景。基于 Jalium.UI 框架开发，采用 DirectX 12 GPU 渲染，提供流畅的书写体验。

### 设计参考

本项目参考了 [InkCanvasForClass Community Edition (ICCE)](https://github.com/InkCanvasForClass/community) 的交互设计，感谢 CJK_mkp 及贡献团队的开源精神。

## 功能特性

### 绘图工具

- **画笔**：支持压感书写，流畅笔锋，可调粗细与颜色
- **荧光笔**：半透明标注，矩形笔尖
- **激光笔**：演示指引，自动淡出消失
- **橡皮擦**：按笔划擦除

### 形状工具

- **直线**：精确绘制直线
- **箭头**：带箭头端点的指向线
- **矩形**：拖拽绘制矩形框
- **椭圆**：拖拽绘制椭圆

### 编辑功能

- **文字**：点击画布添加文本标注
- **撤销/重做**：完整的历史记录管理（参考 ICCE TimeMachine）
- **清空画布**：一键清除所有笔迹
- **保存截图**：将当前批注保存为 PNG 图片

### 交互特性

- **鼠标模式**：切换回正常鼠标操作，不干扰其他软件
- **多笔颜色**：内置多种常用颜色
- **笔触粗细**：可调节笔触大小
- **触控支持**：支持触摸屏输入
- **多显示器**：跨显示器批注

## 快速开始

### 环境要求

- Windows 10 1809 及以上 / Windows 11
- .NET 10 运行时
- DirectX 12 兼容显卡（或软件渲染回退）

### 下载安装

前往 [Releases](../../releases) 页面下载：

- **安装版**（`*_setup.exe`）：标准 Windows 安装程序，支持桌面快捷方式
- **便携版**（`*_portable.zip`）：解压即用，无需安装

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/LanStartWrite/Inkcanvas.git
cd Inkcanvas

# 还原依赖
dotnet restore

# 编译运行
dotnet run --project src/LanStartWrite.Inkcanvas
```

## 构建打包

本项目提供 `build.ps1` 脚本，可一键生成安装包和便携版。

### 便携版（无需额外工具）

```powershell
.\build.ps1 -SkipInstaller
```

生成 `artifacts/LanStartWrite.Inkcanvas_<version>_portable.zip`

### 安装包 + 便携版（需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)）

```powershell
.\build.ps1
```

生成：
- `artifacts/LanStartWrite.Inkcanvas_<version>_setup.exe` — 安装包
- `artifacts/LanStartWrite.Inkcanvas_<version>_portable.zip` — 便携版

### 构建参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `-Version` | 版本号 | 从 csproj 读取 |
| `-SkipInstaller` | 跳过安装包生成 | `$false` |

## 项目结构

```
LanStartWrite.Inkcanvas/
├── src/LanStartWrite.Inkcanvas/        # 主项目
│   ├── AnnotationOverlayWindow.*       # 批注覆盖层窗口（全屏透明）
│   ├── AnnotationToolbarWindow.*       # 工具栏窗口（浮动）
│   ├── PenSecondaryMenuWindow.*        # 笔次级菜单（颜色/粗细）
│   ├── SettingsWindow.*                # 设置窗口
│   ├── StrokeHistory.cs                # 撤销/重做管理器
│   ├── InkCanvasTuning.cs              # InkCanvas 调优
│   ├── InkRuntimeOptions.cs           # 运行时墨迹选项
│   ├── AnnotationToolKind.cs          # 工具类型枚举
│   ├── PenKind.cs                     # 笔类型枚举
│   ├── RadioToolToggleButton.cs       # 工具单选按钮
│   └── Program.cs                     # 入口
├── tools/IconDump/                     # 图标导出工具
├── build.ps1                           # 构建打包脚本
├── Design.MD                           # 设计基线文档
└── AGENTS.md                          # 项目规范（Jalium.UI 约定）
```

## 技术栈

| 技术 | 说明 |
|------|------|
| [.NET 10](https://dotnet.microsoft.com/) | 运行时平台 |
| [Jalium.UI](http://jaliumui.top) v26.10.2 | GPU 加速 UI 框架 |
| JALXAML | 声明式 UI 标记语言 |
| DirectX 12 | GPU 渲染后端 |
| Inno Setup 6 | 安装包打包工具 |

## 使用说明

### 基本操作

1. 启动程序后，屏幕侧边出现浮动工具栏
2. 点击工具栏图标选择工具
3. 在屏幕任意位置进行批注
4. 切换到「鼠标模式」可正常操作其他软件

### 工具栏

| 图标 | 功能 | 说明 |
|------|------|------|
| 🖱️ | 鼠标模式 | 切换回正常鼠标 |
| ✏️ | 画笔 | 自由书写 |
| 🖊️ | 荧光笔 | 半透明标注 |
| 🔴 | 激光笔 | 演示指引（自动消失） |
| 🧹 | 橡皮擦 | 擦除笔迹 |
| 🔤 | 文字 | 添加文本 |
| ➡️ | 箭头 | 绘制箭头 |
| 📏 | 直线 | 绘制直线 |
| ▭ | 矩形 | 绘制矩形 |
| ⭕ | 椭圆 | 绘制椭圆 |
| ↩️ | 撤销 | 撤销上一步 |
| ↪️ | 重做 | 恢复撤销 |
| 🗑️ | 清空 | 清除所有笔迹 |
| 💾 | 保存 | 保存为 PNG |
| ➖ | 最小化 | 收起工具栏 |
| ⚙️ | 设置 | 打开设置窗口 |

## 开发指南

### Jalium.UI 约定

本项目基于 Jalium.UI 框架，**不是 WPF / WinUI / Avalonia**。开发时请遵循：

- 使用 JALXAML（`.jalxaml`）声明 UI，而非 XAML
- 命名空间：`https://schemas.jalium.dev/jalxaml/presentation`
- 启动顺序：`ThemeLoader.Initialize()` → `new Application()` → `app.Run(window)`
- 详见 [AGENTS.md](AGENTS.md)

### 设计规范

UI 实现需遵循 [Design.MD](Design.MD) 中的设计基线，包括窗口分层、Fluent 对齐、控件语义等规则。

## 路线图

- [x] 基础画笔/荧光笔/激光笔
- [x] 形状工具（直线/箭头/矩形/椭圆）
- [x] 文字标注
- [x] 撤销/重做
- [x] 保存截图
- [ ] 橡皮擦多模式（按点/按笔划切换）
- [ ] 全局快捷键
- [ ] 多页白板
- [ ] PPT 集成
- [ ] 自动更新

## 致谢

- [InkCanvasForClass Community Edition](https://github.com/InkCanvasForClass/community) — 设计灵感来源，感谢 CJK_mkp、doudou0720、PrefacedCorg 等贡献者
- [Jalium.UI](https://github.com/VeryJokerJal/Jalium.UI) — GPU 加速 UI 框架
- [Inno Setup](https://jrsoftware.org/isinfo.php) — 安装包打包工具

## 许可证

MIT License
