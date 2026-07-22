# macOS DMG 与 standalone Host 验收

适用范围：macOS 14+、Apple Silicon arm64。

## 自动候选验收

```bash
./scripts/build-macos-packages.sh --version <X.Y.Z> --adhoc
./tests/packaging/verify-macos-packages.sh \
  --directory ./artifacts/release/macos-arm64 \
  --version <X.Y.Z> \
  --expected-signing adhoc
```

验证器挂载 DMG，确认 `Snaploom.app` 与 `/Applications` 链接、50 MB 上限、Bundle ID、版本、
macOS 14 minOS、本地化屏幕录制用途、图标和包内 GPL/NOTICE/SBOM。所有 Mach-O 必须仅含 arm64。
Desktop 内嵌的 `Snaploom Capture Host.app` 与 standalone Host ZIP 中的 bundle 必须逐文件相同；
随后真实启动 Desktop 并在临时位置完成安装/卸载。候选包必须是 ad hoc 且未公证。

## 稳定未签名验收

稳定 runner 使用 `--stable-unsigned`，对 Host、Desktop 与 DMG 施加无发布者身份的 ad hoc 结构签名，
不读取 Developer ID 证书、不提交 Apple 公证，也不执行 stapling。验证器要求 metadata 明确记录
`adHocSignatureVerified=true`、`notarized=false` 与 `gatekeeperWarning=true`，并继续验证架构、包边界、
安装和启动。Release notes 必须披露 Gatekeeper 风险，并提供 SHA256SUMS 与 GitHub attestations。

## 非阻塞人工真机建议

如自愿执行 macOS 14+ Apple Silicon 人工验证，建议覆盖屏幕录制权限缺失/恢复、Gatekeeper、单双屏、
4K/5K Retina、混合 DPI、热插拔、睡眠唤醒、复制/保存与失败恢复。Hosted runner 的包验证不能冒充
带真实交互 Desktop、SCK 和 WebView compositor 的 PERF-01/QA-03 证据；缺少这些人工证据不阻止
稳定 RC 或 stable publish。
