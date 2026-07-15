# Windows 安装与校验

Snaploom v1 支持 Windows 10 22H2 x64 与 Windows 11 x64。Windows 版本只提供当前用户级 EXE 安装包，不需要管理员权限，也不提供便携版。

## 下载前验证

测试版暂未购买代码签名证书，因此 Windows Defender SmartScreen 可能显示“Windows 已保护你的电脑”。请只从项目的 GitHub Releases 下载，并在运行前校验同一 Release 中提供的 `.sha256` 文件。

在安装包所在目录打开 PowerShell，执行：

```powershell
Get-FileHash .\snaploom-1.0.0-windows-x64-setup.exe -Algorithm SHA256
Get-Content .\snaploom-1.0.0-windows-x64-setup.exe.sha256
```

两处 64 位十六进制 SHA256 必须完全一致。若不一致，请删除安装包，不要继续运行。校验一致且确认下载来源为本项目 Release 后，可在 SmartScreen 界面选择“更多信息”查看发布者状态并继续运行未签名测试版。

## 安装与卸载

- 默认安装目录：`%LocalAppData%\Programs\Snaploom`
- 设置目录：`%AppData%\Snaploom`
- 本地日志目录：`%LocalAppData%\Snaploom\Logs`
- 卸载入口：Windows“设置 → 应用 → 已安装的应用 → Snaploom”

直接运行新版本安装包即可覆盖升级。升级与卸载不会要求管理员权限；卸载应用时保留用户设置和日志，便于升级后继续使用或自行清理。
