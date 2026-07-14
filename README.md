# Snaploom

Snaploom 是一款面向 Windows 和 macOS 的轻量桌面截图与标注工具。

v1 聚焦窗口吸附、矩形框选、矩形、箭头、文字、马赛克、复制和 PNG 保存，使用 .NET 10 与 Avalonia 构建。

完整需求与技术决策参见 [`docs/snaploom-v1-spec.md`](docs/snaploom-v1-spec.md)。

## 当前状态

项目目前处于基础骨架阶段。应用启动后仅显示系统托盘/菜单栏图标，不创建主窗口；“开始截图”入口暂时禁用，“退出”可正常结束进程。

## 项目结构

- `src/Snaploom.App`：Avalonia 桌面入口与应用生命周期。
- `src/Snaploom.Core`：与 UI、操作系统无关的领域逻辑。
- `src/Snaploom.Rendering`：跨平台渲染能力。
- `src/Snaploom.Platform.Abstractions`：平台能力接口。
- `src/Snaploom.Platform.Windows`：Windows 平台实现。
- `src/Snaploom.Platform.MacOS`：macOS 平台实现。
- `tests`：核心、渲染与集成测试。

`Snaploom.Core` 和 `Snaploom.Rendering` 不直接访问 Win32、WinRT、AppKit 或 ScreenCaptureKit；原生能力必须通过 `Snaploom.Platform.Abstractions` 暴露。

## 本地开发

需要安装 .NET SDK 10.0.301 或符合 [`global.json`](global.json) 回滚策略的更新补丁版本。

```bash
dotnet restore Snaploom.sln
dotnet build Snaploom.sln --configuration Release --no-restore
dotnet test Snaploom.sln --configuration Release --no-build
dotnet run --project src/Snaploom.App/Snaploom.App.csproj
```

运行后可从 Windows 系统托盘或 macOS 菜单栏退出 Snaploom。

在 Apple Silicon Mac 上执行以下命令时，发布目标会生成带 `com.snaploom.app` Bundle ID 的 `Snaploom.app`：

```bash
dotnet publish src/Snaploom.App/Snaploom.App.csproj \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true
```

应用包位于 `bin/Release/net10.0/osx-arm64/publish/bundle/Snaploom.app`。

依赖许可证审计见 [`docs/licenses/dependency-licenses.md`](docs/licenses/dependency-licenses.md)。
