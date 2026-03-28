[English](./README.md) | [简体中文](./README.zh-CN.md)

# AddinManager

A plugin management and debugging tool for `Autodesk Revit`, designed to quickly load external assemblies, select commands/applications, and generate `.addin` manifests.

<img width="566" height="513" alt="image" src="https://github.com/user-attachments/assets/aef2058d-b1ec-46e5-b807-2c69ac2d7057" />

## Features

- Load and analyze assemblies containing `IExternalCommand` / `IExternalApplication`
- Three execution modes: `Manual` / `Faceless` / `ReadOnly`
- Export manifests based on checked items:
  - Save locally (same directory as the assembly)
  - Save to the Revit global add-in directory (All Users)
- WPF UI capabilities:
  - Command tree search, bulk select, expand/collapse
  - Context menu (run, reload, open file location, assembly info)
- Debug tools:
  - Log viewer (checkbox filters by level/source)
  - Dependency graph window
- Multi-language support: `zh-CN` / `en-US` / `ja-JP`

## Tech Stack

- `.NET Framework 4.8`
- `C#` (project `LangVersion` is set to `preview`)
- `WPF`
- `Autodesk Revit API` (via `Nice3point.Revit.Api.*` NuGet packages)

## Requirements

- Windows x64
- Visual Studio (supports SDK-style `.csproj` and `.NET Framework 4.8`)
- Revit installed (required for actual loading and execution)

## Build Locally

```powershell
# Run in repository root
dotnet restore .\AddInManager\AddInManager.csproj
dotnet build .\AddInManager\AddInManager.csproj -c Release
```

Default output path: `AddInManager\bin\Release\`

## Installation

### 1) Inno Setup

Installer script is provided at repository root: `AddinManager.iss`.

### 2) Manual deployment (Bundle)

Copy all files under `AddInManager\bin\Release\` to:

`C:\ProgramData\Autodesk\ApplicationPlugins\RevitAddinManager.bundle`

## Usage

After launching Revit, you can find the `AddinManager` dropdown in the Ribbon, including:

- `Manual Mode`
- `Faceless Mode`
- `ReadOnly Mode`
- `Debug Log Viewer`
- `Dependency Analyzer`

## Configuration and Data

At runtime, an `AddinData` folder is created next to the plugin assembly. Main files include:

- `AimInternal.json`: loaded add-ins and checked states
- `ui-settings.json`: UI language settings

> Legacy `AimInternal.ini` is automatically migrated to `AimInternal.json` on first run.

## Revit Version Compatibility

`PackageContents.xml` currently declares support for Revit `2017 ~ 2022`.
To add more versions, update both:

- `AddInManager/PackageContents.xml`
- `AddInManager/Contents/<version>/RevitAddinManager.addin`

## Project Structure (Excerpt)

- `AddInManager/App.cs`: Revit startup entry and Ribbon creation
- `AddInManager/AIM.cs`: core execution flow and command dispatch
- `AddInManager/AddinManager.cs`: add-in loading, persistence, manifest export
- `AddInManager/Wpf/MainWindow.xaml(.cs)`: main UI
- `AddInManager/Wpf/LogViewerWindow.xaml(.cs)`: log viewer
- `AddInManager/DebugTools/*`: debugging and analysis features

## License

MIT license
