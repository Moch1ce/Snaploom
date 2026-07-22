# Windows 安装包与 standalone Host 验收

适用范围：Windows 10 22H2 build 19045+ 与 Windows 11 x64，普通用户。

## 自动候选验收

```powershell
./scripts/build-windows-packages.ps1 `
  -Version <X.Y.Z> `
  -SigningMode candidate-unsigned
./tests/packaging/verify-windows-packages.ps1 `
  -Directory ./artifacts/release/windows-x64 `
  -Version <X.Y.Z> `
  -ExpectedSigning candidate-unsigned
```

验证覆盖：x64 Rust/Tauri Desktop 与 Host、50 MB 上限、确定性 standalone Host ZIP、包内 GPL/
NOTICE/SBOM、Host 与 installer staging digest 一致、当前用户安装/Host 登记、Desktop 启动、覆盖升级、
卸载。候选 EXE/installer 必须明确未签名。

## 稳定未签名模式

稳定 runner 不读取证书或时间戳 secrets，使用 `-SigningMode stable-unsigned` 和
`-ExpectedSigning stable-unsigned`。脚本必须确认 Desktop、Host、installer 与 Capture SDK DLL 均未
签名，metadata 必须包含 `unknownPublisherWarning=true`；Release notes 和审批包必须说明未知发布者/
SmartScreen 风险，并提供逐文件 SHA-256、`SHA256SUMS` 与 GitHub attestation 验证方式。

## 非阻塞人工真机建议

如自愿在 Windows 10 和 Windows 11 执行人工验证，证据应来自普通用户交互桌面，覆盖 100%–200%
混合 DPI、单双屏/4K、热插拔、睡眠唤醒、快捷键冲突、复制/保存、失败恢复、安装/覆盖升级/卸载，
并确认不提权、不捕获 UAC 安全桌面。Windows Server hosted runner 的静态/安装验证不能冒充这两项
真机记录；缺少人工记录不阻止稳定 RC 或 stable publish。
