# Snaploom

Snaploom 是一款面向 Windows 10/11 x64 与 macOS 14+ arm64 的轻量桌面截图与标注工具，使用 Tauri 2、Rust 与 TypeScript/Canvas 构建。

最后一个 .NET/Avalonia 实现已冻结在 annotated tag `dotnet-final`。当前分支不存在 .NET fallback 或第二套可构建应用；历史行为通过该标签、[`docs/snaploom-v1-spec.md`](docs/snaploom-v1-spec.md) 和 [功能等价合同](docs/research/snaploom-feature-equivalence-contract.md) 追溯。

## 架构

- `product/`：GPL-3.0-or-later Rust workspace，包含 Desktop、Capture Host、Capture Session 与平台 Adapter。
- `sdk/`：Apache-2.0 Rust workspace，包含公开协议、客户端与后续语言封装；不得依赖 GPL 产品树。
- `web/`：GPL-3.0-or-later pnpm workspace，包含单 Canvas Overlay Editor、统一 Screenshot UI 与 Desktop 设置界面。
- `tools/`：依赖边界与统一验证入口。

Desktop 与第三方 SDK 都只通过独立 Capture Host 进入同一截图链路。公开稳定边界仅为版本化本地 IPC 与 7-symbol C ABI；产品内部 Rust trait、Tauri command 与 TypeScript 类型不承诺外部兼容。

## 本地开发

需要 Rust 1.97.1、Node.js 22.12+ 和 pnpm 10.17。安装依赖后运行：

```shell
pnpm install --frozen-lockfile
cargo test --manifest-path sdk/Cargo.toml --workspace --locked
cargo test --manifest-path product/Cargo.toml --workspace --locked
pnpm run check
```

构建两个 Tauri 二进制：

```shell
pnpm run tauri:desktop
pnpm run tauri:host
```

也可运行 `cargo xtask check` 顺序执行 SDK、产品与 Web 验证。截图 UI 变更必须遵循 [`docs/ui/screenshot-ui.md`](docs/ui/screenshot-ui.md)。

## 许可证与贡献

产品、Capture Host 与截图实现使用 `GPL-3.0-or-later`；公开 Capture SDK 与示例使用 `Apache-2.0`。机器可读归属以 [`REUSE.toml`](REUSE.toml) 为准，详情见 [`LICENSE.md`](LICENSE.md)。贡献者需遵守 [`CONTRIBUTING.md`](CONTRIBUTING.md) 并以 DCO 1.1 签署每个 commit。
