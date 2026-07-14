# 截图工具桌面技术栈评估

> 调研日期：2026-07-13  
> 目标平台：macOS、Windows  
> 产品范围：全局快捷键触发、当前显示器截图、顶层窗口吸附与自由框选、矩形/箭头/文字/马赛克标注、PNG 另存为、托盘/菜单栏常驻。

## 结论

推荐顺序：

1. **Qt 6 Widgets + C++**
2. **Tauri 2 + Rust + Web 前端**
3. **Flutter Desktop**
4. **Electron**

对这个项目，最困难的不是画矩形、箭头或文字，而是桌面系统集成：屏幕捕获、窗口枚举与命中、透明置顶覆盖层、全局快捷键、托盘、DPI 和 macOS 权限。Qt 6 在这些能力之间的调用链最短，窗口与绘图基础也最成熟，因此是默认推荐。

如果团队已经熟悉 Rust 与 Web 前端，或产品明确要求 MIT/Apache 这类宽松许可证，**Tauri 2 可以反超 Qt 6**。它已官方提供托盘、全局快捷键和窗口配置，主要缺口是截图与窗口吸附仍需自研 Rust 平台层。

Electron 能最快做出演示，但并不能仅凭 `desktopCapturer` 完成微信式窗口吸附；补上窗口矩形、Z-order 和鼠标命中后仍要维护原生模块，同时需承担 Chromium/Node 多进程的资源成本。Flutter 的标注画布很好，但关键桌面能力需要 C++ 与 Swift/Objective-C 插件，削弱了单一 Dart 代码库的优势。

## 核心对比

| 维度 | Qt 6 | Tauri 2 | Flutter | Electron |
| --- | --- | --- | --- | --- |
| 产品级屏幕捕获 | 基础 API 可用；稳定方案仍建议接原生捕获 | 无官方截图插件，需 Rust 调原生 API | 需平台插件或 FFI | 内置 `desktopCapturer`，但窗口吸附信息不足 |
| 顶层窗口吸附 | C++ 直接接 Win32/Cocoa，集成路径短 | Rust 分平台实现，边界清晰 | Windows C++ + macOS Swift/ObjC 插件 | 仍需 native addon |
| 透明置顶覆盖层 | 一等窗口能力，成熟 | 官方窗口配置支持；macOS 真透明有发布限制 | 需修改 runner/插件 | `BrowserWindow` 能力完整 |
| 标注绘图 | `QPainter` 很适合，必要时可上 Qt Quick/OpenGL | Canvas/SVG/WebGL 均可 | `CustomPainter`/Canvas 很强 | Canvas/SVG/WebGL 均可 |
| 托盘 | 官方内置 | 官方内置 | 依赖插件/原生实现 | 官方内置 |
| 全局快捷键 | 需少量原生注册；Qt 可接原生消息 | 官方插件 | 依赖插件/原生实现 | 官方内置 |
| 多屏与 DPI | Qt 6 原生统一抽象较完整 | Web 坐标与物理像素需自己校准 | Flutter 有视图 DPI，但屏幕/窗口系统能力仍需原生层 | Chromium DIP 与系统像素需自行校准 |
| 基础资源开销 | 低到中，Qt Widgets 通常最轻 | 低到中，复用系统 WebView | 中，携带 Flutter engine/Dart runtime | 高，携带 Chromium/Node，多进程 |
| 桌面系统工具成熟度 | 很高 | 较高但更年轻 | 普通桌面 UI 成熟，系统工具能力较弱 | 很高，但原生模块和资源成本明显 |
| 许可证 | LGPLv3/GPLv3/商业，多一层合规决策 | MIT/Apache-2.0 | BSD | MIT |
| 对本项目的总体风险 | **最低** | **较低** | 中 | 中到高 |

资源开销一栏是基于各框架官方架构的定性判断，不是统一硬件上的官方 benchmark。正式决策前仍应分别制作最小原型，测量冷启动时间、空闲内存、首次截图延迟和发布包体。

## 1. Qt 6

### 适配性

Qt 直接提供透明背景、无边框、置顶、鼠标穿透和不接受焦点等窗口属性；这类属性正是全屏遮罩层与浮动工具栏所需要的。多顶层窗口、工具窗口和原生事件循环也是长期稳定的桌面能力。[Qt 窗口标志与属性](https://doc.qt.io/qt-6/qt.html)

`QScreen::grabWindow()` 可以抓取屏幕或窗口，并返回设备像素比信息，但官方明确说明它获取的是屏幕上实际可见的像素；被其他窗口遮挡时会得到遮挡内容，Windows 透明分层窗口也有已知限制。因此它适合原型或降级路径，不应作为产品级捕获的唯一实现。[QScreen](https://doc.qt.io/qt-6/qscreen.html)

推荐做法是保留 Qt 负责窗口、交互和绘图，分别用 Windows Graphics Capture/Win32 与 macOS ScreenCaptureKit/CoreGraphics 完成捕获、窗口枚举和命中。`QAbstractNativeEventFilter` 能接收 Windows `MSG`，官方文档还明确提到系统注册热键消息；`QNativeInterface` 可取得平台句柄，但该接口不承诺跨 Qt 版本的源码或二进制兼容，因此应封装在窄平台层中。[原生事件过滤](https://doc.qt.io/qt-6/qabstractnativeeventfilter.html)、[Native Interfaces](https://doc.qt.io/qt-6/native-interfaces.html)

### 绘图与 DPI

矩形、箭头、文字、选区遮罩和马赛克都适合 `QPainter`。Qt Widgets 默认使用 raster 后端；官方列出的高性能路径包含图像绘制、矩形填充、简单变换、裁剪和 `SourceOver` 合成。建议截图缓冲区使用预乘 ARGB，拖动时只重绘变化区域，马赛克结果按块缓存。[QPainter 性能说明](https://doc.qt.io/qt-6.8/qpainter.html)

Qt 6 在高层绘图和窗口几何中使用设备无关像素，自动处理 Retina 与 Windows 显示缩放；Windows 默认是 Per-Monitor DPI Aware V2。截图的原始像素与 UI 坐标仍需显式通过 `devicePixelRatio` 转换。[Qt High DPI](https://doc.qt.io/qt-6/highdpi.html)

### 托盘、测试与发布

`QSystemTrayIcon` 官方支持 Windows 与 macOS。`QShortcut` 本身是应用上下文快捷键，不是系统全局热键，但 Qt 的原生事件入口使 Win32 `RegisterHotKey` 和 macOS 对应实现容易集中封装。[QSystemTrayIcon](https://doc.qt.io/qt-6.9/qsystemtrayicon.html)、[QShortcut](https://doc.qt.io/qt-6/qshortcut.html)

Qt 提供 GUI 输入模拟、单元测试和 benchmark。发布侧有 `windeployqt`、`macdeployqt`；后者支持签名、Hardened Runtime 和 notarization 相关参数。[Qt Test](https://doc.qt.io/qt-6/qtest-overview.html)、[Windows 部署](https://doc.qt.io/qt-6/windows-deployment.html)、[macOS 部署](https://doc.qt.io/qt-6/macos-deployment.html)

### 包体、许可证与学习成本

Qt 需要随应用分发实际使用的库、平台插件和图像插件。Widgets 方案不需要浏览器引擎或 Dart VM，通常有较低的基础包体和空闲内存，但仍需通过 release 构建实测。[Qt 部署模型](https://doc.qt.io/qt-6/deployment.html)

Qt 采用商业许可证与 LGPLv3/GPLv3 双轨。闭源应用可在完整履行 LGPLv3 要求时动态链接；需要提供所用 Qt 库的对应源码或获取方式，并允许用户替换和重新链接库。部分模块对开源用户仅提供 GPL。若不希望承担这些义务，应评估商业许可证。[Qt 许可证](https://doc.qt.io/qt-6/licensing.html)、[LGPL 义务说明](https://www.qt.io/development/open-source-lgpl-obligations)

C++、CMake、Qt 对象生命周期和两套原生 API 的学习门槛较高，但对本项目来说，复杂度集中在同一种系统语言和一个窗口框架内，后期排查焦点、Z-order、DPI 和消息循环问题更直接。

### 判断

**最适合追求长期稳定、低资源占用和桌面原生行为的方案。** 首版优先 Qt Widgets，不建议为这一小型系统工具一开始就引入 QML；如果后续出现复杂动画或高度定制的动态界面，再评估 Qt Quick。

## 2. Tauri 2

### 适配性

Tauri 允许前端通过 command 调用 Rust，参数与返回值可序列化，适合把截图、窗口枚举、权限和保存封装为小而清晰的 Rust API。[Calling Rust from the Frontend](https://v2.tauri.app/develop/calling-rust/)

官方提供系统托盘、全局快捷键插件，并在窗口配置中提供 `alwaysOnTop`、`transparent` 等能力，因此“常驻托盘 + 快捷键 + 单个全屏覆盖窗口”可以主要使用官方能力完成。[System Tray](https://v2.tauri.app/learn/system-tray/)、[Global Shortcut](https://v2.tauri.app/plugin/global-shortcut/)、[WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)

Tauri 没有官方的跨平台截图插件，也没有可直接提供微信式吸附所需窗口矩形、Z-order 和鼠标命中的统一 API。这一层仍需 Rust 分平台调用 Windows 与 macOS 原生接口。Rust 很适合建立稳定的平台抽象，但从零开发会比 Electron 的 `desktopCapturer` 多一些前期工作。

### 覆盖层与 WebView 风险

macOS 真透明 WebView 窗口需要启用 Tauri 的 private API 相关配置，会影响 Mac App Store 发布选择。这个 MVP 可以避免依赖“透出真实桌面”：先在显示遮罩前捕获当前屏幕，把截图作为覆盖窗口的不透明背景，再在其上绘制半透明遮罩和选区。视觉效果与透明覆盖层一致，也能避免把自己的覆盖窗口捕获进去。[WindowConfig](https://v2.tauri.app/reference/config/#windowconfig)

Tauri 使用系统 WebView，Windows 主要面对 WebView2，macOS 主要面对 WKWebView。好处是不随应用捆绑完整 Chromium；代价是两端 WebView 在字体、Canvas 合成、快捷键和输入法方面必须做真实设备兼容测试。标注画布可用 Canvas/SVG/WebGL，实现难度不高。[Tauri 官方仓库与架构简介](https://github.com/tauri-apps/tauri)

### 资源、许可证与判断

Tauri 自身采用 MIT/Apache-2.0 许可，闭源商业分发更直接。复用系统 WebView 通常使基础包体和内存明显低于 Electron，但最终结果取决于前端依赖与截图缓冲区大小。[Tauri MIT 许可证](https://github.com/tauri-apps/tauri/blob/dev/LICENSE_MIT)、[Tauri Apache-2.0 许可证](https://github.com/tauri-apps/tauri/blob/dev/LICENSE_APACHE-2.0)

**这是最有竞争力的次选。** 如果团队已经熟悉 Rust/TypeScript，或不接受 Qt 的 LGPL/商业许可决策，推荐直接选择 Tauri 2，并把“截图、窗口吸附、权限”视为一个独立 Rust 平台模块，而不是依赖不稳定的社区截图插件。

## 3. Flutter Desktop

### 适配性

Flutter 正式支持编译 Windows 与 macOS 桌面应用，也支持自建桌面插件。调用系统 API 时，Windows 平台代码使用 C++，macOS 使用 Objective-C/Swift，通过 platform channel 或 FFI 与 Dart 通信；Flutter 3.38 起官方推荐 FFI package 作为构建和捆绑原生代码的一种方式。[桌面支持](https://docs.flutter.dev/platform-integration/desktop)、[平台通道](https://docs.flutter.dev/platform-integration/platform-channels)、[插件与 FFI](https://docs.flutter.dev/packages-and-plugins/developing-packages)

Flutter 的 Windows 宿主是传统 Win32 应用，macOS 则由原生 embedder 与 `NSViewController` 承载。透明、置顶、不抢焦点、鼠标穿透、托盘和全局热键都不是统一的 Dart 一等 API，需要修改 runner、依赖插件或自建插件。Windows 官方文档还要求非 Flutter 外部窗口额外转发消息，才能正确参与应用生命周期。[Flutter 架构](https://docs.flutter.dev/resources/architectural-overview)、[Windows 外部窗口](https://docs.flutter.dev/platform-integration/windows/extern_win)

### 绘图与代码共享

`CustomPainter`/`Canvas` 非常适合矩形、箭头、文字和选区，fragment shader 可实现高性能马赛克。官方提醒额外 `saveLayer` 成本较高，应控制图层数量和重绘范围。[CustomPainter](https://api.flutter.dev/flutter/rendering/CustomPainter-class.html)、[Fragment shaders](https://docs.flutter.dev/ui/design/graphics/fragment-shaders)

Dart 标注模型、画布和设置页可以高度共享；但本项目的高风险部分仍要维护 Windows C++ 与 macOS Swift/Objective-C 两套实现。最终是 Dart + 两套平台语言，而不是纯 Dart 项目。

### 资源、测试、许可证与判断

Flutter 应用包含 engine、Dart framework/runtime 和平台 embedder；官方架构说明引擎启动涉及加载共享库、Dart runtime、isolate 与渲染表面。因此其基础开销通常高于精简 Qt Widgets 或 Tauri，这是架构推断，需用 `flutter build windows/macos --analyze-size` 和运行时工具实测。[Flutter 架构](https://docs.flutter.dev/resources/architectural-overview)、[体积分析](https://docs.flutter.dev/perf/app-size)

Flutter 支持桌面 integration tests，并提供 Windows Store/MSIX 和 macOS App Store 的正式发布流程。框架使用宽松 BSD 许可证，但仍需检查第三方 packages 的许可证。[桌面集成测试](https://docs.flutter.dev/testing/integration-tests)、[Windows 发布](https://docs.flutter.dev/deployment/windows)、[macOS 发布](https://docs.flutter.dev/deployment/macos)、[Flutter FAQ](https://docs.flutter.dev/resources/faq)

**适合已经有成熟 Flutter 团队的情况。** 对全新团队，它的 UI 开发体验很好，但截图工具最难的系统层并不会因为选 Flutter 而消失。

## 4. Electron

### 适配性

Electron 内置 `desktopCapturer`、`Tray`、`globalShortcut` 和 `BrowserWindow`，因此可以最快搭出“快捷键触发—全屏选区—Canvas 标注—保存”的 MVP。[desktopCapturer](https://www.electronjs.org/docs/latest/api/desktop-capturer/)、[Tray](https://www.electronjs.org/docs/latest/api/tray)、[globalShortcut](https://www.electronjs.org/docs/latest/api/global-shortcut)、[BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window)

但 `DesktopCapturerSource` 官方结构只提供 source 标识、名称、缩略图、显示器标识和应用图标等信息，没有外部窗口矩形、Z-order 或鼠标命中数据。由此可推断，`desktopCapturer` 能列出/捕获源，却不能单独完成微信式“悬停高亮光标下最上层窗口”；窗口吸附仍需 Win32/Cocoa native addon。[DesktopCapturerSource](https://www.electronjs.org/docs/latest/api/structures/desktop-capturer-source)

### 原生模块与资源成本

Electron 的原生模块需要匹配 Electron 使用的 ABI；官方说明升级 Electron 后通常需要重新构建原生模块。这会增加窗口吸附与原生截图扩展的升级维护成本。[Using Native Node Modules](https://www.electronjs.org/docs/latest/tutorial/using-native-node-modules)

Electron 采用 Chromium 风格的主进程、renderer 进程以及 utility 等多进程模型。由此可预期其空闲内存、冷启动和发布包体通常是四者中最高，但仍应以原型数据为准。[Process Model](https://www.electronjs.org/docs/latest/tutorial/process-model)

Electron 使用 MIT 许可证，Canvas/SVG/WebGL 标注也非常成熟。它最适合需要迅速验证交互、团队只有 Web 技术栈、且不敏感于资源占用的情况。[Electron MIT 许可证](https://github.com/electron/electron/blob/main/LICENSE)

### 判断

**适合原型，不是本项目长期实现的首选。** 一旦窗口吸附迫使项目引入原生 addon，Electron“全部用 JavaScript 快速完成”的核心优势会下降，而 Chromium/Node 的资源成本仍然存在。

## 推荐的 Qt 6 实现边界

若采用首选方案，建议从第一天就保持以下边界：

```text
UI / 交互层（跨平台 Qt Widgets）
├── ScreenshotOverlay：遮罩、框选、调整手柄、工具栏
├── AnnotationDocument：矩形、箭头、文字、马赛克对象模型
├── AnnotationRenderer：QPainter 渲染与导出
└── Settings / Tray：快捷键、开机启动、退出

平台能力接口（纯抽象）
├── ScreenCaptureBackend
├── WindowLocator
├── GlobalShortcutBackend
├── PermissionBackend
└── StartupBackend

平台实现
├── windows/：Win32 / Windows Graphics Capture
└── macos/：Cocoa / ScreenCaptureKit / CoreGraphics
```

关键约束：

- 触发快捷键后先捕获鼠标所在显示器，再显示覆盖层，避免截入自身 UI。
- 首版只吸附顶层窗口；拖动即切换自由矩形框选。
- 选区和标注模型统一使用逻辑坐标，导出时映射到原始物理像素。
- 截图背景保留原始像素；标注采用对象模型，保存时一次性合成，便于撤销/重做。
- 马赛克缓存选区的低分辨率结果，不在每次鼠标移动时重新处理整张 4K/5K 图片。
- macOS 屏幕录制权限、Windows/macOS 混合 DPI 和多屏坐标必须进入首轮端到端测试，而不是发布前补测。

## 建议的技术验证

在正式搭建完整工程前，用候选栈完成一个 2～3 天的垂直原型，只验证高风险链路：

1. 注册目标全局快捷键。
2. 获取鼠标所在显示器的原始像素截图。
3. 枚举并高亮鼠标下最上层顶层窗口。
4. 显示不抢焦点的全屏覆盖层，验证 Retina 与 Windows 125%/150%/175% 缩放。
5. 在 4K/5K 截图上连续拖动选区和马赛克，观察帧率与内存峰值。
6. 保存 PNG，并在未授权、快捷键冲突、显示器热插拔时给出明确错误。

只有当 Qt 6 原型暴露团队无法承受的 C++ 或许可证成本时，再切到 Tauri 2；不要仅以设置页或工具栏的开发速度决定截图工具的底层技术栈。
