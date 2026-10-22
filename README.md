# Space.Map

<p align="center">
  <img src="src/Space.Map.App/Assets/space.map-logo.png" width="96" height="96" alt="Space.Map Logo" />
</p>

<p align="center">
  <strong>Fast, Modern, Fluent Windows Disk Space Visualizer</strong><br>
  现代化 Windows 磁盘空间占用可视化与分析工具
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-purple.svg" alt=".NET 10.0">
  <img src="https://img.shields.io/badge/platform-Windows-blue.svg" alt="Platform: Windows">
</p>

---

[English](#features) | [简体中文](#功能特性)

## Features

- **Disk Overview**: Instant list of local fixed drives with usage progress bars.
- **Squarified Treemap**: Interactive, responsive block maps based on Squarified algorithm to explore disk footprints.
- **Drill-Down & Breadcrumbs**: Click to navigate down nested directories and easily backtrack via breadcrumbs.
- **Top 100 Large Files**: Built-in bounded min-heap tracks the largest files during scanning without full-sort memory overhead.
- **Progressive Scanning**: Top-level subtrees report live growing sizes during background scanning.
- **Resilient DFS Walk**: Explicit iterative post-order stack prevents stack overflow on deeply nested paths.
- **Cycle & Junction Safety**: Windows directory identity deduping avoids infinite loop traps in directory reparse points.
- **Localization (i18n)**: Out-of-the-box support for English (`en-US`) and Simplified Chinese (`zh-CN`).
- **Modern Fluent UI**: Powered by [WPF-UI](https://github.com/lepoco/wpfui) with dark/light theme switching.

---

## 功能特性

- **磁盘概览启动页**：启动即展示本机各磁盘卡片与已用空间进度条。
- **Squarified Treemap 矩形树图**：按占用空间直观呈现目录结构，直达大文件/大文件夹。
- **目录层级下钻与面包屑**：双击/单击层级下钻，随时通过面包屑快速返回。
- **Top 100 最大文件**：扫描过程中利用有界最小堆并发收集最大文件，不占用冗余内存。
- **渐进式扫描反馈**：扫描耗时目录时，一级子项即时刷新生长尺寸。
- **抗栈溢出 DFS 遍历**：采用显式栈后序迭代 DFS，杜绝深层目录导致的调用栈溢出（Stack Overflow）。
- **软链接与重解析点防护**：通过 Windows 卷序列号与文件索引去重，安全避免符号链接死循环。
- **双语多语言（i18n）**：原生支持中文与英文无缝切换。
- **现代化 Fluent 设计**：基于 WPF-UI 实现 Windows 11 Fluent 风格与亮暗色主题自适应。

---

## Architecture / 项目架构

Space.Map follows the **Deep Module** design with clean separation of concerns:

```
space.map/
├── .github/                      # CI/CD workflows and issue templates
├── src/
│   ├── Space.Map.Core/           # Standalone domain engine (Zero WPF / Zero OS-PInvoke dependency)
│   │   ├── Scanning/             # Iterative DFS, fan-out engine, directory identity abstractions
│   │   ├── Layout/               # Squarified Treemap geometry layout engine
│   │   ├── Navigation/           # Hierarchical drill-down navigation state machine
│   │   ├── Abstractions/         # Deep interface contracts (IDriveService, IShellActions, IFolderPicker)
│   │   └── Formatting/           # ByteSizeFormatter
│   │
│   ├── Space.Map.Infrastructure/ # Windows OS platform & infrastructure adapters
│   │   ├── Platform/             # Win32 file identity handles, shell actions, folder dialogs
│   │   ├── Drives/               # Physical hardware drive enumeration & volume telemetry
│   │   └── Logging/              # Rolling file logging services
│   │
│   └── Space.Map.App/            # Modern Fluent WPF desktop application (Feature-based structure)
│       ├── Features/             # High locality: DriveHome, Workspace, Shell
│       ├── Controls/             # Custom DrawingContext canvases (TreemapCanvas, PieChartCanvas)
│       ├── Resources/            # i18n localization dictionaries (en-US, zh-CN)
│       └── Services/             # App theme & localization services
│
├── tests/
│   └── Space.Map.Core.Tests/     # xUnit & FluentAssertions test suite
├── Directory.Build.props         # Global MSBuild properties
├── Directory.Packages.props      # Central Package Management (CPM)
└── Space.Map.slnx                # Modern solution file
```

---

## Quick Start / 快速上手

### Requirements
- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build & Run
```powershell
# Clone the repository
git clone https://github.com/your-username/space.map.git
cd space.map

# Build the solution
dotnet build Space.Map.slnx

# Run all unit tests
dotnet test Space.Map.slnx

# Launch the application
dotnet run --project src/Space.Map.App/Space.Map.App.csproj
```

---

## Contributing / 参与贡献

See [CONTRIBUTING.md](CONTRIBUTING.md) for details on code style, testing, and PR submission guidelines.

---

## License / 开源许可

Distributed under the **MIT License**. See [LICENSE](LICENSE) for more information.
