# 采用 Tauri 2 重构桌面应用与截图 Host

Snaploom 将以 Tauri 2、Rust 和 TypeScript/Canvas 取代现有 .NET/Avalonia 实现：Rust 承载截图领域逻辑、平台能力、IPC 与 SDK 基础库，WebView 只承载交互界面。相较 Qt 6 与 Flutter，该选择更符合纯开源授权、较小发布包和双平台原生 SDK 的目标；代价是重写 Windows 与 macOS 平台适配器，并重新建立功能等价测试。
