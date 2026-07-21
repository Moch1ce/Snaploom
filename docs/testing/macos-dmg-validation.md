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

## 稳定签名与公证

稳定 runner 使用 `--developer-id <IDENTITY> --notarize`。脚本从 Host 开始执行 hardened runtime
Developer ID 签名，提交 Host 公证并 staple，再把同一个 Host bundle 嵌入 Desktop；最后签署 Desktop
与 DMG、提交 DMG 公证、staple，并执行 `codesign --verify --deep --strict`、`stapler validate` 和
`spctl`。任何凭据、签名、公证或 Gatekeeper 步骤失败都停止，不能降级为 ad hoc。

## 真机证据

macOS 14+ Apple Silicon 的 `Release qualification` 必须覆盖屏幕录制权限缺失/恢复、Gatekeeper、
单双屏、4K/5K Retina、混合 DPI、热插拔、睡眠唤醒、复制/保存与失败恢复。Hosted runner 的包验证
不能替代带真实交互 Desktop、SCK 和 WebView compositor 的 PERF-01/QA-03 证据。
