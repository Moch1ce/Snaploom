# 当前 Rust/Tauri macOS ScreenCaptureKit 单帧烟雾证据

状态：部分真机证据，不等同于 `QA-03` 或 `PERF-01` qualification。

## 证据身份

- 时间：`2026-07-22T08:02:55+08:00`
- Commit：`71ed793fea27ce9e3eed353bc880389834fbd215`
- 主机：Mac mini，Apple M4，24 GB 内存
- 系统：macOS 26.5（Darwin 25.5.0），`arm64`
- 会话：UID 501 普通用户，同时是 `/dev/console` 的交互用户
- 显示器：单屏 `1920×1080 @ 75 Hz`
- 工作树：执行前后无已跟踪文件改动

没有在证据中记录序列号、硬件 UUID、截图像素、窗口标题、路径或剪贴板内容。

## 执行入口

从仓库根目录对默认忽略的真实平台测试显式 opt-in：

```sh
SNAPLOOM_RUN_SCREEN_CAPTURE_INTEGRATION=1 \
cargo test --manifest-path product/Cargo.toml --locked \
  -p snaploom-platform-macos captures_one_real_sck_frame \
  -- --ignored --nocapture
```

真实 ScreenCaptureKit 单帧测试：通过。`captures_one_real_sck_frame` 在 0.69 秒内完成，断言当前
Rust `MacPlatform` 返回非零物理尺寸、`BGRA8_PREMULTIPLIED` 帧，并且缓冲区长度严格等于
`stride × physical_height`。该入口没有使用 fake platform、fixture、headless browser 或迁移前
.NET/Avalonia 实现。

## 证据边界

这项结果仅证明当前 commit 能在一台已授权的 Apple Silicon 交互桌面上通过真实
ScreenCaptureKit 单帧路径。它不能替代以下门禁，因此不关闭 #41、#45 或 #51：

- 已安装 Desktop/Capture Host 身份下的 TCC 首次授权、拒绝、撤销与恢复；
- Retina/5K、双屏、混合缩放、Spaces、热插拔和睡眠唤醒；
- WKWebView 中简中/英文 IME 候选窗、原生保存对话框与 NSPasteboard；
- 真实 4K compositor、30/20 样本性能与资源稳定性；
- 覆盖升级、卸载、Gatekeeper、Developer ID、notarization 与 stapling；
- Windows 10/11 x64 qualification 及三平台精确资格集合。
