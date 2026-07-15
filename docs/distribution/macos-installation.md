# macOS 安装、签名与公证

## 用户安装

Snaploom v1 支持 macOS 14 及更高版本的 Apple Silicon Mac，不包含 Intel 或 Universal 版本。

1. 打开 `snaploom-<版本>-macos-arm64.dmg`。
2. 将 `Snaploom.app` 拖到 DMG 内的 `Applications` 快捷方式。
3. 从“应用程序”启动 Snaploom。应用仅显示菜单栏图标，不创建主窗口。
4. 首次截图时，根据系统提示授予“屏幕与系统音频录制”权限；若此前拒绝，可在“系统设置 → 隐私与安全性 → 屏幕与系统音频录制”中重新开启。
5. 授权后如系统要求，请退出并重新启动 Snaploom，再按 `Command+Shift+A` 截图。

卸载时退出 Snaploom，然后从“应用程序”删除 `Snaploom.app`。用户配置和日志保留在当前用户的 Snaploom 配置目录中，便于升级或问题排查。

## CI 临时签名包

普通 CI 使用显式 `--adhoc` 模式生成可自动安装、启动和卸载验证的 DMG：

```bash
./scripts/build-macos-dmg.sh --version 1.0.0 --adhoc
./tests/packaging/verify-macos-dmg.sh \
  --dmg-directory ./artifacts/macos-arm64/dmg \
  --version 1.0.0 \
  --expected-signing adhoc
```

ad hoc 包只用于持续集成，不得作为正式版本发布。它使用单独的权限清单关闭库验证，以解决临时签名没有 Team ID 的限制；正式包保持库验证开启。

## Developer ID 正式包

正式构建只接受 `Developer ID Application` 身份，并强制同时执行 Apple 公证与 stapling；脚本不会在凭据缺失或公证失败时降级成 ad hoc 包。

运行前需将证书导入构建机钥匙串，并提供以下环境变量：

- `APPLE_NOTARY_KEY_PATH`：App Store Connect API 私钥文件路径。
- `APPLE_NOTARY_KEY_ID`：API Key ID。
- `APPLE_NOTARY_ISSUER_ID`：API Issuer ID。

构建命令：

```bash
./scripts/build-macos-dmg.sh \
  --version 1.0.0 \
  --developer-id "Developer ID Application: <组织名称> (<TEAM_ID>)" \
  --notarize

./tests/packaging/verify-macos-dmg.sh \
  --dmg-directory ./artifacts/macos-arm64/dmg \
  --version 1.0.0 \
  --expected-signing developer-id
```

输出目录仅包含 DMG、SHA256 文件和不含证书名称、密码、私钥路径的 JSON 元数据。DMG 大小上限为 `50,000,000` 字节，超限即构建失败。
