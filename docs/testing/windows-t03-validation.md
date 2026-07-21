# T03 Windows 最小截图链路真机验收

适用范围：Windows 10 22H2 x64 与 Windows 11 x64，普通用户权限。

## 自动化验证

在 Windows 仓库根目录执行：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm run check
cargo test --manifest-path product/Cargo.toml --workspace --all-features --locked
cargo clippy --manifest-path product/Cargo.toml --workspace --all-targets --all-features --locked -- -D warnings
cargo run --manifest-path Cargo.toml --locked --bin windows-bindings -- --check
pnpm run tauri:desktop
pnpm run tauri:host
```

GitHub Actions 的 `Windows x64` job 只验证无交互环境可执行的编译、合同测试、manifest、bindings、供应链和打包入口；不得把 hosted runner 冒充为真实 WGC、overlay、剪贴板或原生对话框证据。每次真机执行把 OS build、GPU/驱动、显示器/DPI/HDR、应用 commit、原始样本和结果写入 `docs/testing/evidence/<date>-windows-<machine>/`。

## 快捷键与捕获

1. 以普通用户启动 Snaploom，确认没有主窗口，通知区域出现 Snaploom 图标。
2. 在其他应用位于前台时按 `Alt+Shift+A`，确认只覆盖鼠标所在显示器。
3. 连续再次按快捷键，确认不会叠加截图会话或闪烁工具栏。
4. 核对捕获画面与触发瞬间一致，且不包含鼠标指针。
5. 在 100%、125%、150%、175%、200% 缩放下重复捕获，确认物理尺寸、逻辑布局与选区边界一致。
6. 覆盖双屏负原点、混合 DPI、主屏切换、旋转、热插拔；拓扑变化必须失败或完整重试，不能混用新旧 scale。
7. 覆盖普通窗口、跨屏、最小化、tool/menu/tooltip、cloaked、透明、click-through、系统 UI 和 Snaploom 自身窗口，核对 Z 序与过滤。
8. 分别在 SDR 与 Windows HDR 下核对白点、高光不过度裁切、最终 PNG 为 RGBA8/sRGB。

## 框选与保存

1. 从任意方向拖出至少 `8 × 8` 物理像素的选区。
2. 尝试更小选区，确认不能保存；随后重新拖动，确认可以继续截图。
3. 点击“保存 PNG”，确认出现 Windows 原生另存为对话框，且只允许 PNG。
4. 取消保存，确认原选区与会话仍保留。
5. 再次保存，确认成功后浮层退出，Snaploom 继续常驻托盘。
6. 打开 PNG，核对尺寸等于选区物理像素、颜色为 8 位 sRGB，且没有鼠标指针。
7. 使用另一个进程占用剪贴板，核对最多 5 次、每次 20 ms 的有限重试；释放竞争后从注册 `PNG` 格式读回原字节，并与保存结果逐字节比较。

## 安全边界与恢复

1. 在锁屏、UAC 安全桌面或受保护内容场景尝试触发截图。
2. 确认 Snaploom 不请求管理员权限、不绕过系统保护，并显示可理解的失败信息。
3. 制造不可写保存位置，确认浮层内提示保存失败，选区仍可继续保存。
4. 按 `Esc` 或点击退出，确认像素缓冲被释放，再次按快捷键可以创建新会话。
5. 验证快捷键冲突、睡眠/唤醒重注册、当前用户开机启动和失败通知，确认旧设置与可用注册不被破坏。

## 性能与资源

1. 真实默认快捷键至少 30 次，按 nearest-rank 记录 callback 到 overlay 可交互的 P50/P95/max，要求 P95 `≤ 150 ms`。
2. 真实 4K 捕获记录显示器解析、WGC 首帧、GPU copy/tone-map、窗口枚举、WebView ready；4K 编辑帧 P95 `≤ 16.667 ms`，PNG P95 `≤ 1,000 ms`。
3. tray-only 空闲 working set `≤ 100,000,000 bytes`；至少 20 次复制/保存成功循环，尾段 private memory 增长 `≤ 1%`，并核对 COM、D3D、frame event、power/shortcut registration 与 overlay 数量稳定。

## 双平台一致性

对照 [`macos-t02-validation.md`](macos-t02-validation.md) 执行相同的最小路径：默认快捷键 → 当前显示器 → 矩形选区 → 取消保存恢复 → PNG 保存退出。平台差异只应出现在快捷键、权限与系统对话框。
