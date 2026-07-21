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

## 稳定签名模式

稳定 runner 在受保护环境提供 PFX/托管证书路径、密码和 RFC3161 URL，然后使用
`-SigningMode stable-signed`，并通过 `-SignTool` 传入已锁定 Windows SDK 的 x64
`signtool.exe` 绝对路径。脚本显式使用 `/fd SHA256 /td SHA256 /tr` 签署 Desktop、Host 和
installer，并以 `signtool verify /pa /all /v /tw` 与 `Get-AuthenticodeSignature` 复验。任一步失败
立即停止，不能回退为 unsigned。

## 真机证据

Windows 10 和 Windows 11 分别执行 `Release qualification`。证据必须来自普通用户交互桌面，覆盖
100%–200% 混合 DPI、单双屏/4K、热插拔、睡眠唤醒、快捷键冲突、复制/保存、失败恢复、安装/
覆盖升级/卸载，并确认不提权、不捕获 UAC 安全桌面。Windows Server hosted runner 的静态/安装验证
不能代替这两项真机记录。
