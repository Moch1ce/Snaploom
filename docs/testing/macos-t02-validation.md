# T02 macOS 最小截图链路真机验收

适用范围：macOS 14 及更高版本、Apple Silicon。

## 自动化验证

在仓库根目录执行：

```bash
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" build Snaploom.sln -c Release
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" test tests/Snaploom.Core.Tests/Snaploom.Core.Tests.csproj -c Release --no-build
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" test tests/Snaploom.Rendering.Tests/Snaploom.Rendering.Tests.csproj -c Release --no-build
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" test tests/Snaploom.IntegrationTests/Snaploom.IntegrationTests.csproj -c Release --no-build
```

集成测试不会主动弹出屏幕录制授权。只有系统已授权时，才会实际调用 ScreenCaptureKit 并检查统一像素帧。

## 首次授权

1. 启动 Snaploom，确认没有主窗口，菜单栏出现 Snaploom 图标。
2. 在其他应用位于前台时按 `Command+Shift+A`，确认 Snaploom 显示权限用途说明。
3. 点击“继续授权”，在 macOS 系统提示中允许 Snaploom 录制屏幕。
4. 如果授权未立即生效，点击“打开系统设置”，确认进入“隐私与安全性 > 屏幕与系统音频录制”。
5. 按引导退出并重新启动 Snaploom。

## 截图主路径

1. 把鼠标移到待截图显示器，在其他应用位于前台时按 `Command+Shift+A`。
2. 确认只覆盖鼠标所在显示器，画面是触发瞬间的可见像素，且不包含鼠标指针。
3. 连续再按几次快捷键，确认不会叠加新的截图会话或闪烁工具栏。
4. 从任意方向拖出选区，确认选区外有深色遮罩，选区显示真实物理像素尺寸。
5. 尝试小于 `8 × 8` 物理像素的选区，确认不能保存；随后重新拖动，确认可以再次选中。
6. 选择有效区域，点击“保存 PNG”，确认出现 macOS 原生保存对话框，文件名形如 `Snaploom_2026-07-14_12-34-56.png`。
7. 取消保存，确认原选区和截图会话仍保留。
8. 再次保存，确认成功后浮层退出，Snaploom 仍常驻菜单栏。
9. 打开 PNG，核对尺寸等于选区物理像素尺寸、颜色正常、没有鼠标指针。

## 失败与恢复

1. 在系统设置中关闭 Snaploom 的屏幕录制权限，再触发截图。
2. 确认出现可理解的权限提示，并能直接打开对应系统设置页面。
3. 制造不可写的保存位置或移除目标目录，确认浮层内显示保存失败，选区仍可继续保存。
4. 按 `Esc` 或点击“退出”，确认像素缓冲区被释放，再次按快捷键可创建新会话。

## 发布包检查

```bash
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" publish \
  src/Snaploom.App/Snaploom.App.csproj \
  -c Release -r osx-arm64 --self-contained true
```

确认 `Snaploom.app/Contents/MacOS` 中同时存在 `Snaploom.App` 与 `libSnaploomMacOS.dylib`，`Info.plist` 的最低系统版本为 `14.0`，应用以菜单栏代理模式运行。
