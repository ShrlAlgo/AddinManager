[English](./README.md) | [简体中文](./README.zh-CN.md)

# AddinManager

用于 `Autodesk Revit` 的插件管理与调试工具，支持快速加载外部程序集、选择命令/应用并生成 `.addin` 清单。

<img width="566" height="513" alt="image" src="https://github.com/user-attachments/assets/aef2058d-b1ec-46e5-b807-2c69ac2d7057" />

## 功能概览

- 支持加载并解析包含 `IExternalCommand` / `IExternalApplication` 的程序集
- 三种运行模式：`Manual` / `Faceless` / `ReadOnly`
- 支持按勾选项导出清单：
  - 保存到本地（程序集同目录）
  - 保存到 Revit 全局插件目录（All Users）
- WPF 图形界面能力：
  - 命令树搜索、批量选择、展开/折叠
  - 右键菜单（运行、重载、定位文件、程序集信息）
- 调试工具：
  - 日志查看器（按级别/来源复选框筛选）
  - 依赖关系图窗口
- 多语言：`zh-CN` / `en-US` / `ja-JP`

## 技术栈

- `.NET Framework 4.8`
- `C#`（项目中设置为 `preview`）
- `WPF`
- `Autodesk Revit API`（通过 `Nice3point.Revit.Api.*` NuGet 包）

## 环境要求

- Windows x64
- Visual Studio（支持 SDK-style `.csproj` 与 `.NET Framework 4.8`）
- 已安装 Revit（用于实际加载与运行）

## 本地构建

```powershell
# 在仓库根目录执行
dotnet restore .\AddInManager\AddInManager.csproj
dotnet build .\AddInManager\AddInManager.csproj -c Release
```

构建产物默认位于：`AddInManager\bin\Release\`

## 安装

### 1) 使用 Inno Setup

仓库根目录提供安装脚本：`AddinManager.iss`。

### 2) 手动部署（Bundle）

将 `AddInManager\bin\Release\` 下内容复制到：

`C:\ProgramData\Autodesk\ApplicationPlugins\RevitAddinManager.bundle`

## 使用说明

启动 Revit 后，在功能区可看到 `AddinManager` 下拉菜单，包含：

- `Manual Mode`
- `Faceless Mode`
- `ReadOnly Mode`
- `Debug Log Viewer`
- `Dependency Analyzer`

## 配置与数据

运行时会在插件目录下生成 `AddinData` 文件夹（与程序集同级），主要文件：

- `AimInternal.json`：已加载插件与勾选状态
- `ui-settings.json`：界面语言设置

> 旧版 `AimInternal.ini` 会在首次运行时自动迁移到 `AimInternal.json`。

## Revit 版本兼容

`PackageContents.xml` 当前声明的 Revit 运行范围为 `2017 ~ 2022`。
如需扩展版本，请同步调整：

- `AddInManager/PackageContents.xml`
- `AddInManager/Contents/<版本>/RevitAddinManager.addin`

## 目录结构（节选）

- `AddInManager/App.cs`：Revit 启动入口与 Ribbon 创建
- `AddInManager/AIM.cs`：核心执行流程与命令调度
- `AddInManager/AddinManager.cs`：插件加载、持久化、清单导出
- `AddInManager/Wpf/MainWindow.xaml(.cs)`：主界面
- `AddInManager/Wpf/LogViewerWindow.xaml(.cs)`：日志查看器
- `AddInManager/DebugTools/*`：调试与分析功能

## 许可证

MIT license
