# macOS DMG 发布验收

适用范围：macOS 14 及更高版本、Apple Silicon、Developer ID 正式包。

## 自动门禁

正式 DMG 必须通过：

```bash
./tests/packaging/verify-macos-dmg.sh \
  --dmg-directory ./artifacts/macos-arm64/dmg \
  --version <版本> \
  --expected-signing developer-id
```

脚本会核对 SHA256、50 MB 上限、`com.snaploom.app`、版本、应用图标、中英文屏幕录制权限说明、Hardened Runtime 权限、Developer ID 签名、公证票据、Gatekeeper、全包 Mach-O 架构，以及临时目录中的安装、启动和卸载。任何 Intel 或 Universal Mach-O 都会失败。

## 全新系统人工验收

在未安装过 Snaploom 的 macOS 14+ Apple Silicon 真机上执行：

1. 下载 DMG 与 `.sha256`，校验摘要一致。
2. 双击 DMG，确认卷内清楚显示 `Snaploom.app` 与 `Applications` 快捷方式。
3. 将应用拖入“应用程序”并启动，确认 Gatekeeper 不显示“来源不明”或“文件已损坏”。
4. 确认菜单栏名称与图标均为 Snaploom，应用版本与 DMG 版本一致。
5. 按 `Command+Shift+A`，确认系统展示本地化的屏幕录制用途说明。
6. 允许权限；按系统要求重启应用后再次截图，完成框选并复制或保存 PNG，确认图像正确。
7. 退出应用，从“应用程序”删除 `Snaploom.app`，确认应用已卸载且不再运行。
8. 检查发布产物和流水线日志，确认没有证书密码、私钥内容或个人目录路径。

人工验收记录应包含 macOS 版本、Mac 型号、DMG 字节数、SHA256、签名验证结果、公证验证结果和上述步骤结论；不得记录任何凭据。
