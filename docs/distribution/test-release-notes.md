> [!WARNING]
> 这是 Snaploom {{VERSION}} 测试版，不是经过平台商店或证书认证的正式发行版。

## 下载与 SHA256 校验

- Windows x64：`snaploom-{{VERSION}}-windows-x64-setup.exe`
- macOS Apple Silicon：`snaploom-{{VERSION}}-macos-arm64.dmg`
- 每个安装包旁边都有同名 `.sha256` 文件。请先校验摘要，再运行或安装。

Windows PowerShell：

```powershell
Get-FileHash .\snaploom-{{VERSION}}-windows-x64-setup.exe -Algorithm SHA256
```

macOS：

```bash
shasum -a 256 ./snaploom-{{VERSION}}-macos-arm64.dmg
```

## 未签名测试版提示

- Windows 安装包目前没有代码签名证书，Microsoft Defender SmartScreen 可能显示“未知发布者”。确认下载页是 `{{TAG}}` 且 SHA256 一致后，再选择继续运行。
- macOS DMG 仅使用 ad hoc 签名，没有 Apple Developer ID 认证、没有 Apple 公证，也没有 stapling。Gatekeeper 可能阻止首次打开；确认 SHA256 后，可在“系统设置 → 隐私与安全性”中选择“仍要打开”。
- ad hoc 签名只用于测试包完整性与本地运行验证，不能解释为“Apple 已认证”。

安装和手动放行细节见仓库中的 Windows、macOS 分发说明。以下更新记录由 GitHub 根据本版本标签自动生成。
