# Snaploom

Snaploom 是一款面向 Windows 和 macOS 的轻量桌面截图与标注工具。

v1 聚焦窗口吸附、矩形框选、矩形、箭头、文字、马赛克、复制和 PNG 保存，使用 .NET 10 与 Avalonia 构建。

当前产品实现已从 `dotnet-final` 基线恢复为 .NET/Avalonia。此前的 Tauri 2、Rust 与 TypeScript/Canvas 迁移不再是当前开发入口；决策背景见 [ADR 0005](docs/adr/0005-restore-dotnet-avalonia.md)。

完整需求与技术决策参见 [`docs/snaploom-v1-spec.md`](docs/snaploom-v1-spec.md)。

## 当前状态

项目已打通 Windows 与 macOS 的截图链路。应用启动后仅显示系统托盘/菜单栏图标，不创建主窗口；可通过默认全局快捷键或菜单进入当前显示器截图，框选和标注后复制或保存 PNG。

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

在非 Windows 开发机上交叉验证 Windows x64 目标时，需要显式启用 Windows TFM：

```bash
dotnet publish src/Snaploom.App/Snaploom.App.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:SnaploomTargetWindows=true
```

Windows 主机与 Windows CI 会自动选择 `net10.0-windows10.0.19041.0`。

Windows 用户级安装包在 Windows 主机上通过固定版本的 Inno Setup 构建：

```powershell
choco upgrade innosetup --version=6.7.1 --allow-downgrade --yes
pwsh -File scripts/build-windows-installer.ps1 -Version 1.0.0
```

安装包、SHA256 和体积元数据输出到 `artifacts/windows-x64/installer`。安装和 SmartScreen 校验说明见 [`docs/distribution/windows-installation.md`](docs/distribution/windows-installation.md)。

维护者推送 `vX.Y.Z` 标签后，测试版发布流水线会在 Windows x64 和 Apple Silicon runner 上重新执行完整测试，生成未签名 EXE、ad hoc DMG、对应 SHA256，并在全部验证成功后发布 GitHub prerelease。发布门槛、失败恢复和未来 Developer ID 升级步骤见 [`docs/distribution/test-release-process.md`](docs/distribution/test-release-process.md)。

依赖许可证审计见 [`docs/licenses/dependency-licenses.md`](docs/licenses/dependency-licenses.md)。

## 许可证与贡献

产品、Capture Host 与截图实现使用 `GPL-3.0-or-later`；公开 Capture SDK 与示例使用 `Apache-2.0`。机器可读的文件归属以 [`REUSE.toml`](REUSE.toml) 为准，详情见 [`LICENSE.md`](LICENSE.md)。贡献者需遵守 [`CONTRIBUTING.md`](CONTRIBUTING.md) 并以 DCO 1.1 签署每个 commit。
