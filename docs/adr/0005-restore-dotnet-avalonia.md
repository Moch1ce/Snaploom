# 恢复 .NET/Avalonia 为当前产品实现

- 状态：已采纳
- 日期：2026-07-23
- 恢复基线：`dotnet-final`（`69782bc`）

## 决策

Snaploom 恢复并继续维护 `.NET 10 + Avalonia` 桌面实现。当前产品树以 `src/`、`tests/` 和 `Snaploom.sln` 为唯一构建入口；Windows 平台能力继续由 Win32/WinRT Adapter 承载，macOS 截图继续由 Swift/ScreenCaptureKit Bridge 承载。

当前产品不再加载 Tauri、Rust Capture Host、WKWebView/WebView2 或 TypeScript/Canvas 截图浮层。ADR 0002、0003 和 0004 对当前实现不再生效，只作为历史迁移记录保留。若未来重新引入独立 Capture SDK 或 Capture Host，必须通过新的 ADR 明确进程模型、许可证边界、性能门槛和分发方式。

## 原因

Tauri 截图链路需要把完整屏幕帧交付给 WebView，再初始化 Canvas 浮层。实际 macOS 调试中，这条路径产生可见的图片层切换，并且快捷键到浮层可见的单次实测约为 `736 ms`，未达到既有 `P95 ≤ 150 ms` 门槛。冻结的 .NET 实现在同一台 Mac mini M4 上已有 `P95 140.1554 ms` 的真实快捷键证据，并且不需要 WebView 首帧交付。

恢复现有基线比继续为 WebView 冷启动、二进制 IPC 和首帧合成增加平台特例更直接，也保留了已经验证的截图交互、原生权限、系统输入法和打包路径。

## 影响

- 恢复 `dotnet-final` 中的解决方案、产品源码、测试、打包脚本和 CI。
- 删除该标签之后引入的 Rust/Tauri/Web 前端产品树及其构建产物。
- `dotnet-final` 标签本身继续保持只读；当前开发通过新的提交前进，不移动或重建该标签。
- Tauri 迁移期间新增但依赖 Rust Capture Host 的 SDK 和发布流水线不进入当前 .NET 产品树。
