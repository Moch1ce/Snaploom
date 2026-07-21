# Snaploom 依赖许可证与生成物审计

- 审计日期：2026-07-22
- 审计范围：根工具 workspace、`product/`、`sdk/`、`web/` 的锁文件、直接依赖、测试工具与受控字体
- 项目授权：产品与根工具默认 `GPL-3.0-or-later`；`sdk/`、`Package.swift` 与 `examples/sdk/` 按 `Apache-2.0` 边界发布，具体以 `REUSE.toml` 和文件 SPDX 标记为准

## 结论与门禁

仓库已不再使用旧版 `.NET/Avalonia` 架构，也不以闭源分发为目标。Rust 依赖由三个 `deny.toml` 的许可证 allow-list、来源限制和禁用 wildcard 规则约束；Web 依赖以 `pnpm-lock.yaml` 固定；仓库文件由 REUSE 校验。

新增或升级依赖时必须同时更新锁文件，并通过：

```bash
reuse lint
cargo deny --manifest-path Cargo.toml check
cargo deny --manifest-path product/Cargo.toml check
cargo deny --manifest-path sdk/Cargo.toml check
pnpm install --frozen-lockfile
pnpm run check
```

根 workspace 与产品 workspace 接受的许可证集合见各自 `deny.toml`；SDK 的集合更窄。`MPL-2.0` 仅在产品依赖图中准入，并不改变 Snaploom 自有代码的授权。发布包的第三方声明由锁文件生成并在 CI 中复核。

## 主要运行时依赖组

| 依赖组 | 锁定方式 | 用途与许可证控制 |
| --- | --- | --- |
| Tauri 2.11.x 与官方插件 | `product/Cargo.toml`、`product/Cargo.lock` | 桌面窗口、托盘、快捷键、通知、对话框、开机启动和单实例；由 `product/deny.toml` 验证许可证与 crates.io 来源 |
| `objc2` / ScreenCaptureKit 绑定 | `product/Cargo.toml`、`product/Cargo.lock` | macOS 捕获与系统集成；由产品 cargo-deny 门禁验证 |
| Windows API 生成绑定 | 根 `Cargo.toml`、`Cargo.lock` | `windows-bindgen 0.62.1`，`MIT OR Apache-2.0`，来源为 Microsoft `windows-rs` |
| `prost`、`serde`、`zeroize`、`png` 等 | 各 Rust workspace 锁文件 | 协议、序列化、敏感内存清理与 PNG 编码；由对应 cargo-deny 门禁验证 |
| `@tauri-apps/api` | `pnpm-lock.yaml` | WebView 到原生命令的桥接 |

`product/platform/windows/src/sys/bindings.rs` 由仓库内 `tools/windows-bindings` 使用固定版本 `windows-bindgen 0.62.1` 生成。上游源码为 `https://github.com/microsoft/windows-rs`，生成器许可证为 `MIT OR Apache-2.0`；生成后的仓库文件按项目 REUSE 聚合规则标记为 `GPL-3.0-or-later`。复现与漂移检查命令为：

```bash
cargo run --manifest-path Cargo.toml --locked --bin windows-bindings
cargo run --manifest-path Cargo.toml --locked --bin windows-bindings -- --check
```

## 仅测试与构建使用的浏览器资产

| 依赖或资产 | 版本 | 许可证 / 分发边界 |
| --- | --- | --- |
| `@playwright/test` / Playwright | 1.61.1 | Apache-2.0；只用于测试 |
| Chromium headless shell | `Chromium 149.0.7827.0` / Playwright revision 1228 | Chromium BSD 风格许可证及其第三方声明；来自固定摘要的 Playwright 测试镜像，只用于比较金图，不随产品分发 |
| `@fontsource/inter` | 5.3.0 | OFL-1.1；测试专用受控字体 |
| `@fontsource/noto-sans-sc` | 5.3.0 | OFL-1.1；测试专用受控字体 |

字体包来自 Fontsource npm 包，包元数据分别指向 Fontsource 的 Inter 与 Noto Sans SC 字体仓库。它们只在 Ubuntu 24.04 的固定 Chromium 金图夹具中加载，用于消除字体渲染漂移；生产截图浮层遵循 `docs/ui/screenshot-ui.md`，使用系统无衬线字体，不打包这些字体。

金图更新必须显式运行：

```bash
cargo run --locked --bin xtask -- goldens-update
```

该入口只允许 Ubuntu 24.04 更新；CI 使用固定镜像 `mcr.microsoft.com/playwright@sha256:5b8f294aff9041b7191c34a4bab3ac270157a28774d4b0660e9743297b697e48`，永远只比较已提交 PNG 和 sidecar，不自动重写基线。sidecar 同时记录浏览器 revision 与实际二进制版本，避免同一 revision 的下载产物漂移。
