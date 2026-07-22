# macOS Rust 平台 Adapter 决策

状态：Issue [#22](https://github.com/liuchuana/Snaploom/issues/22) 的实施输入

适用范围：macOS 14+、Apple Silicon（arm64）、Tauri 2 / Rust 重构

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、ADR 0001～0004、`docs/ui/screenshot-ui.md`、[Windows Adapter 决策](./windows-rust-platform-adapter.md) 与现有 Swift bridge

## 1. 结论

Snaploom 应当**替换**现有 724 行 Swift dylib 与 C ABI，而不是继续复用、扩展或逐步包装它。新的 macOS 实现应当是一个纯 Rust、单一项目自有 Mach-O 的深 `PlatformAdapter` **Module**：对上只暴露稳定的领域 **Interface**，对下通过 `objc2` 生成的 Apple framework bindings 接入 ScreenCaptureKit、CoreGraphics、AppKit、CoreMedia、CoreVideo 和 ServiceManagement。

推荐组合如下：

| 能力 | 选择 | 不选择 |
| --- | --- | --- |
| 单帧屏幕捕获 | `SCScreenshotManager.captureSampleBuffer` + `SCStreamConfiguration`，从 `CMSampleBuffer` 的 `CVPixelBuffer` 锁定并复制 BGRA | 已废弃的 `CGWindowListCreateImage`、`CGDisplayCreateImage`、`CGDisplayStream` |
| 窗口目录 | `SCShareableContent.windows` 与 `CGWindowListCopyWindowInfo` 按 window ID 合并 | 私有 CGS / WindowServer API、以 Accessibility 权限换元数据 |
| 权限 | `CGPreflightScreenCaptureAccess` 非提示预检；仅用户动作调用 `CGRequestScreenCaptureAccess` | 启动时或后台自动触发系统提示 |
| macOS 框架绑定 | `objc2-*` 生成绑定，缺失符号才做最小、隔离的补充绑定 | 已弃用的 `cocoa` crate、旧 `core-graphics` crate、高层第三方 ScreenCaptureKit wrapper |
| 菜单栏 / 快捷键 / 单实例 / 通知 | Tauri 官方能力或官方插件，Rust 侧注册 | 自建第二套 Cocoa 菜单和 IPC 协议 |
| 开机启动 | Apple `SMAppService.mainAppService` | LaunchAgent 型通用 autostart 实现 |
| 剪贴板 | `NSPasteboard` 写入原始 PNG bytes | 只支持文本或会重新编码图片的通用剪贴板抽象 |
| 保存对话框 | Tauri 官方 dialog plugin 的 Rust API，PNG 过滤器和建议名 | WebView guest command、自建 NSSavePanel 生命周期 |
| 置顶浮层 | 不透明 Tauri WebView 窗口渲染捕获图像背景 + 受控 `NSWindow` 配置 | 透明 WebView、`macOSPrivateApi`、原生 Popup 或独立编辑器 UI |

这里的主捕获 API 仍是 `SCScreenshotManager` 的**单帧**路径。选择 `captureSampleBuffer` 而不是当前 Swift bridge 使用的 `captureImage`，因为前者直接返回由 IOSurface 支撑的 `CMSampleBuffer`，可取得 `CVImageBuffer` 后校验像素格式、stride、尺寸并做一次受控复制；`captureImage` 需要再把 `CGImage` 栅格化到项目拥有的内存。两者都从 macOS 14 可用，但前者对功能合同要求的只读 premultiplied BGRA8888/sRGB buffer 更直接。[Apple：captureSampleBuffer](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager/capturesamplebuffer%28contentfilter%3Aconfiguration%3Acompletionhandler%3A%29) [Apple：CMSampleBufferGetImageBuffer](https://developer.apple.com/documentation/coremedia/cmsamplebuffergetimagebuffer%28_%3A%29)

`captureImage` 只应保留为开发期对照实验或明确切换的实现，不作为运行时静默 fallback。若主路径失败，Adapter 返回可分类错误并保留当前会话；不能悄悄切到已废弃或语义不同的捕获 API。

## 2. 与功能等价合同的对应

此方案不改变产品流程：快捷键触发后捕获指针所在显示器的当前可见像素，显示同一套 WebView 选区 UI，候选窗口只是辅助吸附数据；PNG 复制和 PNG 保存均由用户明确操作触发。外部 Interface 不暴露 `SCDisplay`、`SCWindow`、`CGImage`、`CVPixelBuffer`、`NSWindow` 或 Objective-C 对象。

关键合同映射：

- 截图像素：只读、8-bit premultiplied BGRA8888、sRGB、top-left、y 向下、物理像素。
- 显示器选择：以触发瞬间的 CoreGraphics 全局指针位置为准；多显示器可有负坐标。
- 候选窗口：同一快照、前到后排序、相对捕获显示器的物理像素矩形；单个候选失败不使整次捕获失败。
- UI：复用 `docs/ui/screenshot-ui.md` 的主题、工具栏和图标；不透明 WebView 窗口以捕获图像作为背景并覆盖完整 `NSScreen.frame`，外部工具栏才使用 work area / `visibleFrame` 避让。
- 会话恢复：权限、复制、保存、快捷键恢复等失败不得丢失仍可用的截图会话。
- 隐私：日志不得包含截图内容、窗口标题、剪贴板数据、保存路径或原始系统错误文本。

唯一不能由 macOS 公共 API 严格兑现的点是“排除任意其他进程的 click-through 窗口”，见第 8 节。这必须作为已知能力缺口进入验收记录，不能用私有 API 假装满足。

## 3. 深 Module 与外部 Interface

### 3.1 设计目标

macOS Adapter 应当是一个深 **Module**：较小的 **Interface** 隐藏权限竞态、Objective-C 所有权、main-thread 约束、异步 completion handler、坐标翻转、Retina 比例、窗口目录合并、签名条件和系统错误映射。其 **Depth** 来自高实现复杂度与低调用复杂度，而不是把每个系统调用包装成一个 public trait。

建议领域 Interface 与 Windows Adapter 对齐。以下是行为形状，不预先锁定 Rust trait、async runtime 或取消令牌的具体语法：

| 领域操作 | 输入/输出 | 不得泄漏的细节 |
| --- | --- | --- |
| `probe_capabilities` | 稳定能力与权限状态 | Objective-C class、selector、NSError |
| `capture_current_display` | 取消信号 → `CaptureSnapshot` | `SCDisplay`、`SCWindow`、`CMSampleBuffer`、`CVPixelBuffer` |
| `request/open_capture_permission` | 用户动作 → 权限状态/稳定错误 | TCC code、系统设置 URL |
| `replace_shortcut` | 领域快捷键 → 成功/冲突/系统失败 | 插件注册 ID、原生 event handle |
| `watch_resume` | 领域事件 sink → 生命周期 lease | notification observer token |
| `get/set_autostart` | bool / 领域错误 | `SMAppService`、原始 status/error |
| `write_png` | Capture Session + PNG bytes → 成功/可重试失败 | `NSPasteboard`、pasteboard type |
| `choose_png_destination` | 建议名、最近目录、Session → 选择/取消/失败 | Tauri dialog handle、完整路径日志 |
| `prepare/show/hide_overlay` | Session + `DisplayPlacement` → overlay lease | `WebviewWindow`、`NSWindow`、`NSScreen` |
| `notify` | 稳定通知种类 | plugin handle、原始系统错误 |

`CaptureSnapshot` 只包含领域值：只读预乘 BGRA8888/sRGB 像素与 stride、物理尺寸、逻辑尺寸、指针相对物理坐标、正规化的窗口候选，以及与本次拓扑绑定的 `DisplayPlacement`。`DisplayPlacement` 保存 opaque display ID、CoreGraphics 全局 point 原点、局部物理尺寸/scale 与 AppKit frame 映射；若共享合同仍需要名为“全局物理原点”的字段，它只能由当前显示器的局部变换派生，禁止把不同 scale 显示器的派生像素原点当成连续桌面像素距离。

### 3.2 内部 seams

内部只在有真正替换价值的地方设置 **Seam**：

- `CaptureBackend`：SCScreenshotManager 调用、TCC 竞态和像素复制。
- `WindowCatalog`：SCK/CG 元数据快照、合并、排序和过滤。
- `CoordinateMapper`：CoreGraphics、ScreenCaptureKit 与 AppKit 坐标之间的纯函数转换。
- `MacShell`：托盘、对话框、通知、单实例和 main-thread 调度。
- `ClipboardBackend`：PNG UTI 与 bytes round-trip。
- `LoginItemBackend`：`SMAppService` 状态机。
- `WorkspaceEvents`：睡眠唤醒及显示器拓扑失效。

框架 `unsafe`、retain/release、nullable 返回、block 生命周期和 selector availability 集中放在 `sys/` 内。`AppKit`/Tauri 窗口操作只从统一的 main-thread executor 进入。不要为每一个 C/ObjC 函数制造 trait；那会增加浅层 **Adapter**，降低 **Locality**，也不会带来有用 **Leverage**。

## 4. Swift bridge：迁移知识，替换二进制

当前 `SnaploomMacOSBridge.swift` 值得保留的是已经验证的产品语义与测试样例，而不是 dylib 形态：

- 迁移：屏幕录制预检/请求流程、SCK 单帧捕获思路、窗口过滤启发式、screen-saver level、all-spaces/full-screen 行为、`SMAppService.mainApp`、唤醒监听、NSPasteboard PNG 和现有 frame orientation 测试。
- 修正：不再硬编码 `-3801`；使用生成 bindings 中的 `UserDeclined` 常量或枚举符号。快捷键改为“新注册成功后才撤销旧注册”。坐标不再把 `NSScreen.backingScaleFactor` 当成 SCK 的事实来源。
- 删除：Swift 源编译步骤、C ABI、托管侧阻塞 completion handler 的 semaphore、`libSnaploomMacOS.dylib` 的复制与单独签名、dylib 搜索路径和运行期加载。

纯 Rust 替换后，发布包中不再存在项目自有动态库，因此无需为它关闭 hardened runtime 的 library validation。应删除 `com.apple.security.cs.disable-library-validation`。默认也不携带旧 .NET 运行形态留下的 `com.apple.security.cs.allow-jit`；必须在签名后的 Tauri/WKWebView 应用上验证编辑 UI 与保存对话框。若实测或上游正式要求某项 entitlement，必须用新的 ADR 记录具体触发条件和最小范围，不能沿用旧包的宽泛例外。

## 5. 框架绑定与依赖原则

首选 crates：

- `objc2-screen-capture-kit`
- `objc2-core-graphics`
- `objc2-core-media`
- `objc2-core-video`
- `objc2-app-kit`
- `objc2-foundation`
- `objc2-service-management`
- `block2`（仅 completion block 需要时）

这些 crate 基于 Apple SDK 头文件生成绑定，允许把 availability、枚举符号和 Objective-C 内存语义集中在一套生态中。[objc2 ScreenCaptureKit](https://docs.rs/objc2-screen-capture-kit/latest/objc2_screen_capture_kit/) [objc2 CoreGraphics](https://docs.rs/objc2-core-graphics/latest/objc2_core_graphics/) [objc2 AppKit](https://docs.rs/objc2-app-kit/latest/objc2_app_kit/) [objc2 ServiceManagement](https://docs.rs/objc2-service-management/latest/objc2_service_management/struct.SMAppService.html)

不选择已经标记 deprecated、并指向 `objc2-app-kit` 的 `cocoa` crate；也不选择维护节奏和系统 API 覆盖受第三方 wrapper 限制的高层 ScreenCaptureKit crate。[cocoa crate 源码声明](https://docs.rs/crate/cocoa/latest/source/src/appkit.rs)

具体版本不在研究文档中猜测。实施时在目标 Rust toolchain 与 macOS 14 SDK 上选择相互兼容的稳定版本，提交 `Cargo.lock`，并通过 arm64 Release 构建验证。`objc2` framework crates 当前页面声明 `Zlib OR Apache-2.0 OR MIT`；Tauri 官方 plugins 通常为 Apache-2.0/MIT，最终以锁定版本的 Cargo metadata 和仓库 LICENSE 为准，产出 license 清单与 SBOM，不以本段替代依赖审计。

## 6. 单帧捕获设计

### 6.1 主路径

1. 调用非提示权限预检；未授权则立即返回 `PermissionNotGranted`。
2. 同一采样点取得全局指针位置、`SCShareableContent`、显示器目录和窗口目录。
3. 用 CoreGraphics 全局坐标选择包含指针的显示器；边界相交时采用确定性的 display ID 排序规则。
4. 创建该 `SCDisplay` 的 `SCContentFilter`；截图在浮层显示前完成。
5. 从 macOS 14 的 `SCShareableContentInfo` 读取 `contentRect` 与 `pointPixelScale`，由它们计算目标物理像素尺寸。`SCDisplay.width/height` 是 points；不能再乘 `NSScreen.backingScaleFactor`。
6. 配置 `SCStreamConfiguration`：目标 width/height、`kCVPixelFormatType_32BGRA`、sRGB、`showsCursor = false`、无音频；macOS 14 保持默认 SDR。`captureDynamicRange` setter 是 macOS 15，14 路径不得无条件调用。
7. 调用 `SCScreenshotManager.captureSampleBuffer`。Apple 将它定义为直接从 stream buffer 捕获一个单帧。[Apple：SCScreenshotManager](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager) [Apple：SCStreamConfiguration](https://developer.apple.com/documentation/screencapturekit/scstreamconfiguration)
8. 从 `CMSampleBuffer` 取得 `CVPixelBuffer`，校验 BGRA、plane layout、width/height 与 stride；以 read-only 标志 lock base address，逐行复制到项目拥有的连续或显式 stride buffer，随后 unlock。
9. 输出固定 top-left、y-down、premultiplied BGRA8888/sRGB 的 `CaptureSnapshot`；所有 framework 对象在离开 `sys/` 前释放。

不假设源 stride 等于 `width * 4`。即使可以整块复制，也必须先验证行跨度和总长度。对于异常像素格式、空 image buffer 或尺寸不一致，返回 `PixelConversionFailed`，不得把未验证内存交给 WebView。

### 6.2 色彩、HDR 与 alpha

产品合同要求最终为 8-bit sRGB。配置明确请求 BGRA 与 sRGB；macOS 14 使用 SCK 默认 SDR。测试必须覆盖 HDR/EDR 显示器上的系统 tone mapping 结果和稳定性，但不把 macOS 15 才可写的 dynamic-range 属性作为 14 路径前提。输出 alpha 语义必须经合成样例确认 premultiplied；若系统返回 opaque frame，仍以合同格式表示，不随显示器改变格式。

### 6.3 取消、超时与陈旧拓扑

Objective-C completion 无法保证底层请求可取消，因此取消令牌只控制结果接收与会话状态；回调晚到时安全丢弃。Adapter 设置有限超时并用一次性 completion state 防止 double resolve。捕获前后比较 topology generation；若热插拔使显示器消失或尺寸变化，丢弃结果并最多重采样一次，仍不稳定则返回 `DisplayUnavailable`。

## 7. 屏幕录制权限与 TCC 竞态

- `CGPreflightScreenCaptureAccess()` 仅查询，不触发提示；它是应用启动、设置页显示状态和快捷键触发前的唯一自动检查。[Apple：preflight](https://developer.apple.com/documentation/coregraphics/cgpreflightscreencaptureaccess%28%29)
- `CGRequestScreenCaptureAccess()` 只允许由用户明确点击“授权”后调用。[Apple：request](https://developer.apple.com/documentation/coregraphics/cgrequestscreencaptureaccess%28%29)
- bundle 保留本地化 `NSScreenCaptureUsageDescription`。
- request 返回后再次 preflight；若系统需要重新启动应用才能生效，UI 明确提示重启，而不是循环请求。
- preflight 为 granted 之后，用户仍可能在捕获进行中撤销权限。若 SCK completion 返回 `SCStreamErrorDomain` 的 `UserDeclined`，映射为 `PermissionRevoked` 并回到权限引导；使用生成枚举符号，不再硬编码 `-3801`。[Apple：SCK errors](https://developer.apple.com/documentation/screencapturekit/error-constants) [objc2：SCError 生成源码](https://docs.rs/objc2-screen-capture-kit/latest/src/objc2_screen_capture_kit/generated/SCError.rs.html)
- 打开 Screen Recording 具体 pane 的 URL 不是稳定公开 API，必须封装成内部 best-effort **Seam**；失败时打开 Privacy & Security 根页或显示手动路径，不把 URL 当成合同能力。

测试权限时使用已安装、固定 bundle ID `com.snaploom.app`、稳定签名身份的 app。TCC 决策与 code identity 相关，不能用每次变化的 `cargo run` 二进制代替发布形态验收。

## 8. 窗口枚举、Z 序与 click-through 限制

### 8.1 目录合并

`SCShareableContent.windows` 提供 `SCWindow` 的 window ID、frame、layer、onscreen 和 owning application；`CGWindowListCopyWindowInfo(.optionOnScreenOnly | .excludeDesktopElements, kCGNullWindowID)` 提供 CoreGraphics window ID、bounds、layer、alpha、owner PID 等字典，并按前到后排列。两份快照按 window ID 合并，以 CG 返回顺序作为 Z 序。[Apple：SCWindow](https://developer.apple.com/documentation/screencapturekit/scwindow) [Apple：CGWindowListCopyWindowInfo](https://developer.apple.com/documentation/coregraphics/cgwindowlistcopywindowinfo%28_%3A_%3A%29) [Apple：optionOnScreenOnly](https://developer.apple.com/documentation/coregraphics/cgwindowlistoption/optiononscreenonly)

过滤规则：

- 排除自身 PID / bundle、桌面元素、已知系统 UI、非零 layer、离屏、零面积和低于阈值的 alpha 窗口。
- 将窗口与目标显示器的物理 rect 相交并 clamp；不跨出 `CaptureSnapshot`。
- 生成会话内稳定的候选 ID；不把窗口标题带出 Adapter 或写日志。
- 单个窗口缺字段、几何异常或转换失败时丢弃该候选；CG 目录整体失败时返回空候选并标记内部 `WindowEnumerationPartial`，屏幕像素仍成功。

Apple 的 required/optional window-list keys 没有 `ignoresMouseEvents`，公开 `SCWindow` 属性也不提供任意其他进程窗口的鼠标穿透状态。[Apple：required keys](https://developer.apple.com/documentation/coregraphics/required-window-list-keys) [Apple：optional keys](https://developer.apple.com/documentation/coregraphics/optional-window-list-keys)

因此，**无法只用公共 API 可靠排除所有 click-through 窗口**。本方案明确：

- 不调用私有 CGS / WindowServer API；
- 不为截图功能新增 Accessibility 权限；
- 使用 layer、alpha、系统 UI bundle、可见性与几何做保守启发式；
- 将 click-through 精确过滤记录为功能等价合同的已知能力缺口，设置手工测试矩阵；若未来 Apple 增加公共元数据，再通过 `WindowCatalog` Seam 替换实现并补 ADR。

Issue #27 的 macOS 风险原型必须把“layer=0、alpha>0、`ignoresMouseEvents=true` 的测试窗口不应成为候选”列为显式可行性门禁。若公共启发式不能通过，就不能宣称 `CAP-04` 已等价；必须回到功能合同/ADR 作出产品取舍，不得静默引入私有 API 或 Accessibility 权限。

这个限制不应阻止整屏捕获，也不能在文档中宣称“已完全检测”。

## 9. 多显示器、Retina 与坐标规范

macOS 有三个容易混淆的空间：

- CoreGraphics / ScreenCaptureKit 全局显示坐标：主显示器左上为原点，y 向下；其他显示器原点可为负。
- AppKit 全局屏幕坐标：主屏左下为原点，y 向上，单位为 points。
- 捕获 buffer：目标显示器局部、左上原点、y 向下、物理 pixels。

AppKit 的默认绘图坐标来自左下原点；`CGDisplayBounds` 给出显示器在全局桌面空间的位置。[Apple：AppKit coordinate systems](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/CocoaDrawingGuide/Transforms/Transforms.html) [Apple：CGDisplayBounds](https://developer.apple.com/documentation/coregraphics/cgdisplaybounds%28_%3A%29)

`CoordinateMapper` 应是无副作用纯模块，并遵循：

1. 指针和显示器选择尽量留在 CoreGraphics 空间，使用 `CGEventGetLocation` 与 `CGDisplayBounds`，不把 `NSEvent.mouseLocation` 直接和 SCK/CG rect 比较。
2. 每次快照读取新的 `SCShareableContentInfo.contentRect`、`pointPixelScale`、SC display rect 与 CG display bounds；不缓存全局 2x，不假设主屏比例适用于所有屏。
3. 以 `contentRect` 和 `pointPixelScale` 推导物理尺寸；转换时独立计算 X/Y scale，允许测试非等比输入，即使现实硬件通常等比。
4. 外接窗口候选的最小边使用 floor、最大边使用 ceil，再 clamp 到 `[0,width] × [0,height]`，避免漏掉边缘像素。
5. overlay 需要 AppKit points 时，在 main thread 从规范 CG rect 做一次显式 y 翻转；创建后再核对 `NSScreen.frame` 与 backing 信息。`NSScreen` 提供 backing conversion API，但它只服务 AppKit window placement，不是捕获像素真相来源。[Apple：NSScreen](https://developer.apple.com/documentation/appkit/nsscreen)
6. 对外保留 opaque display ID 与该显示器自身的 point→pixel 变换。混合 scale 桌面不存在可安全用于跨显示器距离运算的单一“全局物理像素平面”；overlay 定位以 display ID + AppKit frame 为准，候选窗口和指针只转换成捕获显示器局部物理坐标。
7. 捕获期间收到显示配置变化则使 mapper snapshot 失效，并按第 6.3 节重试一次。

纯测试覆盖：1x/2x 混合 Retina、非整数 points 边界、独立 X/Y scale、显示器位于主屏左/右/上/下、负原点、指针恰在边界、跨屏窗口、菜单栏/Dock、热插拔和拓扑重排。

## 10. 系统 Shell 能力

### 10.1 菜单栏与应用形态

使用 Tauri 2 内建 tray/menu，由 Rust 注册菜单事件；保留 `LSUIElement=true`，必要时在 main thread 设置 `NSApplicationActivationPolicyAccessory`，不显示 Dock 图标和普通应用菜单栏。[Tauri：system tray](https://v2.tauri.app/learn/system-tray/) [Apple：Accessory activation policy](https://developer.apple.com/documentation/appkit/nsapplication/activationpolicy-swift.enum/accessory)

托盘菜单只发出领域 intent（截图、设置、退出等），不直接调用捕获底层，以保持单实例、快捷键和菜单走同一用例入口。

### 10.2 全局快捷键与唤醒

使用 Tauri 官方 global-shortcut plugin 的 Rust API。[Tauri：global shortcut](https://v2.tauri.app/plugin/global-shortcut/)

- 拒绝无 modifier 快捷键；默认 `Cmd+Shift+A`。
- 替换必须具备 rollback：先注册新快捷键，成功后才撤销旧快捷键；新注册冲突时旧快捷键继续有效。
- 监听 `NSWorkspace.didWakeNotification`，唤醒后按期望配置重新确认/注册；失败通知用户但不清空配置。[Apple：didWakeNotification](https://developer.apple.com/documentation/AppKit/NSWorkspace/didWakeNotification)
- 回调只发 capture intent，重复事件由用例层会话门控合并。

### 10.3 单实例与通知

single-instance plugin 必须是 Tauri 初始化的第一个 plugin；第二实例 callback 只发“激活/截图” intent，不持有业务状态，也不成为整个 SDK 的全局 gate。[Tauri：single instance](https://v2.tauri.app/plugin/single-instance/)

系统通知使用 Tauri 官方 notification plugin；通知内容只使用本地化稳定文案，不带窗口、路径或错误原文。

### 10.4 开机启动

macOS 14+ 直接使用 Apple `SMAppService.mainAppService`，与当前 Swift 行为及系统 Login Items 管理一致。调用 register/unregister 后必须重新读取 status，只有读回与目标一致才更新 UI 和持久化配置。[Apple：SMAppService.register](https://developer.apple.com/documentation/servicemanagement/smappservice/register%28%29) [Apple：ServiceManagement](https://developer.apple.com/documentation/servicemanagement/)

`SMAppService` 要求应用有有效 code signature：普通未打包开发二进制可能失败，ad hoc 候选与 `stable-unsigned` 稳定包都要验证。系统若要求用户批准，映射为 `AutoStartNeedsApproval` 并引导到 Login Items，不循环注册。macOS 不采用 LaunchAgent 型通用 autostart plugin；Windows 可继续使用其已选方案。

## 11. 置顶截图浮层

截图 UI 必须继续使用同一 WebView root，而不是新建 AppKit 编辑器。创建隐藏、无边框、不可缩放、`transparent=false`、always-on-top 的 Tauri window；窗口展示预先捕获的截图作为不透明背景，避免 Tauri 在 macOS 上为透明 WebView 启用 `macOSPrivateApi`。取得 `WebviewWindow::ns_window()` 后，在 main thread 用 `objc2-app-kit` 配置底层 `NSWindow`。[Tauri：WebviewWindow](https://docs.rs/tauri/latest/tauri/webview/struct.WebviewWindow.html) [Tauri：macOS private API](https://v2.tauri.app/reference/config/#macosprivateapi)

必要配置：

- `level = .screenSaver`，并在测试中确认高于 Dock；
- collection behavior 包含 `canJoinAllSpaces` 与 `fullScreenAuxiliary`，验证普通 Spaces 与全屏 Space；
- frame 精确等于目标 `NSScreen.frame`，覆盖菜单栏和 Dock；
- settings/dialog 出现前隐藏 overlay，完成或取消后按会话状态恢复；
- 不依赖 macOS 不支持的 `skip_taskbar`，应用形态由 `LSUIElement` / accessory policy 保证。

Apple 提供 `NSWindow.level`、screen-saver window level 和 all-spaces collection behavior；这些细节保持在 `MacShell` 内，UI 组件不知道 NSWindow。[Apple：NSWindow.level](https://developer.apple.com/documentation/appkit/nswindow/level-swift.property) [Apple：screenSaver level](https://developer.apple.com/documentation/appkit/nswindow/level-swift.struct/screensaver) [Apple：canJoinAllSpaces](https://developer.apple.com/documentation/appkit/nswindow/collectionbehavior-swift.struct/canjoinallspaces)

浮层与截图始终覆盖完整 `NSScreen.frame`。只有选区外部工具栏、尺寸标签等外部 UI 使用 work area / `visibleFrame` 做避让；选区内部工具栏按 `docs/ui/screenshot-ui.md` 基于真实选区边缘定位，允许覆盖菜单栏或 Dock 区域，不把可见工作区误当成截图边界。

## 12. 剪贴板与原生保存对话框

### 12.1 PNG 剪贴板

使用 `NSPasteboard.general`：先 `clearContents`，再以 PNG pasteboard type / UTI 调用 `setData(_:forType:)` 写入编码完成的**同一份 PNG bytes**；不让 AppKit 或 WebView 重新编码。[Apple：NSPasteboard](https://developer.apple.com/documentation/appkit/nspasteboard) [Apple：setData](https://developer.apple.com/documentation/appkit/nspasteboard/setdata%28_%3Afortype%3A%29) [objc2：NSPasteboard](https://docs.rs/objc2-app-kit/latest/objc2_app_kit/struct.NSPasteboard.html)

写入返回 false 或读回字节不一致时返回 `ClipboardWriteFailed`，保留截图会话。自动化测试用私有/测试 pasteboard 或串行化访问，验证 exact bytes round-trip；日志不打印 bytes。

### 12.2 保存对话框

使用 Tauri 官方 dialog plugin 的异步 Rust builder，由 Rust 侧设置 PNG filter、建议文件名与最近目录。[FileDialogBuilder](https://docs.rs/tauri-plugin-dialog/latest/tauri_plugin_dialog/struct.FileDialogBuilder.html)

调用顺序：隐藏 overlay → 激活 accessory app → 打开 app-modal save dialog → `None` 映射为正常取消 → 无扩展名时补 `.png` → 原子/安全写入 → 恢复会话 UI。不得把 sheet 绑定到已经隐藏的 overlay；Issue #27 必须在 `LSUIElement=true` 的签名 app 中验证对话框可见、获得焦点且取消后能恢复会话。验证失败即阻断该实现选择并回到本决策更新，不静默绕到 WebView 文件选择器。路径只在领域值中短暂存在，不进入遥测或日志。

## 13. 信任模式与发布

### 13.1 测试构建

- 生成 arm64 `.app`，bundle ID 固定为 `com.snaploom.app`，最低系统版本 14.0。
- ad hoc 签名、未公证，验证 app 内所有 Mach-O 均已签名。
- app bundle 中不得出现 `libSnaploomMacOS.dylib`、Swift bridge 或旧 C ABI 产物。
- entitlements 默认不得包含 `com.apple.security.cs.disable-library-validation`、`com.apple.security.cs.allow-jit`、`get-task-allow`；若 Tauri/WKWebView 的签名实测要求例外，先提交新的 ADR，再加入经证明的最小 entitlement。
- 在签名后的 packaged app 上验证 `SMAppService`；不能以裸二进制结果代替。

### 13.2 稳定发布

- 使用 `stable-unsigned`，对 App、Host 与 DMG 施加无发布者身份的 ad hoc 结构签名；library validation 保持启用。
- 不读取 Developer ID 证书、不调用 `notarytool` 或 `stapler`，metadata 明确 `notarized=false`。
- 用 `codesign --verify --deep --strict` 验证包结构，并在 Release notes 固定披露 Gatekeeper 风险、SHA256SUMS 与 GitHub attestations。
- 确认 arm64-only 与最低系统版本；不得描述为 Apple 已认证或已公证。

如果未来取得 Developer ID，可以另开 Issue 重新评估 Apple 认证流程；它不属于当前稳定发布硬门禁。[Apple：安全打开 Mac 上的 App](https://support.apple.com/102445) [Tauri：DMG](https://v2.tauri.app/distribute/dmg/)

删除项目 dylib 后移除 library-validation 例外是硬性验收项；若未来重新引入第三方非系统 dylib，必须另开 ADR 解释签名链，而不是恢复宽泛 entitlement。

## 14. 错误模型、降级与隐私

对外仅返回稳定枚举与可恢复性，不泄露 NSError 文本：

| 错误 | 用户/用例行为 |
| --- | --- |
| `PlatformUnavailable` | 系统版本或必要类不可用；功能禁用 |
| `PermissionNotGranted` | 显示授权引导，不触发自动 prompt |
| `PermissionRevoked` | 捕获中撤销；回权限页并保留可恢复状态 |
| `DisplayUnavailable` | 拓扑重采样一次，仍失败则结束本次 |
| `CaptureUnavailable` / `CaptureTimeout` | 通知失败，不切旧 API |
| `CaptureCanceled` | 静默结束当前请求 |
| `PixelConversionFailed` | 丢弃异常 buffer，不向 UI 传数据 |
| `WindowEnumerationPartial` | 继续整屏截图，候选为空/部分 |
| `ShortcutConflict` | 保留旧快捷键 |
| `ShortcutResumeFailed` | 保留设置并通知用户 |
| `ClipboardWriteFailed` | 保留截图会话，可重试 |
| `SaveCanceled` | 正常取消并恢复 overlay |
| `SaveDialogFailed` / `FileWriteFailed` | 恢复会话并显示稳定文案 |
| `AutoStartNeedsApproval` | 引导 Login Items |
| `AutoStartFailed` | 读回系统状态，不虚报成功 |
| `OverlayFailed` | 不进入不可退出的捕获态 |
| `SecondarySignalFailed` | 第二实例退出并给出可诊断稳定代码 |

底层日志只允许：稳定错误码、框架/方法类别、OS 版本、匿名尺寸/耗时。禁止：截图像素、窗口标题/应用名、剪贴板、保存路径、原始 NSError `localizedDescription`、系统设置 URL 参数。若调试需要底层 code，只记录 domain 的 allowlist 分类和数值 code，发布日志不得记录任意 `userInfo`。

降级原则：权限失败不捕获；捕获失败不走 deprecated API；窗口元数据失败退化为无吸附候选；通知失败不影响主流程；保存/复制失败保留会话；坐标/拓扑不一致最多重采样一次。

## 15. 验证策略

### 15.1 自动化

1. **领域合同测试**：用 fake `PlatformAdapter` 覆盖捕获、取消、复制、保存、权限、错误恢复，与 Windows 测试共享用例。
2. **坐标属性测试**：参数化混合 Retina、负原点、上下左右布局、非整数边界、独立 X/Y scale、指针边界、跨屏窗口、热插拔重试。
3. **像素测试**：把现有 Swift `FrameOrientationTest` 的 1×2 red/blue fixture 移植到 Rust；额外验证 BGRA、premultiplied alpha、stride、top-row 方向、sRGB 标记和 4K 行复制。
4. **窗口目录测试**：伪造 SCK/CG 字典，验证 ID 合并、前到后 Z 序、缺字段、self/system/layer/alpha/geometry 过滤和候选局部失败。
5. **TCC 集成测试**：preflight 不提示；授权/拒绝；preflight 后 capture 前撤销映射为 `PermissionRevoked`；断言日志无 NSError 原文。
6. **Shell 测试**：快捷键替换 rollback、冲突、睡眠唤醒恢复；second-instance intent；通知失败不影响主流程。
7. **剪贴板/对话框**：PNG exact bytes round-trip；`LSUIElement` app-modal dialog 可见且可聚焦；cancel/failure 恢复 overlay；保存扩展名与文件 bytes。
8. **开机启动**：ad hoc candidate 与 `stable-unsigned` packaged app 各验证 register/unregister/read-back；未批准状态映射正确。
9. **浮层**：level 高于 Dock、frame 等于完整 `NSScreen.frame`、菜单栏/Dock 区域可覆盖、普通 Spaces/全屏 Space、混合 Retina。

click-through 必须有手工矩阵：至少透明悬浮控件、HUD、带 `ignoresMouseEvents` 的自有测试窗口和常见系统 overlay。验收结果应明确“公共 API 无法证明任意其他进程 click-through”，不能把启发式通过写成精确能力通过。

### 15.2 性能与可靠性

- 快捷键到 overlay 可见：30 个样本，P95 ≤ 150 ms。
- idle physical footprint ≤ 100 MB。
- 记录 1x、Retina 4K、混合 Retina 的 content query、capture、pixel copy、window merge、WebView handoff 分段耗时。
- 连续 20 次捕获后，稳定空闲 physical footprint 尾部增长 ≤ 1%；检查 autorelease pool、block 与 CVPixelBuffer lock/unlock 配对。
- completion 超时、late callback、取消和退出竞态运行 stress test。

### 15.3 CI / 发布门禁

- arm64 macOS 14+：`cargo fmt --check`、`clippy -D warnings`、unit/integration、Tauri Release build。
- 锁定依赖的 license/SBOM 审计；拒绝 `cocoa`、旧 capture API 或未批准高层 wrapper。
- 扫描源码/符号，禁止 `CGWindowListCreateImage`、`CGDisplayCreateImage`、私有 CGS。
- 扫描 app bundle，禁止 `libSnaploomMacOS.dylib`；验证 Mach-O architecture、deployment target、签名与 entitlements。
- 实机门禁覆盖 macOS 14 基线与当前支持版本；权限/Spaces/Retina/SMAppService 不能只靠无 UI runner。

## 16. 实施顺序

1. 建立共享 `PlatformAdapter` Interface、领域值、fake 与错误枚举；先移植坐标和像素方向测试。
2. 建立 `objc2-*` 依赖与最小 availability/arm64 Release 编译门禁；不引入 Swift dylib。
3. 实现 `captureSampleBuffer` → CVPixelBuffer BGRA copy；完成 TCC revoked race 和超时/cancel。
4. 实现 `WindowCatalog` 的 SCK/CG 合并、过滤与 click-through 能力缺口记录。
5. 实现 `CoordinateMapper` 与 mixed-Retina/negative-origin/hotplug 集成。
6. 接入 tray、single-instance、global shortcut、wake、notification 和 `SMAppService`。
7. 接入 NSPasteboard、dialog 和 NSWindow overlay；按截图 UI 规范做视觉/交互验收。
8. 删除 Swift bridge、C ABI、swiftc/framework build、dylib packaging 与旧 entitlements。
9. 完成 ad hoc 候选包、`stable-unsigned` 稳定包、Gatekeeper 风险披露与安装后验收。

迁移期间的历史回退点是 ADR 0004 规定的 `dotnet-final` tag；`main` 不恢复双实现或长期双轨。删除 bridge 应在 Rust Adapter 合同测试和最小端到端捕获通过后一次完成，后续回退通过 Git 历史完成，而不是在运行时保留旧 backend。

## 17. 关键一手资料

- Apple ScreenCaptureKit overview：[ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit)
- Apple 单帧 API：[SCScreenshotManager](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager)
- Apple 示例：[Capturing screen content in macOS](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos)
- Apple filter geometry：[SCContentFilter.contentRect](https://developer.apple.com/documentation/screencapturekit/sccontentfilter/contentrect)
- Apple 权限：[CGPreflightScreenCaptureAccess](https://developer.apple.com/documentation/coregraphics/cgpreflightscreencaptureaccess%28%29)、[CGRequestScreenCaptureAccess](https://developer.apple.com/documentation/coregraphics/cgrequestscreencaptureaccess%28%29)
- Apple 窗口目录：[CGWindowListCopyWindowInfo](https://developer.apple.com/documentation/coregraphics/cgwindowlistcopywindowinfo%28_%3A_%3A%29)
- Apple 已废弃捕获 API：[CGWindowListCreateImage](https://developer.apple.com/documentation/coregraphics/cgwindowlistcreateimage%28_%3A_%3A_%3A_%3A%29)
- Apple 登录项：[ServiceManagement](https://developer.apple.com/documentation/servicemanagement/)
- Apple 未认证应用打开说明：[Safely open apps on your Mac](https://support.apple.com/102445)
- Tauri 官方插件入口：[Tauri plugins](https://v2.tauri.app/plugin/)
- Rust framework bindings：[objc2 ScreenCaptureKit](https://docs.rs/objc2-screen-capture-kit/latest/objc2_screen_capture_kit/)、[objc2 CoreGraphics](https://docs.rs/objc2-core-graphics/latest/objc2_core_graphics/)、[objc2 AppKit](https://docs.rs/objc2-app-kit/latest/objc2_app_kit/)
