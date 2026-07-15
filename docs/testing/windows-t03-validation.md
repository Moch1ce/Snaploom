# T03 Windows 最小截图链路真机验收

适用范围：Windows 10 22H2 x64 与 Windows 11 x64，普通用户权限。

## 自动化验证

在 Windows 仓库根目录执行：

```powershell
dotnet restore Snaploom.sln
dotnet build Snaploom.sln -c Release --no-restore
dotnet test Snaploom.sln -c Release --no-build
```

Windows CI 会通过公共 `IScreenCaptureService` 调用 Windows Graphics Capture，验证当前显示器能够进入统一 `CapturedFrame`；无头环境不弹出保存对话框。

## 快捷键与捕获

1. 以普通用户启动 Snaploom，确认没有主窗口，通知区域出现 Snaploom 图标。
2. 在其他应用位于前台时按 `Alt+Shift+A`，确认只覆盖鼠标所在显示器。
3. 连续再次按快捷键，确认不会叠加截图会话或闪烁工具栏。
4. 核对捕获画面与触发瞬间一致，且不包含鼠标指针。
5. 在 100%、125%、150%、175%、200% 缩放下重复捕获，确认物理尺寸、逻辑布局与选区边界一致。

## 框选与保存

1. 从任意方向拖出至少 `8 × 8` 物理像素的选区。
2. 尝试更小选区，确认不能保存；随后重新拖动，确认可以继续截图。
3. 点击“保存 PNG”，确认出现 Windows 原生另存为对话框，且只允许 PNG。
4. 取消保存，确认原选区与会话仍保留。
5. 再次保存，确认成功后浮层退出，Snaploom 继续常驻托盘。
6. 打开 PNG，核对尺寸等于选区物理像素、颜色为 8 位 sRGB，且没有鼠标指针。

## 安全边界与恢复

1. 在锁屏、UAC 安全桌面或受保护内容场景尝试触发截图。
2. 确认 Snaploom 不请求管理员权限、不绕过系统保护，并显示可理解的失败信息。
3. 制造不可写保存位置，确认浮层内提示保存失败，选区仍可继续保存。
4. 按 `Esc` 或点击退出，确认像素缓冲被释放，再次按快捷键可以创建新会话。

## 双平台一致性

对照 [`macos-t02-validation.md`](macos-t02-validation.md) 执行相同的最小路径：默认快捷键 → 当前显示器 → 矩形选区 → 取消保存恢复 → PNG 保存退出。平台差异只应出现在快捷键、权限与系统对话框。
