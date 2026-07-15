# T15 Windows 安装包真机验收

## 自动验收

Windows CI 使用固定的 Inno Setup 6.7.1 构建 `win-x64` self-contained 用户级 EXE。发布阶段只移除运行时不需要的 PDB 调试符号，并执行以下检查：

1. 安装包、SHA256 和元数据一致，安装包不超过 50,000,000 字节。
2. 静默安装不触发提权，安装目录、产品版本、图标和 `Snaploom.Desktop` 当前用户卸载项正确。
3. 安装后的应用能够启动并保持运行，日志写入 `%LocalAppData%\Snaploom\Logs`。
4. 再次运行安装包能够覆盖现有安装。
5. 静默卸载后应用文件和当前用户卸载项均被移除。

## Windows 10/11 真机矩阵

分别在 Windows 10 22H2 x64 与 Windows 11 x64 普通用户账户执行：

1. 按 `docs/distribution/windows-installation.md` 校验 SHA256，确认 SmartScreen 显示未签名提示且安装过程不请求管理员凭据。
2. 完成安装，确认开始菜单中的 Snaploom 名称和图标正确，“已安装的应用”存在 Snaploom 卸载项。
3. 启动 Snaploom，按 `Alt+Shift+A`，完成一次框选截图并复制，再完成一次框选截图并保存 PNG。
4. 保持用户设置不变，运行更高版本安装包，确认可以覆盖升级且升级后仍能截图。
5. 从“已安装的应用”卸载，确认程序目录和卸载项被移除；确认用户设置与日志按说明保留。
