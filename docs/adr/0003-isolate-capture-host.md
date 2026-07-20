# 通过独立 Capture Host 隔离截图实现

Capture SDK 只提供稳定的宿主语言接口，并通过版本化本地 IPC 启动和调用独立的 Tauri Capture Host；不把 WebView、截图权限或交互界面加载进宿主进程。该结构隔离宿主框架、崩溃、权限与升级周期，同时让 `Apache-2.0` SDK 与 `GPL-3.0-or-later` Capture Host 保持清晰的发布边界。
