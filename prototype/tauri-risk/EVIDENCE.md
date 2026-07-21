# Tauri 截图链路风险原型证据

验证问题：一个不透明 Tauri 2 WebView 是否能在 macOS arm64 上覆盖完整显示器，以 4K 截图为背景承载选区、标注、系统文字输入，并把最终 PNG 返回 Rust，同时满足首版包体与内存预算？

## 环境

- 日期：2026-07-21
- 主机：Apple Silicon arm64，macOS 26.5
- Rust：1.97.1 stable
- Tauri：2.11.5；CLI 2.11.4
- Node：22.12.0；pnpm 10.17.0
- WebView：系统 WKWebView；`transparent=false`，未启用 `macOSPrivateApi`

## 已运行路径

1. 生成 3840×2160 的不透明截图背景与三个模拟窗口候选。
2. 在完整显示器 WebView 中显示遮罩、物理像素选区与尺寸标签。
3. 比较三个结构：单 Canvas、背景 + 交互 Canvas、背景 + SVG/DOM。
4. 贯通矩形、箭头、文字 textarea/IME、马赛克、撤销、完成与取消入口。
5. 启动时把完整 4K PNG data URL 通过 Tauri invoke 传到 Rust；Rust 解码、校验 PNG signature 并计算 SHA-256。
6. 构建 arm64 Release `.app`，检查 architecture、ad hoc 签名、包体和进程 physical footprint。

## 测量结果

| 指标 | 结果 | 判定 |
| --- | --- | --- |
| 前端生产构建 | JS 202.45 kB，gzip 64.32 kB | 通过 |
| Release app bundle | 9.7 MB | 低于 50 MB 门槛 |
| Mach-O | arm64 | 通过 |
| 签名 | ad hoc / linker-signed | 原型通过；正式发布仍需 Developer ID + notarization |
| 4K WebView→Rust PNG | 5,453,898 bytes；SHA-256 已由 Rust 返回 | 通过 |
| 单 Canvas 合成时间 | 稳态 UI 读数约 0～1 ms/次（合成背景、遮罩和当前标注） | 风险解除；最终产品仍须真 4K 交互基准 |
| 主进程 physical footprint | 48 MB，峰值 64 MB | 低于 100 MB 门槛 |
| 主进程 RSS | 约 123～132 MB | 不作为合同指标；记录用于诊断 |
| 可见窗口 | 1920×1080 全屏窗口，完整截图背景且遮罩/选区/工具栏层级正确 | 通过 |

视觉证据见 [`evidence/macos-arm64-single-canvas.png`](./evidence/macos-arm64-single-canvas.png)。

## 结论

采用 Variant A：单个 Canvas 负责截图背景、遮罩、选区、标注、控制柄与最终合成；文字编辑期间短暂叠加系统 textarea，提交后回到 Canvas。它的 Interface 最小、坐标真相唯一，能把高 DPI、裁切、Z 序和像素输出留在一个深 Module 内。

Variant B 证明分层可用，但会引入背景和交互层同步、缩放与截图时序的额外 Interface；没有提供等量 Leverage。Variant C 的原生 textarea/IME 值得保留为临时输入层，但 SVG/DOM 标注会把命中、层级和物理像素映射扩散到多个渲染系统，因此不作为最终主渲染路径。

## 仍需后续真机门禁

- Windows x64 由本分支 GitHub Actions 执行前端与 Rust/Tauri 编译；真实 WGC、DPI、置顶和窗口样式仍由平台 Adapter 集成测试验收。
- macOS 任意其他进程的 click-through 窗口没有公共属性可读取；原型不能解除该能力缺口，必须按 macOS Adapter 决策保持显式合同例外或产品取舍。
- 原型使用生成背景隔离 WebView 风险，没有请求 TCC 或调用真实 ScreenCaptureKit/WGC；真实捕获由各平台 Adapter 负责。
- data URL 只用于原型证明桥接。最终产品必须通过 Rust 管理的会话资源/二进制通道交付捕获 buffer 和 PNG，避免 Base64 复制。

## 复现

```sh
PATH="/opt/homebrew/opt/rustup/bin:$PATH" pnpm install
pnpm build
PATH="/opt/homebrew/opt/rustup/bin:$PATH" cargo check --manifest-path src-tauri/Cargo.toml
PATH="/opt/homebrew/opt/rustup/bin:$PATH" pnpm prototype:build
```
