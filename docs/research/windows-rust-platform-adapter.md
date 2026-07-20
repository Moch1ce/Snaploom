# Windows Rust 平台 Adapter 决策

状态：Issue [#23](https://github.com/liuchuana/Snaploom/issues/23) 的实施输入

适用范围：Windows 10 22H2 x64（最低 build 19045）与 Windows 11 x64

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、ADR 0001～0004、`docs/ui/screenshot-ui.md`

## 1. 结论

Windows 首版采用以下组合，不再保留平行候选：

1. Windows 系统调用使用 `windows-rs` 工具链生成的最小 full-fidelity bindings。生成器使用 `windows-bindgen`，生成结果提交到仓库；运行时只依赖生成代码所需的 `windows-core`、`windows-result`、`windows-strings`、`windows-link`。不直接依赖完整 `windows` / `windows-sys` umbrella crate，不手写 `extern "system"`、COM vtable 或 WinRT ABI。
2. 屏幕捕获直接使用 Windows Graphics Capture（WGC）与 D3D11：`IGraphicsCaptureItemInterop::CreateForMonitor`、`Direct3D11CaptureFramePool::CreateFreeThreaded`、一帧捕获、关闭鼠标指针合成。WGC 不可用或失败时不回退到 GDI `BitBlt`、DXGI Desktop Duplication 或社区截图 crate。
3. 顶层窗口枚举、窗口过滤、DPI/显示器解析、PNG 剪贴板、系统唤醒和最终置顶保证直接调用生成的 Win32/WinRT bindings；这些产品特有语义不交给通用 crate 猜测。
4. 常驻托盘使用 Tauri 2 内建 tray；全局快捷键、开机启动、原生保存对话框、应用单实例与通知分别使用 Tauri 2 官方能力。插件只从 Rust 侧调用，不把插件 guest commands 直接授权给 WebView。
5. 对领域层只暴露一个深层 `PlatformAdapter` Interface。`HWND`、`HMONITOR`、HRESULT、COM、D3D11、Tauri `AppHandle` / `WebviewWindow` 和插件错误全部留在 Windows Adapter 内。
6. 捕获、剪贴板、保存和窗口枚举都采用合同规定的可恢复失败语义；不得为了“看起来成功”而切换到语义不同的备用路径。

这套选择优先保证功能等价、错误可诊断、依赖可审计和测试 Locality。具体 crate 版本在 Rust workspace 建立时按当时稳定的 Tauri 2 兼容版本锁入 `Cargo.lock`；本文不提前猜测未来实施提交的精确版本。

## 2. 合同映射

本决策至少承接以下合同：

| 合同 | Windows Adapter 责任 |
| --- | --- |
| `APP-01` | tray-only 启动；截图能力不可用时只禁用“开始截图” |
| `APP-02` | 官方 single-instance 插件关闭第二实例并向主实例发送一次截图意图 |
| `APP-03` | 所有平台入口进入同一个领域 Capture Session 门控；Adapter 不另建会竞争的门控 |
| `APP-04` | 注册/替换快捷键、冲突回滚、唤醒重注册及失败通知 |
| `CAP-01` | 指针所在显示器、无鼠标、物理像素、逻辑尺寸、全局原点、候选窗口、预乘 BGRA8888/sRGB |
| `CAP-02` | 普通用户直接捕获；不提权、不请求额外捕获授权、不绕过安全桌面或 DRM |
| `CAP-03` | 捕获错误分类、资源释放、允许重试、隐私日志 |
| `CAP-04` | 产品定义的窗口过滤、视觉 Z 顺序、显示器裁切与桌面回退 |
| `OUT-01/02` | 向剪贴板写入与保存完全相同的最终 PNG 字节 |
| `OUT-03` | PNG-only 原生保存对话框；先隐藏浮层；取消/失败恢复状态 |
| `SET-01` | 当前用户级开机启动；失败时不得伪造 UI 或持久化成功 |
| `ERR-01` | 每种平台失败映射到稳定、可恢复的领域结果 |
| `PERF-01` | 快捷键到可交互 P95、空闲内存、PNG 延迟和资源稳定性门禁 |
| `QA-01～03` | 统一平台测试 Seam、自动矩阵与 Windows 真机矩阵 |

## 3. Module、Interface 与 Locality

### 3.1 深层 Module

`platform-windows` 是一个深层 Module：较小的领域 Interface 隐藏 WGC、D3D11、Win32 窗口管理、剪贴板所有权、Tauri 插件生命周期和资源释放。它的 Depth 来自“少量领域操作背后承载大量平台规则”，而不是来自类型或文件数量。

这个 Module 的 Leverage 体现在三个调用方都使用相同语义：

- Snaploom 应用的快捷键、托盘与设置入口；
- 独立 Capture Host 的交互式截图入口；
- 无 Windows 环境的自动测试 fake。

跨平台共享代码不得通过条件编译直接调用 Win32。Windows 与 macOS 分别实现同一个 Platform `Adapter`，测试实现一个纯内存 Adapter。

### 3.2 对外 Interface

以下是 Interface 的行为形状，不预先锁定 Rust trait 语法：

| 领域操作 | 输入/输出 | 不得泄漏的细节 |
| --- | --- | --- |
| `probe_capabilities` | 稳定能力状态 | OS build、HRESULT、插件错误 |
| `capture_current_display` | 取消令牌 → `CaptureSnapshot` | `HMONITOR`、WGC item、D3D texture |
| `replace_shortcut` | 领域快捷键 → 成功/冲突/系统失败 | 虚拟键、原生注册 ID |
| `watch_resume` | 领域事件 sink → 生命周期 lease | power callback、registration handle |
| `get/set_autostart` | bool / 领域错误 | 注册表值、插件 manager |
| `write_png` | Capture Session + PNG bytes → 成功/可重试失败 | `HWND`、`HGLOBAL`、clipboard format ID |
| `choose_png_destination` | 建议名、最近目录、Session → 选择/取消/失败 | Tauri/rfd handle、完整路径日志 |
| `prepare/show/hide_overlay` | Session + 显示器几何 → overlay lease | `WebviewWindow`、`HWND_TOPMOST` |
| `notify` | 稳定通知种类 | Windows toast 原始异常 |

`CaptureSnapshot` 只包含领域值：

- 只读预乘 BGRA8888/sRGB 像素与步幅；
- 物理尺寸与逻辑尺寸；
- 显示器全局物理原点；
- 指针相对物理坐标；
- 已正规化的窗口候选。

窗口候选 ID 是单次 Capture Session 内稳定的 opaque `u64`。它可以由 `HWND` 派生，但调用方不得把它还原为句柄或对它执行系统操作。

### 3.3 内部 Seams

为保持 Locality，内部按系统能力簇建立少量 Seams，而不是为每个 syscall 创建一个 trait：

| 内部 Seam | 责任 |
| --- | --- |
| `CaptureBackend` | 显示器解析、WGC session、D3D11 设备/纹理与颜色转换 |
| `WindowCatalog` | `EnumWindows`、过滤、边界读取、Z 顺序与候选正规化 |
| `ClipboardBackend` | owner、有限重试、`HGLOBAL` 所有权与注册 `PNG` 格式 |
| `WindowsShell` | Tauri tray/plugins、overlay、保存对话框、通知与单实例 |
| `PowerEvents` | suspend/resume 注册和释放 |

所有 `unsafe`、生成 bindings 和 Windows 资源所有权代码集中在 `crates/platform-windows/src/sys/`。Tauri 集成集中在 `shell/`。纯过滤、裁切、错误映射和领域转换放在安全 Rust 文件中。这样代码审查可以在一个 Locality 内检查 ABI 与资源释放，不需要追踪整个 workspace。

## 4. Windows bindings 选择

### 4.1 候选比较

| 候选 | 优点 | 不采用原因/用途 |
| --- | --- | --- |
| `windows` | C 风格、COM、WinRT 的较安全完整投影；namespace feature gating | 适合探索，但运行时直接依赖完整投影不如本项目固定 API 集可审计；不作为直接依赖 |
| `windows-sys` | 低开销原始 C bindings | 不提供适合 WGC/COM/WinRT 所有权的高层投影，容易把 vtable 与 ABI 细节扩散到 Adapter |
| 手写 `extern` / vtable | 表面依赖最少 | 签名、calling convention、GUID、COM 引用计数和错误转换都由项目维护，风险不可接受 |
| `windows-bindgen` 最小 full-fidelity bindings | 精确筛选调用面；保留 COM/WinRT wrappers；生成结果可审查、可复现 | **采用**；增加一个明确的生成步骤和 CI diff 门禁 |

`windows-rs` 当前官方说明把 `windows` / `windows-sys` 视为完整 API projection，并建议新项目优先组合小型 crates 或用 `windows-bindgen` 生成项目特定绑定。`windows-bindgen` 也建议发布型项目提交生成文件、把生成器留在独立未发布工具中，并在 CI 重新生成后检查 diff。[windows-rs 选择说明](https://github.com/microsoft/windows-rs#choosing-your-crates)、[windows-bindgen 文档](https://github.com/microsoft/windows-rs/blob/master/docs/crates/windows-bindgen.md)

### 4.2 生成与升级规则

- `tools/windows-bindings` 是 `publish = false` 的 workspace 工具，只依赖 `windows-bindgen`。
- 过滤清单按能力分组并带注释；至少覆盖 WGC、D3D11/DXGI interop、显示器、窗口、DWM、剪贴板、DPI、置顶和电源通知。
- 生成的 `bindings.rs` 提交到 `platform-windows/src/sys/`，运行时不执行代码生成。
- CI 执行生成器后要求 `git diff --exit-code`。
- 升级 `windows-bindgen`、Windows metadata 或过滤清单必须与生成 diff 同一提交，并重新跑 Windows 真机/集成矩阵。
- 最终 runtime/build/dev 依赖都进入 `Cargo.lock`、SBOM 与 Issue #29 定义的许可证门禁。

## 5. 屏幕捕获

### 5.1 主路径

每次 Capture Session：

1. 立即读取物理指针位置，通过 `MonitorFromPoint` 选择唯一显示器，再以 `GetMonitorInfoW` 取得完整显示器物理边界；不使用 work area，因为截图与浮层必须覆盖任务栏。
2. 使用 `IGraphicsCaptureItemInterop::CreateForMonitor` 为该 `HMONITOR` 创建 `GraphicsCaptureItem`。该入口从 Windows 10 1903 起可用，低于产品最低 build。[CreateForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor)
3. 创建带 BGRA support 的 D3D11 device，把 DXGI device 转为 WinRT `IDirect3DDevice`。
4. 使用 `Direct3D11CaptureFramePool::CreateFreeThreaded`，避免依赖 UI `DispatcherQueue`；buffer count 为 1，只等待第一帧。[CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)
5. 将 `GraphicsCaptureSession.IsCursorCaptureEnabled` 设为 `false`，不把鼠标合成进底图。
6. 第一帧到达后立即停止 session；把 GPU surface 复制到 staging texture，在 worker 线程按 row pitch 读取。
7. 输出紧凑或显式步幅的只读预乘 BGRA8888/sRGB；不把 D3D surface 留给领域层或 WebView。
8. 无论成功、取消或失败都按逆序释放 frame、session、frame pool、texture、context、device 与 COM references。

Microsoft 的 WGC 文档确认 display/window frame、BGRA8 frame pool、`FrameArrived` / `TryGetNextFrame` 流程，并提醒不要在 UI 线程执行重工作。[Screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)

### 5.2 SDR、HDR 与 alpha

- SDR 路径请求 `B8G8R8A8UIntNormalized`。
- Windows HD Color 打开时，WGC 官方文档警告 BGRA8 可能导致过度裁切/发白；该路径使用 `R16G16B16A16_FLOAT`，在 GPU 或经基准证明足够快的 SIMD 路径 tone-map 到 8 位 sRGB。
- 屏幕最终像素视为不透明，输出 alpha 固定为 255，因此天然满足预乘格式。
- HDR 真机在首版迁移验收中至少验证白点、高光不过度裁切、最终 PNG 为 8 位 sRGB；不得静默把浮点线性值截断为 8 位。

### 5.3 不采用备用捕获

| 候选 | 决策 |
| --- | --- |
| GDI `BitBlt` | 不回退；捕获语义、受保护内容、颜色和性能与 WGC 不同，会让错误依机器悄悄变成另一种图片 |
| DXGI Desktop Duplication | 首版不并行维护第二套旋转、输出/适配器与 HDR 路径；仅在未来 ADR 有明确测量证据时重新评估 |
| [`xcap`](https://github.com/nashaofu/xcap) | 不采用；跨平台截图/录屏抽象与本项目分平台 Adapter 重复，不能承载 Snaploom 精确的帧、错误和窗口过滤合同 |
| [`windows-capture`](https://github.com/NiiightmareXD/windows-capture) | 不采用；面向持续帧/录屏的高层 handler 和编码能力超出单帧截图需要，并隐藏了本项目必须控制的资源与错误语义 |
| [`scrap`](https://github.com/quadrupleslap/scrap) | 不采用；API 与依赖世代较旧，且跨平台帧抽象不能替代 WGC/产品窗口语义 |

社区 crate 的许可证即使可准入，也不能代替功能与失败语义审查。许可证是必要条件，不是选型充分条件。

### 5.4 失败与拓扑竞态

- `GraphicsCaptureSession::IsSupported == false`：能力探测返回不可用，tray 仍可用，“开始截图”禁用。
- 捕获 item 创建、D3D device、frame pool 或 GPU copy 失败：返回稳定 `CaptureUnavailable`，显示可理解的失败浮层，关闭后释放 Capture Session 门控。
- frame 超时：返回 `FrameTimeout`；超时必须有界且不阻塞 Tauri 主线程。
- `CreateForMonitor` 返回无效参数、frame `ContentSize` 与目标拓扑不一致或显示器热插拔：重新读取指针和显示器并完整重试一次；第二次失败后不再循环。
- UAC 安全桌面、锁屏、远程/非交互桌面、DRM/受保护内容：失败或产生系统允许的受保护结果；不请求提权，不绕过，不切换捕获后门。
- Windows 正常桌面路径不调用 `GraphicsCaptureAccess::RequestAccessAsync(Borderless)`，也不声明 `graphicsCaptureWithoutBorder`；该能力需要额外 manifest/同意，与 `CAP-02` 冲突。[Borderless access](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureaccess.requestaccessasync)

## 6. 窗口枚举与命中输入

`WindowCatalog` 使用 `EnumWindows` 的返回顺序建立视觉 Z 顺序，并为每个顶层窗口收集：

- `IsWindowVisible`、`IsIconic`；
- owning PID 与 Snaploom 自身 PID；
- `GWL_STYLE` / `GWL_EXSTYLE`；
- owner、class name；
- `DWMWA_CLOAKED`；
- `DWMWA_EXTENDED_FRAME_BOUNDS`，失败时才使用 `GetWindowRect`；
- layered alpha、tool/click-through/no-activate 等状态。

过滤规则与旧合同一致：排除隐藏、最小化、零/负尺寸、自身、系统 UI、child/non-normal、tool/menu/tooltip、cloaked、全透明和 click-through 窗口。`DwmGetWindowAttribute` 是读取 extended frame bounds 和 cloaked 状态的权威入口。[DwmGetWindowAttribute](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute)、[DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)

Adapter 把全局物理 `RECT` 减去捕获显示器原点，裁切到捕获物理尺寸，再输出领域 `WindowCandidate`。命中与排序仍由共享核心的纯函数执行；Windows Module 不复制选择算法。

窗口枚举是增强能力，不得拖垮底图捕获：单个窗口读取失败只丢弃该候选；枚举整体失败返回空候选，用户仍可选择当前显示器全范围。错误只记录稳定事件种类，不记录标题、路径或进程命令行。

## 7. 多显示器与 DPI

### 7.1 进程设置

Windows manifest 在任何 `HWND` 创建前声明 `PerMonitorV2`。Microsoft 推荐用 manifest 设置默认 DPI awareness；PMv2 会提供顶层/子窗口 DPI 通知和非客户区缩放。[Application manifests](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)、[DPI awareness contexts](https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context)

如果 Tauri 或 WebView 在验证中没有继承 PMv2，启动探测必须失败并禁用截图，不能在已有窗口后再调用 `SetProcessDpiAwarenessContext` 伪装成功。

### 7.2 坐标规则

- 显示器、指针、窗口和捕获纹理全部使用物理像素，可包含负的全局原点。
- 每次截图重新解析显示器，不缓存 `HMONITOR` 或全局 DPI；这同时覆盖热插拔和主屏切换。
- 领域逻辑尺寸由物理尺寸和当前 monitor/window scale 得出；X、Y 比例分别计算，即使 Windows 常规配置返回相同比例也不把它写死。
- PMv2 线程不调用 `GetDpiForMonitor`。Microsoft 明确说明该 API 不是 DPI-aware，PM-aware 调用方应使用 `GetDpiForWindow`。[GetDpiForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
- overlay 创建并定位后，用 `GetDpiForWindow` 复核其 monitor DPI；创建前使用 Tauri monitor 的 physical size/origin 与 `scale_factor`。`GetDpiForWindow` 对 per-monitor aware 窗口返回所在显示器 DPI。[GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
- overlay 收到 scale-factor/`WM_DPICHANGED` 或目标显示器在显示前消失时，取消本次显示并重新开始一次完整解析；不在同一 Capture Snapshot 上混用新旧 scale。

## 8. Tauri shell 能力

### 8.1 依赖选择

| 能力 | 选择 | 规则 |
| --- | --- | --- |
| 托盘与菜单 | Tauri 2 `TrayIconBuilder` / menu | Rust 侧创建；启动不创建主窗口；菜单至少截图、设置、开机启动、退出 |
| 全局快捷键 | `tauri-plugin-global-shortcut` | Rust 侧注册，领域层负责替换事务和冲突恢复 |
| 开机启动 | `tauri-plugin-autostart` | 当前用户级；enable/disable 后读回验证，再持久化设置 |
| 保存对话框 | `tauri-plugin-dialog` | Rust API；PNG-only filter、建议名、最近目录、parent overlay |
| 应用单实例 | `tauri-plugin-single-instance` | 必须是第一个注册的插件；callback 只投递领域截图意图 |
| 系统通知 | `tauri-plugin-notification` | 用于快捷键、唤醒、开机启动等合同错误；正文使用本地化稳定文案 |
| 剪贴板 | 直接 Win32 bindings | 不用 `tauri-plugin-clipboard-manager` |

Tauri 官方资料确认 tray/menu、global shortcut、autostart、dialog、single instance 与 Windows 支持；single-instance 官方要求优先注册。[System Tray](https://v2.tauri.app/learn/system-tray/)、[Global Shortcut](https://v2.tauri.app/plugin/global-shortcut/)、[Autostart](https://v2.tauri.app/plugin/autostart/)、[Dialog](https://v2.tauri.app/plugin/dialog/)、[Single Instance](https://v2.tauri.app/plugin/single-instance/)、[官方插件仓库](https://github.com/tauri-apps/plugins-workspace)

### 8.2 快捷键与系统唤醒

- 初始默认值为 `Alt+Shift+A`，领域构造器拒绝无修饰键组合。
- 修改快捷键时先注册候选值；成功后才注销旧值。候选冲突时旧注册保持可用，UI 与持久化值不变化。
- 同值更新是幂等操作，不先注销再制造短暂不可用窗口。
- 插件 callback 只发送按下意图；重复意图由共享 Capture Session gate 静默忽略。
- `PowerRegisterSuspendResumeNotification(DEVICE_NOTIFY_CALLBACK, …)` 监听 `PBT_APMRESUMEAUTOMATIC` / resume 事件，收到后重新建立当前期望快捷键；注册 handle 随 Adapter 释放。[PowerRegisterSuspendResumeNotification](https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-powerregistersuspendresumenotification)
- 唤醒重注册失败时保留“期望快捷键”设置，发送系统通知并允许用户再次修改；不得崩溃或清空设置。

Windows 原生 `RegisterHotKey` 的失败返回和 `MOD_NOREPEAT` 行为是插件实现验收的真机参照，而不是再维护一套并行公开实现。[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

### 8.3 单实例与全局 Capture Session

single-instance 插件只解决 Snaploom App 的 `APP-02`：第二应用实例退出，主实例收到一次 `CaptureIntent::SecondInstance`。callback channel 即使失败，第二实例也不能变成第二个常驻应用。

它不解决 App 与 Capture SDK/Host 的跨进程单 Capture Session。全系统 gate、`Busy` 和 IPC leader 由 Issue #28 设计；Windows Adapter 不额外创建会与 #28 竞争的命名 mutex。

### 8.4 overlay

overlay 按当前显示器创建为按需窗口：

- 初始隐藏，等捕获像素和前端 ready 后一次性显示；
- `decorations = false`、`resizable = false`、`shadow = false`；
- `always_on_top = true`、`skip_taskbar = true`；
- 使用显示器完整物理 origin/size，不按 work area 限制；
- 不使用会把工具设置拆成原生窗口的 popup；所有设置浮层在同一 WebView 根布局内；
- Tauri 配置完成后在内部以 `SetWindowPos(HWND_TOPMOST, …)` 复核最终物理位置、尺寸与 topmost；失败则销毁未完成 overlay 并返回类型化错误。

Tauri WindowConfig 提供 `alwaysOnTop`、`skipTaskbar`、decorations 等配置；`SetWindowPos` 文档定义 `HWND_TOPMOST` 的持久置顶语义。[WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)、[SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

## 9. PNG 剪贴板

### 9.1 为什么不用官方 clipboard plugin

Tauri clipboard plugin 的桌面实现通过 `arboard` 接收 RGBA image。它适合通用图片复制，但不能保证把最终编码 PNG 原字节写入 Windows 注册的 `PNG` 格式；因此不能证明复制与保存字节一致。[插件桌面实现](https://github.com/tauri-apps/plugins-workspace/blob/v2/plugins/clipboard-manager/src/desktop.rs)

### 9.2 写入协议

1. 用 `RegisterClipboardFormatW(L"PNG")` 取得共享的注册格式 ID。
2. 取得当前 Capture Session overlay 的有效 `HWND`，在 bounded worker 操作中调用 `OpenClipboard(hwnd)`。
3. clipboard 被其他进程占用时有限重试；基线采用最多 5 次、20 ms 间隔，总等待有界且不阻塞 Tauri 主线程。
4. `EmptyClipboard` 后用 `GlobalAlloc(GMEM_MOVEABLE)` 分配精确 PNG 长度，`GlobalLock` 复制字节，解锁后 `SetClipboardData`。
5. `SetClipboardData` 成功即把 memory ownership 转给系统；项目不得再 `GlobalFree`。失败则由项目释放。
6. 始终 `CloseClipboard`；只有完整成功后，Enter/完成路径才关闭 overlay。失败保持 Session、选区、标注与历史，可重试。

不能向 `OpenClipboard` 传 NULL。Microsoft 明确说明：NULL 会让 `EmptyClipboard` 把 owner 设为 NULL，继而导致 `SetClipboardData` 失败；有效 overlay `HWND` 只存在于内部 Seam，不进入领域 Interface。[OpenClipboard](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openclipboard)

注册格式、`GMEM_MOVEABLE`、多个格式和 ownership 规则以 Microsoft clipboard 文档为准。[Clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats)、[Clipboard operations](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations)

首版只承诺注册 `PNG` 格式，不同时增加 `CF_DIBV5`。多格式写入会引入“PNG 已成功、兼容格式失败”的部分成功语义；若真实兼容性测试证明有必要，应另开 ticket 设计原子/降级规则。

## 10. 原生保存对话框与开机启动

保存顺序固定为：

1. 提交可提交的文字草稿并生成最终 PNG；
2. 隐藏 overlay 及同窗设置浮层，但不销毁 Session；
3. 用 `tauri-plugin-dialog` 构造 PNG-only save dialog，`set_parent` 指向当前 overlay，设置建议文件名和最近成功目录；
4. `None` 表示用户取消，不是错误；恢复隐藏前的全部编辑状态；
5. 对用户路径补齐/校验 `.png`，写入临时文件并按输出 ticket 的原子策略落盘；
6. 写入成功后更新最近目录并退出，失败则恢复 overlay 和全部状态。

对话框错误与文件写入错误分开分类。路径永不进入隐私日志。

开机启动使用官方 autostart plugin。切换流程先操作系统、再读回验证、最后更新内存/磁盘设置；任一步失败，UI 和持久化值保持旧状态。Windows 只允许当前用户级启动，不写 HKLM、不请求管理员权限。

## 11. 依赖与权限清单

### 11.1 直接依赖

| scope | 依赖 | 用途 |
| --- | --- | --- |
| runtime | `tauri` 2（tray/menu/window 所需 features） | shell 与 overlay |
| runtime | `tauri-plugin-global-shortcut` | 全局快捷键 |
| runtime | `tauri-plugin-autostart` | 当前用户开机启动 |
| runtime | `tauri-plugin-dialog` | 原生保存对话框 |
| runtime | `tauri-plugin-single-instance` | App 单实例 |
| runtime | `tauri-plugin-notification` | 合同要求的系统通知 |
| runtime | `windows-core`, `windows-result`, `windows-strings`, `windows-link` | 最小生成 bindings 的 COM/WinRT、错误、字符串与链接支持 |
| build/dev tool | `windows-bindgen` | 生成提交到仓库的 bindings，不进入发布 runtime |

不直接加入 `xcap`、`windows-capture`、`scrap`、`tauri-plugin-clipboard-manager`、`windows` 或 `windows-sys`。如果 Tauri 的锁定传递依赖包含其中的 Windows 支撑 crate，仍按真实 `Cargo.lock` 审计，不能因为“不是直接依赖”而忽略。

### 11.2 manifest 与权限

- Windows manifest：`PerMonitorV2`、支持 Windows 10/11 的 compatibility 声明、稳定应用标识；不请求 `requireAdministrator`。
- WGC：不声明 borderless capability，不弹系统捕获 picker，不请求额外管理员/捕获授权。
- 安装：用户级安装路径与卸载项遵循 `DIST-02`。
- Tauri capabilities：插件从 Rust 侧调用，不向 WebView 直接授予 global-shortcut、autostart、dialog、clipboard、notification guest commands。前端只能调用 Snaploom 自己的窄命令，并由 Rust 校验 Session 与参数。
- 文件系统：只允许写用户在原生对话框明确选择的目标；最近目录只持久化路径设置，不写日志。
- 网络：此 Adapter 无网络权限。
- 日志：不得记录截图像素、PNG、窗口标题、标注文字、剪贴板内容、完整路径、原始异常 message/stack；只输出稳定事件枚举与错误类型名。

所有直接和传递依赖必须满足 ADR 0001 / Issue #29 的许可、NOTICE、SBOM、锁文件和供应链门禁。

## 12. 类型化错误与降级

| 稳定错误/结果 | 触发 | 用户与状态 | 降级 |
| --- | --- | --- | --- |
| `PlatformUnavailable` | OS/build/WGC 不支持 | 禁用“开始截图”，tray/设置/退出可用 | 无捕获备用 |
| `DisplayUnavailable` | 无交互桌面、显示器消失 | 捕获失败浮层；关闭后可重试 | 拓扑只完整重试一次 |
| `CaptureUnavailable` | WGC/D3D/interop 失败 | 系统捕获失败；释放 gate | 不回退 GDI/DXGI |
| `FrameTimeout` | 首帧未在上限内到达 | 同上 | 无循环等待 |
| `PixelConversionFailed` | copy/tone-map/stride 失败 | 同上 | 不返回半帧 |
| `WindowEnumerationPartial` | 个别/全部候选读取失败 | 底图仍成功 | 丢弃坏候选或返回空候选，桌面回退 |
| `ShortcutConflict` | 候选组合被占用 | 明确状态；旧快捷键仍有效 | 保持旧注册/设置 |
| `ShortcutResumeFailed` | 唤醒重注册失败 | 系统通知；应用继续常驻 | 保留期望设置，允许修改 |
| `ClipboardBusy` / `ClipboardWriteFailed` | 有限重试耗尽/ownership 失败 | 浮层显示复制失败；Session 完整保留 | 允许再次复制 |
| `SaveCanceled` | 用户关闭对话框 | 无错误；完整恢复 | 不是异常 |
| `SaveDialogFailed` / `FileWriteFailed` | 原生对话框/写盘失败 | 保存失败；完整恢复 | 允许换位置重试 |
| `AutoStartFailed` | plugin 或读回验证失败 | 设置反馈/通知 | OS/UI/持久化保持旧值 |
| `OverlayFailed` | 窗口创建、物理定位或 topmost 失败 | 不显示半完成 Session | 销毁窗口并释放 gate |
| `SecondarySignalFailed` | 主实例 callback 投递失败 | 第二实例仍退出 | 不启动第二常驻实例 |

内部可保留 HRESULT/Win32 code 用于分类和测试，但隐私日志不得输出 raw message、完整 stack 或用户数据。

## 13. 验证计划

### 13.1 跨平台自动测试

- fake `PlatformAdapter` 覆盖成功、取消、类型化失败和资源 lease 释放。
- `CaptureSnapshot` 验证步幅、物理/逻辑尺寸、X/Y 比例、负原点、指针相对坐标和只读像素。
- 窗口过滤/裁切/排序使用纯数据参数化，覆盖全部 exclusion flags、同 Z 稳定 ID 与桌面回退。
- 快捷键替换覆盖“新注册成功后注销旧值”“候选冲突保留旧值”“唤醒失败保留期望值”。
- clipboard fake clock/调用表覆盖重试次数、owner 非空、成功所有权转移、每条失败释放 memory/close。
- 保存工作流覆盖先隐藏、parent、取消/失败完整恢复、成功退出、最近目录仅成功后更新。
- 单实例 callback 与 App/SDK 统一 gate 分开测试，确保插件不冒充全系统 gate。
- 错误映射与隐私 logger 自动断言不出现 PNG、标题、标注文字、路径或 raw exception message。

### 13.2 Windows CI

- `x86_64-pc-windows-msvc` Release build、unit/integration tests、Clippy 与格式检查。
- 重新生成 bindings 后 `git diff --exit-code`。
- Cargo.lock 许可策略、SBOM/NOTICE 与禁止依赖门禁。
- 能在无交互 runner 执行的 API 形状/错误映射测试不冒充真实桌面捕获。

### 13.3 Windows 真实桌面自动/人工矩阵

| 维度 | 场景 |
| --- | --- |
| OS | Windows 10 22H2 x64、Windows 11 x64 |
| 显示器 | 单屏、双屏、负原点、主屏切换、横竖旋转、热插拔 |
| DPI | 100%、125%、150%、175%、200%，混合 DPI |
| 颜色 | SDR、Windows HD Color/HDR → 最终 8 位 sRGB |
| 窗口 | 普通、跨屏、最小化、工具、菜单、tooltip、cloaked、透明、click-through、系统 UI、自身窗口 |
| 捕获限制 | 锁屏、UAC 安全桌面、受保护内容、非交互/远程场景 |
| 快捷键 | 默认、自定义、冲突、按键长按、睡眠/唤醒重注册失败 |
| shell | tray-only、菜单、第二实例、开机启动成功/失败、通知 |
| 输出 | PNG clipboard exact bytes、clipboard contention、Ctrl+C 保留、Enter/完成退出、保存取消/失败/成功 |
| overlay | 完整显示器覆盖、任务栏上方、无 taskbar icon、置顶、保存前隐藏、同窗设置浮层 |

clipboard 真机测试从注册 `PNG` 格式读回字节并与保存 PNG 做逐字节比较，同时验证 overlay 关闭后数据仍可粘贴。

### 13.4 性能与资源

- 至少 30 次真实全局快捷键样本，nearest-rank P95 从系统 callback 到 overlay 可交互必须 `≤ 150 ms`。
- tray-only 空闲 working set `≤ 100,000,000 bytes`；测试包含至少一次截图后的回落状态。
- 4K/混合 DPI 捕获分别记录 display resolve、WGC first frame、GPU copy/tone-map、window enumeration、WebView ready，避免只记录总时长而无法定位。
- 至少 20 次复制/保存循环，Windows private memory 尾段增长 `≤ 1%`；额外检查 COM refs、D3D resources、frame events、power registration、shortcut registration 和 overlay window 数量归零/稳定。
- 社区 capture crate 或持久 D3D device 只有在当前方案实测无法达标、且 ADR 记录新 trade-off 后才能引入；不得先加备用再用性能测试解释。

## 14. 实施顺序

1. 建立 `PlatformAdapter` 领域 Interface、值对象、稳定错误和 fake，先通过无 Windows 测试。
2. 建立 `windows-bindgen` 过滤/生成工具、提交 bindings，并启用再生成门禁。
3. 实现显示器解析、WGC 单帧与 D3D11 SDR 路径；随后补 HDR tone-map、拓扑重试和资源压力测试。
4. 迁移窗口枚举/过滤并接共享窗口选择纯函数。
5. 接 tray、single-instance、global-shortcut、resume、autostart 与 notification，验证冲突/唤醒事务。
6. 实现有效 owner `HWND` 的注册 PNG clipboard 与 bounded retry。
7. 实现 parented PNG save dialog 和 overlay 物理定位/topmost/hide-restore 生命周期。
8. 跑完整 Windows 真机、性能、安装与许可门禁；每个合同 ID 绑定自动证据或真机记录。

后续实施 ticket 可以细化文件名和内部类型，但不得削弱本文确定的 Interface、失败语义、权限边界和验收矩阵；若必须改变捕获后端、剪贴板格式或跨进程 gate，先写 ADR 并更新功能等价合同。

## 15. 一手资料

- [microsoft/windows-rs](https://github.com/microsoft/windows-rs)
- [windows-bindgen](https://github.com/microsoft/windows-rs/blob/master/docs/crates/windows-bindgen.md)
- [Windows Graphics Capture screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [IGraphicsCaptureItemInterop::CreateForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor)
- [Direct3D11CaptureFramePool::CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)
- [Windows DPI application manifest](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)
- [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
- [GetDpiForMonitor caveat](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [PowerRegisterSuspendResumeNotification](https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-powerregistersuspendresumenotification)
- [Windows clipboard formats/operations](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats)
- [OpenClipboard owner requirement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openclipboard)
- [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [Tauri 2 System Tray](https://v2.tauri.app/learn/system-tray/)
- [Tauri 2 official plugins](https://github.com/tauri-apps/plugins-workspace)
- [Tauri 2 WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)
- [xcap](https://github.com/nashaofu/xcap)、[windows-capture](https://github.com/NiiightmareXD/windows-capture)、[scrap](https://github.com/quadrupleslap/scrap)
