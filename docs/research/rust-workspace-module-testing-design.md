# Rust 单仓模块、依赖与测试体系决策

状态：Issue [#30](https://github.com/moch1ce/Snaploom/issues/30) 的实施输入

适用范围：Windows 10 22H2 / Windows 11 x64、macOS 14+ arm64、Tauri 2 / Rust / TypeScript 重构

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、ADR 0001～0004、[Windows Adapter](./windows-rust-platform-adapter.md)、[macOS Adapter](./macos-rust-platform-adapter.md)、[Tauri 浮层与 Canvas 决策](./tauri-overlay-canvas-feasibility.md)、[Capture SDK C ABI 与 IPC](./capture-sdk-c-abi-ipc.md)、[截图 UI 规范](../ui/screenshot-ui.md)

## 1. 最终结论

Snaploom 采用**一个 Git 仓库、三个构建 workspace、三个产品深 Module、两条许可证依赖链**：

1. `product/` 是 `GPL-3.0-or-later` Rust workspace，包含 Snaploom Desktop、Capture Host、Capture Session 与 Windows/macOS 平台实现。
2. `sdk/` 是独立的 `Apache-2.0` Rust workspace，包含公开协议、SDK client、7-symbol C ABI、C++/C#/Swift wrappers 与示例。
3. `web/` 是 `GPL-3.0-or-later` pnpm workspace，包含 Capture Host 的单 Canvas `OverlayEditor` 与 Desktop 设置界面。
4. 产品运行时只设置三个需要长期维护的深 Module：`CaptureSession`、`PlatformAdapter`、`OverlayEditor`。它们各有一个小而完整的内部 Interface，复杂实现保持在 Seam 后面。
5. 仓库对第三方唯一承诺稳定、版本化的公开边界是 #28 已冻结的 **C ABI 与本地 IPC**。Rust trait、TypeScript 类型、Tauri commands、目录和内部消息可以在同一仓库提交中原子演进，不承诺外部兼容。
6. 迁移用功能等价合同 ID 组织 tracer scenarios：每个切片从 App 或 SDK 入口贯穿到可观察状态、最终像素、平台结果或错误恢复；但不为每个副作用、syscall、标注工具或测试替身创建浅端口。
7. `product` 与 `sdk` 分别拥有 `Cargo.toml`、`Cargo.lock` 和 `cargo-deny` 策略。Apache workspace 禁止解析到 GPL product/web；GPL product 可以单向依赖 Apache protocol/client。
8. CI 在每次 push/PR 运行依赖方向、生成代码、无头合同、双平台 Release build 与安装包静态验证；真实桌面、性能、权限、IME、签名/公证在专用 runner 和 Release gate 运行，不能用 fake 或无 UI runner 冒充。

这是一种有约束的混合方案：以深 Module 保持长期架构 Locality，以合同 ID tracer scenarios 驱动一次性替换，不采用“按技术层先横向铺满”或“每个 effect 一个 port”的两个极端。

## 2. 方案比较与取舍

Issue #30 比较了三种组织方式：

| 方案 | 优势 | 主要风险 | 最终吸收/否决 |
| --- | --- | --- | --- |
| 按端到端领域能力组织 | 截图入口到结果的行为集中；迁移每一步可运行；测试接近用户结果 | slice 可能过大，若没有小 Interface 会变成“把所有代码放一个目录” | **吸收** Capture Session 纵向切片、合同场景与端到端交付顺序 |
| 按稳定技术层与 crate 组织 | 依赖方向、许可证、构建和平台代码最清晰 | 容易先完成大量“以后会被调用”的层，最后才暴露集成错误 | **吸收** GPL/Apache 双 workspace、平台 Locality、生成/许可门禁 |
| Intent/Effect + 大量副作用 ports | 取消、Busy、失败恢复和 fake 很容易穷举 | 端口数量膨胀；一个平台行为被拆成许多浅 Interface；装配与 mock 反而超过产品逻辑 | **否决其公开形状**；只保留真实变化 Seam 和场景化故障注入 |

最终组织单位不是“每个工具”“每个 syscall”或“每个测试场景”，而是三个删除后会把复杂度重新扩散到多个调用方的深 Module。合同 ID 是验证索引，不是运行时模块边界或第二套业务语言。

## 3. 仓库与 workspace 布局

```text
/
├── Cargo.toml                         # 仅聚合工具命令，不把两种许可混成一个 workspace
├── rust-toolchain.toml
├── pnpm-workspace.yaml                # 只指向 web/**
├── product/                           # GPL-3.0-or-later Rust workspace
│   ├── Cargo.toml
│   ├── Cargo.lock
│   ├── deny.toml
│   ├── apps/
│   │   ├── desktop/                   # Tauri Desktop composition root
│   │   └── capture-host/              # Tauri Host composition root、IPC ingress
│   ├── capture-session/               # 深 Module：唯一会话与输出工作流
│   │   ├── src/
│   │   ├── tests/
│   │   └── benches/
│   ├── platform/
│   │   ├── contract/                  # PlatformAdapter Interface 与领域值
│   │   ├── fake/                      # ScriptedPlatform，仅测试/开发 harness
│   │   ├── windows/                   # WGC/D3D11/Win32/Tauri shell
│   │   └── macos/                     # SCK/objc2/AppKit/Tauri shell
│   ├── desktop-shell/                 # tray/settings/log/update 用例；非截图实现
│   └── testkit/
│       ├── scenario/
│       ├── host-harness/
│       └── sensitive-memory/
│
├── sdk/                               # Apache-2.0 Rust workspace
│   ├── Cargo.toml
│   ├── Cargo.lock
│   ├── deny.toml
│   ├── protocol/                      # proto、framing、版本、golden fixtures
│   ├── client/                        # transport、locator、launcher、exactly-once
│   ├── c-abi/                         # 7-symbol 动态库实现
│   ├── include/                       # 权威 C header
│   ├── cpp/
│   ├── dotnet/
│   ├── swift/
│   ├── examples/
│   └── testkit/fake-host/
│
├── web/                               # GPL-3.0-or-later pnpm workspace
│   ├── package.json
│   ├── pnpm-lock.yaml
│   ├── overlay-editor/                # 深 Module：单 Canvas 编辑器
│   │   ├── src/
│   │   ├── tests/
│   │   └── benches/
│   ├── screenshot-ui/                 # 主题、Toolbar、Icon、FloatingUiLayout
│   ├── desktop-settings/
│   └── testkit/
│
├── testing/
│   ├── contract-map/                  # 合同 ID → 自动/视觉/真机/性能证据
│   ├── scenarios/                     # 跨语言 tracer 输入与期望，不含分支逻辑
│   ├── goldens/
│   │   ├── state/
│   │   ├── render/
│   │   └── ui/
│   ├── desktop-e2e/
│   ├── performance/
│   └── packaging/
│
└── tools/
    ├── xtask/                         # 一致的本地/CI 入口
    ├── windows-bindings/
    ├── schema-check/
    └── contract-check/
```

根 `Cargo.toml` 不建立包含全部 package 的超级 workspace。Cargo workspace 共享依赖解析、命令和一个 lockfile；把 Apache 与 GPL 包纳入同一 workspace 会削弱独立 SDK 的锁文件、审计和发布身份。因此 `product/` 与 `sdk/` 是两个真实 workspace；根工具只顺序调用它们。[Cargo Workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html)

`web/` 使用一个 `pnpm-workspace.yaml` 和一个 lockfile，生产构建使用 frozen lockfile。pnpm workspace 对本地 package 使用 `workspace:` 协议，避免意外解析同名 registry package。[pnpm Workspaces](https://pnpm.io/workspaces)

禁止建立 `common/`、`shared-utils/`、`core-types-everywhere/` 或每种标注一个 package。共享必须先证明至少两个调用方需要同一行为，而不是只有相似名称。

## 4. 三个深 Module

### 4.1 `CaptureSession`

#### Interface

只有 Capture Host 构造该 Module：

```rust
pub trait CaptureSession {
    fn try_begin(
        &self,
        request: CaptureRequest,
    ) -> Result<CaptureSessionHandle, BeginError>;
}

pub trait CaptureSessionHandle {
    fn id(&self) -> SessionId;
    fn cancel(&self) -> CancelDisposition;
    async fn terminal(self) -> CaptureTerminal;
}
```

`try_begin` 原子返回 Accepted handle 或 `Busy`，不排队。Accepted handle 最终恰好得到一个 `Completed`、`Canceled` 或 `Failed`；`Completed` 持有统一 renderer 生成的 PNG 与物理尺寸。

这只是仓库内部 Interface，不进入 SDK header 或 IPC schema。Host IPC Adapter 把 `StartCapture`、`CancelCapture` 和连接断开翻译成调用；Desktop 与 C SDK 都不能直接构造此 Module。

#### 隐藏实现

- `Idle → Preparing → Interactive → Finalizing → Terminal cleanup → Idle`；
- 全局 gate、cancel/finalize 竞态、terminal CAS 与 late callback 丢弃；
- 捕获、overlay ready/show/hide、保存恢复、剪贴板重试与最终结果顺序；
- topology generation、最多一次完整重采样与类型化失败；
- frame、overlay、renderer、mosaic cache、history、observer 的资源 lease tree；
- Host IPC DTO、Tauri bridge DTO 与领域值之间的校验/转换；
- 隐私日志事件与性能分段，不包含像素、文字、窗口标题或路径。

#### 内部 Seams

`PlatformAdapter` 与 `OverlayEditorBridge` 是两个真实变化点。Clock/RNG 仅为 deadline、ID 与测试确定性作为私有依赖注入；它们不成为产品外部 trait。PNG 状态机、选择/标注规则、错误恢复和资源清理留在 Module 内，不拆成 effect ports。

### 4.2 `PlatformAdapter`

#### Interface

该 Interface 以领域操作聚类，而不是包装系统调用：

```rust
pub trait PlatformAdapter {
    async fn probe_capabilities(&self) -> CapabilityStatus;
    async fn capture_current_display(&self, cancel: CancelToken)
        -> Result<CaptureSnapshot, PlatformError>;
    async fn prepare_overlay(&self, placement: DisplayPlacement)
        -> Result<OverlayLease, PlatformError>;
    async fn replace_shortcut(&self, shortcut: Shortcut)
        -> Result<(), ShortcutError>;
    async fn write_png(&self, owner: SessionId, png: &SensitivePng)
        -> Result<(), ClipboardError>;
    async fn choose_png_destination(&self, request: SaveRequest)
        -> Result<SaveOutcome, SaveError>;
    async fn set_autostart(&self, enabled: bool)
        -> Result<AutostartStatus, PlatformError>;
    fn watch_resume(&self, sink: ResumeSink) -> Result<EventLease, PlatformError>;
    async fn notify(&self, notification: StableNotification);
}
```

实际 Rust 语法可以随实现调整，但操作集合不能退化成 `call_win32`、`invoke_objc` 或 Tauri plugin pass-through。`CaptureSnapshot`、`DisplayPlacement`、`WindowCandidate`、稳定错误和 lease 是 Interface 的全部平台知识。

#### 隐藏实现

- Windows：WGC、D3D11、HDR→sRGB、窗口目录、PMv2、Win32 PNG clipboard、topmost、resume；
- macOS：SCScreenshotManager、CVPixelBuffer、TCC、SCK/CG 窗口合并、坐标翻转、NSPasteboard、SMAppService、NSWindow level/Spaces；
- Tauri tray、global shortcut、dialog、single instance、notification 的 Rust 侧生命周期；
- `HWND`、`HMONITOR`、HRESULT、COM、D3D、`SCDisplay`、`CVPixelBuffer`、`NSWindow`、NSError 与 AppHandle。

平台 Module 内可以有 `CaptureBackend`、`WindowCatalog`、`ClipboardBackend`、`Shell`、`PowerEvents/WorkspaceEvents` 等私有 Seams，因为 Windows/macOS 实现和 fake 确有替换价值。不得为每个 Win32/Objective-C 函数创建 trait。

### 4.3 `OverlayEditor`

#### Interface

```ts
export interface OverlayEditor {
  mount(snapshot: SnapshotDescriptor): Promise<void>;
  dispatch(input: NormalizedInput): EditorEffects;
  render(frameTime: number): void;
  projectFloatingUi(): FloatingUiProjection;
  composeResult(): Promise<Uint8Array>;
  snapshotState(): EditorSnapshot;
  dispose(): void;
}
```

Tauri bridge 只传 Session-scoped 领域命令、opaque 底图资源和 bounded binary；不传 DOM node、Canvas context、任意路径、平台 handle 或 plugin object。Tauri commands 会把前端参数反序列化到 Rust，因此 Rust 必须继续校验 Session、尺寸、长度和状态，不能把 WebView 视为可信调用者。[Tauri：Calling Rust from the Frontend](https://v2.tauri.app/develop/calling-rust/)

#### 隐藏实现

- 一个可见主 Canvas、只读底图、遮罩、选区、8 控制柄和所有标注；
- 根 capture-phase 输入路由、pointer capture、手势阈值、命中/Z 序与光标；
- `capturePhysical` / `overlayLogical` / scale X/Y 映射；
- 同一对象模型和 render plan 驱动预览与最终 PNG；
- 128 物理像素马赛克瓦片、dirty rect、每帧至多一次合成；
- 一个复用 textarea 的 IME/composition、自动换行和边界裁限；
- 同窗 `ScreenshotUiTheme`、Toolbar、Icon、FloatingUiLayout 与设置浮层。

Canvas backing bitmap 的物理尺寸与 CSS 布局尺寸是不同概念；生产以 Rust snapshot 的物理宽高设置 backing store，而不是把 `devicePixelRatio` 当捕获事实。[HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html) Pointer capture、`pointercancel` 与 composed event path 遵循 Pointer Events 标准，工具栏/textarea 必须在根路由阻止手势进入截图 surface。[Pointer Events](https://www.w3.org/TR/pointerevents3/)

## 5. 全局不变量

1. 每个登录会话、每个 IPC major 只有一个 Capture Host leader 和一个 Capture Session gate。
2. Desktop App 与所有 SDK client 通过同一 Host；App 不保留截图实现旁路。
3. 已有 Session 时立即 `Busy`，不排队、不抢焦点、不闪烁当前 overlay。
4. 成功入队的 SDK `start` 恰好一次异步终态；同步失败绝不 callback。
5. 捕获底图在 Session 内只读；标注是相对选区的非破坏性对象。
6. 一个可见 Canvas 是截图像素、几何、Z 序、裁切与导出的唯一真相；DOM 不进入 Capture Result。
7. 物理像素、显示器和 scale 来自 `CaptureSnapshot`，不用 CSS、主屏 scale 或缓存拓扑猜测。
8. App、clipboard、save 和 SDK result 使用同一最终 PNG bytes；不为各出口重新编码。
9. 保存取消/失败、clipboard 失败完整恢复选区、对象、草稿、活动工具、选中态与历史。
10. gate 只在 overlay、buffer、renderer/cache 与历史释放后回到 Idle。
11. WebView 不获得 clipboard/dialog/filesystem/global-shortcut/autostart guest 权限。
12. 生产底图/结果不用 Base64、data URL、共享内存、临时文件或 TCP fallback。
13. 窗口目录失败只退化为少量/无候选；捕获、分辨率、IME、输出和性能合同不静默降级。
14. Rust panic、C++ exception、Swift/C# callback 控制流不得穿过 C ABI。
15. 日志、fixture failure output 和性能样本不包含截图、标注文字、剪贴板、窗口标题或完整路径。

## 6. 依赖 DAG 与禁止边

允许依赖：

```text
web/overlay-editor ------------------------------┐
                                                 │ Tauri 内部 bridge
product/apps/capture-host                        │
  ├── product/capture-session <──────────────────┘
  │     └── product/platform/contract
  ├── product/platform/windows | macos
  └── sdk/protocol

product/apps/desktop
  ├── product/desktop-shell
  └── sdk/client ──> sdk/protocol

sdk/c-abi ──> sdk/client ──> sdk/protocol
sdk/cpp | dotnet | swift ──> 7-symbol C ABI
```

禁止依赖：

```text
sdk/**                    -X-> product/**
sdk/**                    -X-> web/**
sdk/**                    -X-> Tauri / GPL Host symbols
web/**                    -X-> 平台系统调用或 Tauri guest plugins
platform/**               -X-> OverlayEditor/DOM/Canvas
desktop                   -X-> CaptureSession 实现
CaptureSession            -X-> Windows/macOS 具体类型
wrappers                  -X-> IPC/protobuf/Host locator 的重复实现
```

机械门禁：

- `sdk` 的 `cargo metadata` 中所有 workspace/path dependency 必须位于 `sdk/`；
- `product` 允许 path dependency 指向 `sdk/protocol`、`sdk/client`，反向一律失败；
- SDK archive、NuGet、XCFramework 与 C/C++ zip 扫描不得含 Tauri、Host、capture-session 或 platform symbols/files；
- `web` bundle 不含 Node runtime、Tauri guest capability 或访问任意 filesystem/network 的代码；
- 两个 Rust workspace 分别执行 `cargo deny check`，SDK allowlist 更窄；
- `Cargo.lock`、`pnpm-lock.yaml`、生成的 Windows bindings 与 protobuf outputs 必须提交且 clean-diff。

## 7. Fake 与测试替代策略

### 7.1 `ScriptedPlatform`

`product/platform/fake` 实现完整 `PlatformAdapter`，但只建模可观察平台行为：

- `VirtualDesktop`：显示器、物理/逻辑尺寸、scale X/Y、工作区、指针、窗口候选和像素；
- 每个领域操作可脚本化 success/cancel/typed failure；
- `VirtualClock` 控制 capture timeout、clipboard retry、interaction deadline 和 resume；
- `LeaseTracker` 记录 frame、overlay、observer、shortcut、dialog、clipboard buffer 是否释放；
- `CallLog` 只供测试断言领域调用顺序，不进入生产 Interface。

Fake 不模拟 WGC COM vtable、SCK block、NSWindow 或 Win32 clipboard ownership；这些属于真实 Adapter 的平台测试。共享 Platform contract suite 只检查 Interface 行为，平台专用 suite 再检查原生资源与 flags。

### 7.2 IPC 与 Host

Apache `sdk/testkit/fake-host` 实现公开 wire，不依赖 GPL Host，用于 SDK/ABI/wrapper 测试。它支持：

- partial read/write 和每个 frame/chunk 边界断开；
- Busy、cancel race、duplicate/late terminal、invalid sequence；
- crash/EOF、handshake/launch timeout、协议 minor/capability 矩阵；
- bounded 1080p/4K/5K PNG 与超限结果。

GPL `product/testkit/host-harness` 把真 Host IPC ingress 接到 `CaptureSession + ScriptedPlatform + OverlayHarness`，验证 App/SDK 竞争唯一 gate。真实 SDK→真实 Host 的 E2E 属于 GPL 测试资产，不能塞进 Apache SDK 包。

### 7.3 Canvas 与 DOM

纯状态、几何、命中和布局在 Vitest 中测试。像素测试运行真实、固定版本浏览器 Canvas，不 mock `CanvasRenderingContext2D` 调用次数；生产 WKWebView/WebView2 另跑平台 E2E。

仅为测试建立 recording surface 时，它是 `OverlayEditor` 私有 Seam，不公开为可插拔 renderer。预览和导出始终只有一套生产 render plan。

## 8. 合同 ID tracer scenarios

`testing/contract-map/contracts.yml` 是迁移可追踪性的权威索引，每项至少包含：

```yaml
id: OUT-02
title: copy-keeps-editing
implementation:
  - product/capture-session
  - web/overlay-editor
evidence:
  automatic:
    - product:capture-session::copy_keeps_session
    - web:overlay-editor::copy_snapshot
  pixel:
    - testing/goldens/render/copy-save-identical.png
  platform:
    - windows:clipboard-exact-png
    - macos:pasteboard-exact-png
status: required
```

实际 schema 可以更紧凑，但必须保证：

- 每个功能合同 `MUST` 至少映射一种当前实现证据；
- 需要真机的合同不能只映射 fake/headless；
- 视觉、性能、权限、IME、安装与签名必须指向对应证据类别；
- 删除或改名测试时 CI 立即发现悬空引用；
- ticket/PR 可声明本次更新的合同 ID，CI 反向检查相关场景已运行；
- scenario 文件只保存输入、fixture、期望状态/像素和平台前置条件，不包含条件分支、循环或产品算法。

推荐 tracer scenario 形状：

```text
testing/scenarios/OUT-02-copy-keeps-editing/
├── scenario.json
├── captured-frame.png
├── events.json
├── expected-state.json
└── expected-render.png
```

同一 scenario 可由 Rust state harness、browser renderer 和平台 E2E 读取；不能为了共享而发明新的运行时协议。

## 9. 测试金字塔

### 9.1 快速领域与属性测试

- `CaptureSession`：gate、终态、cancel/finalize 竞态、disconnect、错误恢复、lease 释放；
- 选区/DPI：100/125/150/175/200%、独立 X/Y scale、负原点、任意方向、8 物理像素下限；
- 窗口候选：过滤、裁切、Z 序、稳定 ID、桌面回退；
- 标注：创建、命中、移动、缩放、样式、删除、undo/redo、选区裁切；
- 浮动 UI：下方/内部右下/上方、工作区避让、标注后冻结；
- 设置、隐私日志、快捷键替换事务、手动更新错误分类。

这些测试通过深 Module Interface 或明确的私有纯函数 Seam 观察行为，不锁定内部函数调用数、React render 次数、Canvas calls 或系统错误文本。

### 9.2 Module contract

- `PlatformAdapter` 共享 contract suite：成功、取消、类型化失败、retry/restore 与 lease；
- `OverlayEditor`：正规化输入→状态快照、浮动投影、最终 decoded pixels；
- C ABI：layout、calling convention、symbols、所有权、callback threading/reentrancy；
- protocol：framing、golden binary、minor 双向兼容、capabilities、fuzz corpus；
- SDK client：exactly-once、cancel/destroy、Host discovery、secure bootstrap 与 transport limits。

### 9.3 浏览器与进程集成

- pinned browser 中运行单 Canvas、DOM 浮层、pointer capture 和 textarea composition；
- 真 Host IPC + fake platform/editor：App/SDK 并发、Busy、cancel、Host/client crash；
- Tauri Host 使用 opaque binary 资源完成 snapshot→editor→PNG，不出现 Base64；
- SDK wrappers 通过真 C ABI 动态库连接 fake Host；
- 20 个进程并发 launch 只产生一个 Host leader。

### 9.4 双平台真实桌面

- Windows 10/11：WGC、PMv2、混合 DPI、HDR→sRGB、topmost、任务栏、PNG clipboard、dialog；
- macOS 14+：TCC、SCK、混合 Retina、Spaces/full-screen Space、Dock/菜单栏、IME、NSPasteboard、dialog；
- 单/双屏、负原点、热插拔、睡眠唤醒、快捷键冲突、第二实例；
- 权限拒绝/撤销、受保护桌面、clipboard contention、保存取消/失败；
- 用户级安装、覆盖升级、卸载、ad hoc/Developer ID、notarization/staple。

### 9.5 发布与压力

- SDK/Host 资产边界、LICENSE/NOTICE/SBOM/checksum/对应源码；
- 1000 次 start/Busy/cancel、100 次 5K result；
- 每个 frame/chunk boundary crash、sensitive allocation 清零；
- 安装包大小、架构、最低系统、签名与 Release 原子性。

## 10. Golden 与像素测试

```text
testing/goldens/state/*.json
testing/goldens/render/*.png
testing/goldens/ui/*.png
sdk/protocol/fixtures/*.bin
sdk/c-abi/tests/layout/*.json
```

规则：

1. 状态 golden 保存领域值，不保存 DOM tree、Rust debug dump 或内部 enum discriminant。
2. renderer golden 使用固定底图、固定 sRGB、受控测试字体和物理尺寸；比较解码 RGBA，而不是依赖 PNG 压缩字节稳定。
3. 同一次 Session 的 clipboard/save/SDK 输出必须比较原始 PNG bytes 完全一致，因为它们应共享同一 allocation。
4. 文字金图使用仓库内准入并标注许可证的受控字体；系统 emoji 只断言不崩溃。
5. Canonical Canvas golden 在固定浏览器生成；Windows WebView2/macOS WKWebView 另保存平台视觉证据，不能用 Chromium 结果冒充生产 WebView。
6. 工具栏、设置浮层、textarea 和尺寸标签做 UI screenshot + 几何断言；最终 Capture Result 不含这些 DOM 元素。
7. CI 永不自动更新 golden。更新必须运行显式 `xtask goldens-update`，在独立提交中审查像素 diff 与关联合同 ID。
8. 每个 golden 带生成器版本、fixture hash、物理尺寸和合同 ID sidecar，避免“图片存在但不知道证明什么”。

## 11. 性能与资源基准

基准直接继承 `PERF-01`，不降低门槛：

| Benchmark | 硬门槛 | 分段 |
| --- | ---: | --- |
| 快捷键→overlay 可交互 | 30+ 样本 nearest-rank P95 ≤ 150 ms | intent、display resolve、capture、pixel handoff、WebView ready、first paint |
| 4K 编辑 | P95 ≤ 16.667 ms、稳定 60 FPS | input merge、state transition、dirty calculation、Canvas paint、long task/drop |
| 4K PNG | P95 ≤ 1,000 ms，含实际临时文件写入 | render、encode、Rust binary handoff、write |
| 空闲内存 | ≤ 100,000,000 bytes | Windows working set；macOS physical footprint |
| 资源稳定 | 20+ 成功循环尾段增长 ≤ 1% | 最后两个连续 5 次窗口中位数 |
| 分发包 | 每平台应用安装包 ≤ 50,000,000 bytes | 完整 production bundle |

补充要求：

- 性能 job 独占运行，不与编译、测试或病毒扫描争抢 CPU；
- 保存每个原始样本与可复算聚合结果，不只保存最终 P95；
- 使用真实 3840×2160/5K frame、真实 WebView 与真实平台 capture，synthetic/headless 只做回归预警；
- 分别跟踪 bitmap、D3/COM、CVPixelBuffer、WebView、IPC buffer、threads/handles/fds；
- 取消或直接关闭不计成功输出循环；每轮等待 Session 完成 cleanup；
- shared memory、持久 GPU device 或替代 renderer 只有在数据证明当前设计无法达标且新 ADR 批准后才能引入。

## 12. CI jobs

### 12.1 每次 push 与 PR

| Job | 内容 | 失败意义 |
| --- | --- | --- |
| `architecture` | 两个 `cargo metadata`、禁止 path/dependency、Web capability、SDK 资产边界 | 许可证或模块方向被破坏 |
| `format-lint` | Rust fmt/Clippy、TS typecheck/ESLint、C/C++ warning-as-error | 基础质量不通过 |
| `generated-clean` | proto、C header mirror、Windows bindings 重新生成后 clean diff | schema/bindings 漂移 |
| `product-contracts` | CaptureSession、ScriptedPlatform、desktop-shell、属性/场景测试 | 产品合同回归 |
| `web-contracts` | reducer/layout/input、Canvas golden、DOM/IME browser tests | OverlayEditor 回归 |
| `sdk-contracts` | ABI、protocol、fake Host、compatibility、fault injection | 公开 SDK 边界回归 |
| `host-e2e` | 真 IPC + fake Session/platform/editor、Busy/cancel/crash | 进程/终态语义回归 |
| `windows-release` | x64 Release build、tests、Tauri bundle、installer 静态校验 | Windows 不能交付 |
| `macos-release` | arm64 Release build、tests、frame orientation、Tauri app/DMG 静态校验 | macOS 不能交付 |
| `compliance-product` | GPL workspace deny、REUSE、NOTICE/SBOM smoke | GPL 产物合规失败 |
| `compliance-sdk` | Apache 窄 allowlist、包内容、NOTICE/SBOM smoke | SDK 不能安全分发 |
| `contract-map` | 每个 MUST 的证据引用存在且类别充分 | 迁移失去可追踪性 |

`windows-release` 和 `macos-release` 每次都至少完成 Release build 与安装包结构验证，不能因只改另一平台就永久跳过。受影响路径过滤只用于提前运行快速子集；合并所需的 aggregator 必须等待完整 required jobs。

### 12.2 Nightly / 专用真实桌面 runner

- Windows 10、Windows 11、macOS 14 与当前支持版本；
- WGC/SCK、权限、DPI/Retina、topmost、IME、clipboard/dialog、sleep/hotplug；
- IPC fuzz smoke、leader concurrency、crash boundary、1000/100 压力；
- 4K 性能预警与资源循环。

### 12.3 Release gate

- 在专用真实桌面 runner 重新执行硬性能与平台矩阵；
- 构建 App、Capture Host、C SDK、NuGet、Swift/XCFramework、源码、NOTICE、SBOM、checksums；
- 验证同 tag/semver、独立许可证身份、SDK 不含 Host；
- Windows/macOS 任一失败或任一资产缺失都不得公开部分 Release；
- Developer ID/notarization 失败不得回退 ad hoc；测试版 ad hoc 与正式签名模式必须显式区分。

## 13. 迁移 tracer slices

每个 slice 必须产生一个从入口到可观察结果的最小路径、更新合同映射、独立测试；不能只交付一个未被产品调用的底层 crate。

1. **Workspace 与许可证外壳**：建立 `product/sdk/web`、锁文件、deny、REUSE、空 App/Host 构建和依赖方向门禁。
2. **公开协议 tracer**：C SDK `start → IPC → fake Host → terminal callback/free`，覆盖 cancel、Busy 和 exactly-once。
3. **Host gate tracer**：真 Host ingress → `CaptureSession` → fake platform/editor → cancel；App/SDK 并发只接受一个。
4. **Frame-to-overlay tracer**：fake 当前显示器 snapshot → opaque binary → 单 Canvas first paint → ready/show/dispose。
5. **Selection-to-PNG tracer**：自由框选、物理像素尺寸、遮罩与完成 PNG；建立首个 render golden。
6. **Window selection tracer**：初始抑制、候选 Z 序、桌面回退、混合 DPI/负原点。
7. **Annotation tracers**：矩形 → 箭头 → 文字/IME → 马赛克；每个都贯穿对象、history、renderer、PNG，而非只新增按钮。
8. **Output tracer**：完成/copy/save、同 bytes、保存隐藏/取消恢复、clipboard/file failure 恢复。
9. **Windows real Adapter**：WGC frame 先替换 fake capture，再逐步替换窗口、overlay、clipboard/dialog、shortcut/shell；每步复用同一 scenario。
10. **macOS real Adapter**：SCK/TCC frame 先替换 fake，再替换窗口、overlay、pasteboard/dialog、SMAppService/shell。
11. **Desktop lifecycle**：tray-only、second instance、快捷键事务/唤醒、设置、隐私日志、手动更新。
12. **SDK wrappers**：C++、C#、Swift 只封装 7-symbol ABI，跑 ownership/thread/cancel/package 矩阵。
13. **Package/performance/release**：双平台安装、签名、公证、性能、SBOM/NOTICE/source 与原子 Release。

真实 Adapter 的引入采用“替换 fake 的一个领域操作，再跑相同合同”的方式；不长期保留第二捕获 backend，也不先横向写完整个平台层再等待集成。

## 14. 完成判定

Issue #30 的设计被正确实施，需要同时满足：

- 仓库只有 `product` GPL Rust、`sdk` Apache Rust、`web` GPL pnpm 三个 workspace 身份；
- `CaptureSession`、`PlatformAdapter`、`OverlayEditor` 各只有一个外部 Interface，复杂度没有扩散到 apps/wrappers；
- C ABI/IPC 是唯一公开版本化边界，内部 bridge 不被文档宣传为第三方能力；
- SDK graph、archive 与 symbols 都无法到达 GPL/Tauri；
- 每个功能合同 MUST 有当前实现证据，需真机项没有被 fake 替代；
- 同一 tracer scenario 能在 fake、browser/process harness 和适用真实 Adapter 上复用可观察断言；
- golden、性能原始样本、平台证据和 package evidence 可由合同 ID 找回；
- 双平台 Release build、安装包、许可证与资产原子性成为 required gates；
- 旧 .NET 代码删除后，不需要运行 `dotnet-final` 才能解释任何产品行为或验收门槛。

## 15. 一手资料

- Cargo workspace 与 lockfile：[Cargo Workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html)、[Cargo Lock Files](https://doc.rust-lang.org/cargo/guide/cargo-toml-vs-cargo-lock.html)
- Cargo manifest 许可证：[The Manifest Format](https://doc.rust-lang.org/cargo/reference/manifest.html#the-license-and-license-file-fields)
- Rust FFI 与 `repr(C)`：[Rustonomicon FFI](https://doc.rust-lang.org/nomicon/ffi.html)、[Alternative representations](https://doc.rust-lang.org/nomicon/other-reprs.html)
- pnpm workspace：[pnpm Workspaces](https://pnpm.io/workspaces)
- Tauri command、窗口与 shell：[Calling Rust](https://v2.tauri.app/develop/calling-rust/)、[WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)、[System Tray](https://v2.tauri.app/learn/system-tray/)、[Tauri Plugins](https://v2.tauri.app/plugin/)
- Canvas 与输入：[HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html)、[Pointer Events](https://www.w3.org/TR/pointerevents3/)、[UI Events Composition Events](https://w3c.github.io/uievents/#events-compositionevents)
- Protocol Buffers 兼容：[Proto3 language guide](https://protobuf.dev/programming-guides/proto3/)、[Proto best practices](https://protobuf.dev/best-practices/dos-donts/)
- Windows/macOS 平台一手 API：见 [Windows Adapter](./windows-rust-platform-adapter.md) 与 [macOS Adapter](./macos-rust-platform-adapter.md) 的一手资料索引。
