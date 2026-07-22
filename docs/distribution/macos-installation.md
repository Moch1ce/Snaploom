# macOS 未认证包安装

## 用户安装

Snaploom v1 支持 macOS 14 及更高版本的 Apple Silicon Mac，不包含 Intel 或 Universal 版本。

1. 打开 `snaploom-<版本>-macos-arm64.dmg`。
2. 将 `Snaploom.app` 拖到 DMG 内的 `Applications` 快捷方式。
3. 当前稳定包没有 Developer ID 证书且未经过 Apple 公证。首次双击可能被 Gatekeeper 拦截；确认 DMG 来自项目 GitHub Releases、GitHub attestation 有效且 SHA256 一致后，在“系统设置 → 隐私与安全性”中选择“仍要打开”。
4. 再次从“应用程序”启动 Snaploom。应用仅显示菜单栏图标，不创建主窗口。
5. 首次截图时，根据系统提示授予“屏幕与系统音频录制”权限；若此前拒绝，可在“系统设置 → 隐私与安全性 → 屏幕与系统音频录制”中重新开启。
6. 授权后如系统要求，请退出并重新启动 Snaploom，再按 `Command+Shift+A` 截图。

卸载时退出 Snaploom，然后从“应用程序”删除 `Snaploom.app`。用户配置和日志保留在当前用户的 Snaploom 配置目录中，便于升级或问题排查。

## 稳定未签名模式

稳定 RC 使用显式 `--stable-unsigned` 模式生成可自动安装、启动和卸载验证的 DMG：

```bash
./scripts/build-macos-packages.sh --version 1.0.0 --stable-unsigned
./tests/packaging/verify-macos-packages.sh \
  --directory ./artifacts/release/macos-arm64 \
  --version 1.0.0 \
  --expected-signing stable-unsigned
```

该模式只使用无发布者身份的 ad hoc 结构签名，不代表 Apple Developer ID 身份。Release 必须同时发布
SHA256SUMS、GitHub attestations 与上述 Gatekeeper 提示，不得描述为“Apple 已认证”或“已公证”。

## 可选的未来认证模式

如果未来取得 Apple Developer 账号与证书，可以另开 Issue 重新评估 Developer ID、公证和 stapling；
它们不属于当前稳定发布硬门禁。当前 workflow 不读取任何 Apple 证书或公证 secret。

输出目录只包含 DMG、standalone Host、checksum 和不含凭据的 JSON 元数据。DMG 大小上限为
`50,000,000` 字节，超限即构建失败。
