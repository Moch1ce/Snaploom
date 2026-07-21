# macOS arm64 Adapter 验收矩阵

适用范围：macOS 14 及更高版本、Apple Silicon。实现入口为
`product/platform/macos`，只使用 Rust 与 `objc2` framework bindings；发布物不得包含
Swift bridge、项目自有 dylib、私有 API 或 Accessibility 权限。

## 自动化验证

在仓库根目录执行：

```bash
PATH="/opt/homebrew/bin:$HOME/.rustup/toolchains/1.97.1-aarch64-apple-darwin/bin:/usr/bin:/bin" \
  cargo test --manifest-path product/Cargo.toml --workspace --all-features
PATH="/opt/homebrew/bin:$HOME/.rustup/toolchains/1.97.1-aarch64-apple-darwin/bin:/usr/bin:/bin" \
  cargo clippy --manifest-path product/Cargo.toml --workspace --all-features --all-targets -- -D warnings
PATH="/opt/homebrew/bin:$HOME/.rustup/toolchains/1.97.1-aarch64-apple-darwin/bin:/usr/bin:/bin" \
  cargo build --manifest-path product/Cargo.toml --release --locked
```

默认测试不会请求 TCC 权限，也不会写入系统通用剪贴板。仓库保留的 ignored SCK 测试只用于
开发期诊断；`cargo test` 生成的临时测试进程不具备 `com.snaploom.app` 的固定 bundle identity，
因此它的结果不得作为 TCC 或 RC 验收证据。真实捕获必须从已安装且稳定签名的 Snaploom
Capture Host 触发，并记录应用版本、签名身份和系统版本。

## 首次授权与撤销竞态

1. 安装使用固定 bundle ID 和稳定签名身份的 Snaploom，不用每次变化的 `cargo run` 代替。
2. 启动后确认只执行 `CGPreflightScreenCaptureAccess`，没有自动弹出系统权限框。
3. 从产品权限引导明确点击“授权”，确认此时才调用 `CGRequestScreenCaptureAccess`。
4. 若系统要求重启，退出并重新启动；不要循环请求。
5. 捕获进行中撤销“屏幕与系统音频录制”权限，确认结果映射为
   `PERMISSION_REVOKED`，而非包含 `NSError` 原文的内部错误。

## SCScreenshotManager 与像素合同

1. 把鼠标移动到目标显示器，再触发 `Command+Shift+A`。
2. 确认只捕获鼠标所在显示器，捕获完成后才显示浮层，帧内没有鼠标指针。
3. 对已知四角颜色图检查方向：输出必须为 top-left、y-down、premultiplied
   BGRA8888/sRGB，不能上下翻转或通道交换。
4. 用非紧凑 stride 样例确认逐行复制会去除 padding；异常 pixel format、空 image
   buffer、尺寸或 stride 不匹配必须失败。
5. 在 HDR/EDR 显示器检查系统 SDR tone mapping；macOS 14 路径不得调用 macOS 15
   才可用的 dynamic-range setter。

## Retina、多屏、Spaces 与窗口候选

1. 覆盖 1x/2x、左右/上下排列、负原点和主副屏不同 scale；浮层必须精确覆盖目标
   `NSScreen.frame`，包括菜单栏和 Dock。
2. 热插拔或改变排列发生在捕获期间时，第一次结果必须作废并最多重试一次。
3. 在普通 Space 和全屏 Space 验证浮层使用 screen-saver level、
   `canJoinAllSpaces`、`fullScreenAuxiliary`，且 WebView 仍为不透明。
4. 确认候选顺序来自 `CGWindowListCopyWindowInfo` 的前到后顺序，几何来自 SCK，
   自身 PID、系统 UI、非零 layer、离屏、透明和无效窗口被排除。
5. 公共 API 无法可靠识别所有跨进程 click-through 窗口；该项保留为已知能力缺口，
   不得以私有 CGS、WindowServer 或 Accessibility 权限规避。

## 剪贴板、保存和系统 Shell

1. 对随机 PNG bytes 执行 `NSPasteboard` 写入并立刻读回，必须逐字节相等；失败时保留会话。
2. 打开 app-modal 原生保存对话框前隐藏浮层；取消后恢复原会话，写入失败也恢复；成功后
   使用原子重命名完成并退出会话。
3. 默认快捷键为 `Command+Shift+A`。替换快捷键时先注册新值，再注销旧值；冲突或 rollback
   失败时旧值继续有效。
4. 睡眠唤醒后通过 `NSWorkspaceDidWakeNotification` 重新确认快捷键，失败只显示稳定通知。
5. `SMAppService.mainAppService` 注册/注销后必须读回 status；
   `RequiresApproval` 映射为需要用户批准，不得改用 LaunchAgent autostart plugin。

## 发布物检查

- `LSMinimumSystemVersion=14.0`、`LSUIElement=true`、本地化
  `NSScreenCaptureUsageDescription` 存在。
- Capture Host 与 Desktop 均为 arm64 Mach-O；不存在项目自有 Swift dylib、旧 bridge 符号、
  `CGWindowListCreateImage`、`CGDisplayCreateImage`、透明 WebView 或 `macOSPrivateApi=true`。
- 真实 TCC、Retina/多屏、Spaces、pasteboard 和 `SMAppService` 结果记录到 RC 验收证据，
  hosted runner 的编译与单元测试不能替代这些真机结论。

## PERF-01 真机记录

以下结果必须由同一个已安装、稳定签名的 RC 采集；#45 的源码/hosted CI 结果不能代填：

| 场景 | 样本/门限 | RC 证据 |
| --- | --- | --- |
| 快捷键到浮层可见 | 30 次，P95 ≤ 150 ms | 待签名 RC |
| 空闲 physical footprint | ≤ 100 MB | 待签名 RC |
| 连续捕获资源稳定性 | 20 次后尾部增长 ≤ 1% | 待签名 RC |
| 分段耗时 | 1x、Retina 4K、混合 Retina | 待签名 RC |
| completion 竞态 | timeout、late callback、取消、退出 | 待签名 RC |
