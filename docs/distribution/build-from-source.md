# 从对应源码构建 Snaploom

Release 的 `snaploom-<版本>-corresponding-source.tar.zst` 由精确 tag commit 生成，包含
`product`、`sdk`、`web` 三个 workspace、锁文件、公开协议、生成器输入、打包脚本、
REUSE 元数据和许可证。它不包含官方签名私钥、Apple 公证凭据、NuGet token 或构建缓存。

修改版不需要 Snaploom 官方签名即可使用公开 ABI/IPC。恢复依赖并构建 unsigned/ad hoc 版本：

```bash
corepack enable
pnpm install --frozen-lockfile
cargo build --manifest-path product/Cargo.toml --workspace --release --locked
cargo build --manifest-path sdk/Cargo.toml --workspace --release --locked
pnpm run build
```

macOS 14+ Apple Silicon 候选包使用明确的 ad hoc 模式：

```bash
./scripts/build-macos-packages.sh --version <X.Y.Z> --adhoc
```

Windows 10 22H2+/Windows 11 x64 候选包使用明确的 unsigned 模式（需要 Inno Setup 6.7.1）：

```powershell
./scripts/build-windows-packages.ps1 -Version <X.Y.Z> -SigningMode candidate-unsigned
```

正式稳定 Windows 包使用 `stable-unsigned`，并依靠 checksum 与 GitHub attestation 验证来源；正式稳定
macOS 包只能在受保护的发布环境使用 Developer ID 和 Apple 公证凭据构建。该平台信任策略不改变修改版
Host 对公开协议的可用性。
