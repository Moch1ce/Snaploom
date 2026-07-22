# Tauri 截图浮层与 Canvas 编辑可行性决策

状态：Issue [#25](https://github.com/moch1ce/Snaploom/issues/25) 的实施输入

适用范围：Windows 10 22H2 / Windows 11 x64、macOS 14+ Apple Silicon、Tauri 2 / Rust / TypeScript 重构

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、[截图界面视觉规范](../ui/screenshot-ui.md)、[Windows Rust 平台 Adapter](./windows-rust-platform-adapter.md)、[macOS Rust 平台 Adapter](./macos-rust-platform-adapter.md)

## 1. 最终结论

**Tauri 2 的不透明 WebView 可以承载 Snaploom 的完整截图浮层与编辑交互，生产实现采用“一个可见 Canvas + 同窗 DOM 浮动 UI + 一个可复用临时 `textarea`”方案。**

- 唯一可见 Canvas 负责截图背景、选区外遮罩、窗口吸附预览、自由选区、选区控制柄、矩形、箭头、已提交文字、马赛克、对象选择反馈和最终像素合成。它是截图像素、几何、裁切与 Z 序的唯一渲染真相。
- 工具栏、工具设置、错误状态和尺寸标签使用同一 WebView 根布局内的 DOM 组件；它们不是标注对象，也不参与最终 PNG。不得使用独立原生 Popup 或第二个 WebView。
- 文字编辑期间，在对应 Canvas 几何上短暂叠加一个可复用的系统 `textarea`，只负责焦点、选择、换行及 IME 组合输入；提交后隐藏输入框，文字回到统一对象模型和 Canvas 渲染器。
- React 只承载应用壳、浮动 UI 与声明式状态投影。高频指针状态、命中、手势和逐帧绘制不得依赖每个 `pointermove` 触发 React 重渲染。
- Rust 与平台 Adapter 继续拥有系统捕获、显示器/窗口拓扑、权限、全局快捷键、置顶窗口、剪贴板、原生保存对话框、文件写入、单实例、开机启动、唤醒和通知。WebView 不直接获得这些插件或系统权限。

该决定是有条件的“可实施”，不是把风险原型等同于最终验收。本文第 10 节的真实多显示器、混合
DPI、IME、4K 性能与双平台真机矩阵保留为非阻塞人工建议；缺少结果不阻断发布，但不得冒充已通过，
也不允许静默降低图片分辨率、交互帧率或合同能力。

## 2. 决策依据与证据边界

### 2.1 风险原型已经证明的能力

Issue [#27](https://github.com/moch1ce/Snaploom/issues/27) 的提交 [`b7cf791`](https://github.com/moch1ce/Snaploom/commit/b7cf791f4edcf430289d633dc3c7989c63f1c1a6) 在独立原型分支比较了三种结构，并提供以下一手证据：

| 已证明能力 | 证据 | 可得出的结论 |
| --- | --- | --- |
| 不透明 Tauri 2 WebView 可用无边框、全屏、置顶窗口配置运行，且未启用 `macOSPrivateApi` | 原型 [`tauri.conf.json`](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/src-tauri/tauri.conf.json)、[Tauri `WindowConfig`](https://v2.tauri.app/reference/config/#windowconfig) | 不需要透明 WebView 才能显示截图浮层；截图本身可以成为不透明背景。 |
| 一个 3840×2160 backing store 可绘制背景、遮罩、选区与标注，并在 1920×1080 窗口中正确显示 | [原型实现](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/src/App.tsx)、[视觉证据](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/evidence/macos-arm64-single-canvas.png) | Canvas backing store 与 CSS 显示尺寸可以分离，物理像素画布路径成立。 |
| Variant A/B/C 均能完成矩形、箭头、中文文字、马赛克、撤销与状态切换；Variant A 稳态合成读数约 0～1 ms | [证据记录](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/EVIDENCE.md) | WebView 的基本输入和绘制能力足以继续生产实现；0～1 ms 只解除结构风险，不替代真实 4K P95。 |
| 临时 `textarea` 能接受中文并提交为 Canvas 文字对象 | [原型 `textarea` 路径](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/src/App.tsx) | 系统文本控件只作为 IME Adapter 的方案成立。 |
| 5,453,898-byte 的 4K PNG 能从 WebView 进入 Rust，并由 Rust 校验 PNG signature 与 SHA-256 | [Rust command](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/src-tauri/src/lib.rs) | WebView→Rust 的结果交付可贯通；原型 data URL/Base64 不能进入生产。 |
| macOS arm64 Release `.app` 为 9.7 MB，physical footprint 48 MB、峰值 64 MB | [证据记录](https://github.com/moch1ce/Snaploom/blob/b7cf791f4edcf430289d633dc3c7989c63f1c1a6/prototype/tauri-risk/EVIDENCE.md) | 空壳 + 风险原型低于 50 MB 包体和 100 MB 空闲内存门槛；完整产品必须重新测量。 |
| Windows x64 完成前端构建、`fmt`、`clippy -D warnings` 与 Tauri `--no-bundle` 构建 | [GitHub Actions run 29795580253](https://github.com/moch1ce/Snaploom/actions/runs/29795580253) | 所选结构可跨平台编译；这不是 Windows 桌面交互、DPI 或 WGC 真机证据。 |
| 浮动工具栏曾因 `pointerdown` 冒泡把选区改为 1×1，随后在浮动层根部阻断传播并通过回归 | [修复提交](https://github.com/moch1ce/Snaploom/commit/b7cf791f4edcf430289d633dc3c7989c63f1c1a6) | 输入路由必须是模块级不变量，不能依赖每个按钮的 `click` handler 恰好正确。 |

Canvas 的 backing bitmap 与 CSS 尺寸本来就是两个维度，指针事件也定义了 capture、cancel 与 composed path；生产实现按这些标准行为建立坐标和输入适配，不依赖某个 WebView 的偶然细节。[HTML Canvas](https://html.spec.whatwg.org/multipage/canvas.html)、[Pointer Events](https://www.w3.org/TR/pointerevents3/)

### 2.2 原型没有证明的能力

以下事项仍是明确的生产门禁，不能从上述原型结果外推：

- 真实 WGC / ScreenCaptureKit 捕获、TCC 权限、HDR/SDR 转换、帧方向、步幅和平台窗口目录；
- 指针所在副显示器的显式窗口定位、负原点、混合 DPI、显示器热插拔及 `WM_DPICHANGED` / macOS topology change；
- Windows 任务栏、macOS Dock/菜单栏、Spaces/full-screen Space 上方的最终置顶与焦点；
- Windows WebView2 真机交互、macOS 14 基线 WKWebView、简中/英文输入法的 composition 生命周期；
- 真实工具栏规范、8 个选区控制柄、对象命中/缩放、非破坏性马赛克瓦片缓存、保存/复制恢复语义；
- 真实 4K/5K 显示器上的持续 60 FPS、最终 PNG P95、30 次快捷键 P95 和 20 次资源稳定循环；
- 平台剪贴板的相同 PNG bytes、PNG-only 原生对话框、签名安装包与发布资产；
- macOS 公共 API 对任意其他进程 click-through 窗口的精确识别。该能力按 macOS Adapter 的已知缺口处理，不得宣称原型已解决。

## 3. 为什么选择 Variant A

### 3.1 三种结构的比较

| 结构 | 优点 | 代价 / 风险 | 决定 |
| --- | --- | --- | --- |
| A：单 Canvas | 一个坐标真相；背景、遮罩、对象、控制柄和导出共享顺序与裁切；命中模型可与渲染模型共用；最少层同步 | 需要项目实现命中、光标、无障碍替代和高效脏区绘制；IME 不能只靠 Canvas | **采用**；IME 用临时 `textarea`，操作按钮使用同窗 DOM。 |
| B：背景元素 + 交互 Canvas | 背景图可独立解码，交互 Canvas 表面较轻 | 两个栅格层必须在缩放、拓扑、显示、隐藏、导出时严格同步；容易出现 1px 漂移和时序撕裂 | 不作为生产结构；内部离屏缓存可以存在，但只允许一个可见像素合成面。 |
| C：背景 + SVG/DOM 标注 | DOM 文本与控件容易接入；开发期可视化方便 | 命中、Z 序、裁切、像素化马赛克、DPI 与 PNG 合成横跨 Canvas/SVG/DOM；渲染语义容易分叉 | 不采用；保留 DOM 的范围只限非导出浮动 UI 和临时 IME 输入。 |

Variant A 的关键优势不是 Canvas API 更少，而是它把“屏幕上看到的最终截图”收敛为一个深模块：同一对象模型和绘制计划驱动预览与导出，避免 CSS/SVG/Canvas 三套坐标和裁切规则互相校准。该方向直接承接功能合同 `ANN-01`、`IMG-01` 与 `QA-01`。

### 3.2 “单 Canvas”的精确定义

- **一个可见主 Canvas**：充满截图窗口，backing width/height 等于当前 `CaptureSnapshot` 的物理像素尺寸。
- **允许不可见缓存**：`ImageBitmap`、离屏 Canvas、马赛克 128 物理像素瓦片、字形测量缓存和最终选区导出 surface 可以作为主渲染模块内部实现；它们不得成为第二套可交互或可见坐标真相。
- **同窗 DOM 不算第二渲染器**：工具栏、设置浮层、尺寸标签、错误提示和临时 `textarea` 不进入 Capture Result，统一锚定主 Canvas 的投影几何。
- **导出不截取 DOM**：禁止用 WebView 截屏或 DOM screenshot 生成结果。最终 PNG 只由捕获底图与非破坏性标注对象按统一渲染计划合成。

## 4. 前端深模块与职责边界

### 4.1 `OverlayEditor` 的最小 Interface

生产前端应把截图编辑封装为一个深模块，外部只需要以下行为形状；名称可在实施 ticket 中调整：

| 操作 | 输入 / 输出 | 隐藏的复杂度 |
| --- | --- | --- |
| `mount(snapshotDescriptor)` | 会话 ID、物理/逻辑尺寸、scale X/Y、工作区、指针、窗口候选、只读底图资源句柄 | Canvas 初始化、底图解码、坐标变换、首帧 ready |
| `dispatch(input)` | 正规化 pointer/key/composition/command → 新状态或 effect | 手势阈值、选择、对象命中、历史、文字草稿、取消规则 |
| `render(frameTime)` | 当前状态 → 主 Canvas | 背景、遮罩、对象、控制柄、脏区、瓦片缓存 |
| `projectFloatingUi()` | 当前状态 → 工具栏/设置/textarea DOM 投影 | 浮动定位、冻结规则、选中态、焦点 |
| `composeResult()` | 当前状态 → 物理像素 PNG binary / 稳定失败 | 选区裁切、创建顺序、sRGB、metadata、文字提交 |
| `snapshotState()` | 确定性状态快照 | 自动测试接缝，不暴露 Canvas 调用次数 |
| `dispose()` | 无 | 清零/释放底图引用、bitmap、缓存、history、pointer capture |

Tauri command 只传递稳定领域意图与结果，不把 `CanvasRenderingContext2D`、DOM node、平台句柄、完整路径或插件对象跨边界暴露。Tauri 官方也把 command/event/channel 定义为前后端 IPC；生产参数必须由 Rust 校验，不能把原型的任意 data URL command 当成正式能力。[Tauri：Calling Rust](https://v2.tauri.app/develop/calling-rust/)

### 4.2 React 与高频编辑循环

- React state 可保存工具选择、按钮可用性、设置浮层和用户可见错误；编辑 Session 的 authoritative state 使用独立 store/reducer，并能序列化为测试快照。
- 原始 `pointermove` 只更新待处理输入；每个动画帧最多推进一次状态并绘制一次。不得为每个原始事件重建 React 树。
- 对象命中、选区控制柄、光标枚举和浮动 UI 布局都是纯函数，可在无 WebView 环境参数化测试。
- screenshot bitmap、Canvas context、pointer capture、`ImageBitmap` 与瓦片缓存属于生命周期资源，必须由 Session `dispose` 集中释放。

## 5. 物理像素、逻辑像素与多显示器

### 5.1 坐标真相

每个 Capture Session 固定四类值：

1. `capturePhysical`：目标显示器局部、左上原点、y 向下的物理像素；Canvas backing store 和最终 PNG 使用此空间。
2. `overlayLogical`：WebView 内容区的 CSS 逻辑像素；DOM 浮动 UI 和系统文字框使用此空间。
3. `scaleX/scaleY`：由 Rust `CaptureSnapshot` 的物理/逻辑尺寸独立计算；不能假设 X/Y 相同，也不能把 `window.devicePixelRatio` 当成捕获事实来源。
4. `displayPlacement/workArea`：Rust Adapter 提供的目标 display ID、完整窗口位置和逻辑工作区；Canvas 不自行查询桌面拓扑。

Canvas 设置为：backing `width = physicalWidth`、`height = physicalHeight`，CSS `width/height = overlayLogical`。指针坐标通过当前 surface 的 `getBoundingClientRect()` 归一化后乘物理尺寸并 clamp；样式中的 2/4/8 逻辑像素、4 逻辑像素手势阈值和 UI 间距则通过 Session scale 转换。不得先四舍五入 CSS 坐标再推导物理选区。

### 5.2 多显示器规则

- 一次 Capture Session 只创建一个 overlay，覆盖触发瞬间指针所在的一个显示器；不在所有显示器复制多窗口，也不允许跨屏选区。
- Rust 在创建 overlay 前解析目标显示器的完整 frame、工作区、scale 与全局原点，并显式放置窗口；原型的通用 `fullscreen: true` 不能作为生产定位方案。
- Windows 在任何窗口创建前启用 PerMonitorV2；macOS 使用 display ID + AppKit frame/ScreenCaptureKit 局部像素变换。具体平台坐标遵循两份 Adapter 决策。
- overlay 出现前若目标显示器消失、尺寸/scale 改变，丢弃该 snapshot 并由 Rust 完整重试一次。显示后收到 DPI/topology change 时取消当前 Session 并重新捕获；禁止把旧底图映射到新 scale 后继续编辑。
- overlay 覆盖完整显示器，包括任务栏、Dock 和菜单栏；只有位于选区外部的尺寸标签/工具栏按工作区避让。选区内部工具栏始终按真实选区边缘定位。

### 5.3 DPI 门禁

自动几何矩阵至少覆盖 100%、125%、150%、175%、200%，以及独立 X/Y scale、负全局原点、非整数逻辑边界、跨屏窗口裁切和 8 物理像素最小选区。每个比例都要断言：

- 指针→物理点映射、任意方向框选和 4 逻辑像素手势阈值；
- 8 个控制柄命中、边/角缩放、选区移动与边界 clamp；
- 逻辑线宽、字号和马赛克画笔投影；
- 尺寸标签显示真实物理像素；
- 最终 PNG width/height 精确等于物理选区，不能有 1px 漂移。

## 6. 输入、焦点与事件冒泡不变量

### 6.1 指针路由

生产实现必须在根布局建立统一的 capture-phase 输入路由，而不是让每个按钮自行避免冒泡：

1. `pointerdown` 先检查右键；任意位置右键立即取消 Session，不复制、不保存，包括工具栏和文字输入框。
2. 若事件 `composedPath()` 命中工具栏、设置浮层、错误提示或 `textarea` 等 `data-overlay-ui` 区域，浮动层自行处理，截图 surface 不创建手势、不改变选区、不取得 pointer capture。
3. 只有主 Canvas 的有效左键手势才取得 pointer capture；`pointermove/up/cancel/lostpointercapture` 都由同一手势状态机收束，确保移出窗口或失焦不会留下半成品。
4. `dblclick`、drag、context menu 和触摸/笔事件同样经过该路由；浏览器默认图片拖拽、文本选择与 context menu 在截图 surface 禁用。
5. 同窗 DOM 的锚点和命中区来自统一布局函数；打开设置浮层不得改变主工具栏宽度或按钮位置。

必须保留一条端到端回归：分别在工具栏按钮、分隔线、设置项、textarea、尺寸标签附近执行 `pointerdown/move/up`，断言选区和当前手势完全不变。它直接防止原型曾出现的 1×1 选区回归。

### 6.2 键盘优先级

- 根 capture handler 首先处理 `Esc`：任何状态立即退出，包括文字正在 composition；这服从 `OUT-04`，不采用输入法通常用 Esc 取消候选的默认层级。
- `Command/Ctrl+C` 在文字编辑中也先提交可提交草稿，再生成最终 PNG、复制并保留 Session；不能变成只复制 textarea 选中文字。
- `Command/Ctrl+S` 先提交草稿并交给 Rust 隐藏 overlay、打开原生 PNG 对话框；取消/失败恢复同一状态。
- `Command/Ctrl+Enter` 提交文字；文字编辑时普通 Enter 保留为换行。组合输入期间不得因 `keydown Enter` 提前提交，需同时检查 composition 状态与 `KeyboardEvent.isComposing`。
- 文字编辑中 `Delete/Backspace`、普通字符和输入法事件归 textarea；不得删除当前标注对象。Session undo/redo 在预览、composition 或文字编辑进行中不跳历史。
- 工具快捷键 `R/A/T/M/V` 只在非 editable、非 composition 且没有 Control/Command/Alt 修饰时触发；快捷键激活工具但不打开设置浮层。
- OS 全局截图快捷键始终由 Rust 官方插件处理；WebView 只处理 overlay 已获得焦点后的编辑快捷键。

组合输入使用标准 composition 生命周期，文字布局必须把已提交文本和 pre-edit 串都计入实时边界；不能只监听 `input` 后的最终字符串。[UI Events：Composition Events](https://w3c.github.io/uievents/#events-compositionevents)

## 7. 文字与 IME 的唯一例外

临时 `textarea` 是 Canvas 渲染边界内唯一允许的系统编辑控件：

- 整个应用复用一个实例，Session 中只移动、缩放、显示和隐藏；每个文字对象不创建常驻 DOM 节点。
- 初始紧凑，按 committed text + pre-edit text 的完整排版增长，并裁限在当前选区剩余范围；不得通过水平滚动隐藏开头字符。
- 背景透明、2px 小圆角中性边框、无系统蓝色 focus ring、品牌绿 caret、低透明品牌色 selection，四角小控制点均来自统一主题令牌。
- 编辑已有文字时，Canvas 暂时不画该对象，防止重影；提交、取消或 Session 结束时恢复单一对象状态。
- 失焦原因必须分类：点击画布第一次只提交并退出编辑，第二次才开始新文字；保存/复制先提交；直接取消 Session 不产生 Capture Result。
- 自动换行后的所有行共享一个对象命中区域；提交后的字体、换行、裁切和导出全部回到统一 Canvas renderer。

如果某目标 WebView 的简中/英文 IME、候选窗定位或 composition 序列尚未经过真机矩阵，应继续作为
非阻塞人工建议跟踪且不得声称已验收；不得把文字工具改成不支持 IME、WebView prompt、独立原生窗口
或逐字 DOM 标注作为降级。

## 8. 必须留在 Rust / Platform Adapter 的职责

| 能力 | Rust / Adapter 责任 | 前端只接收 / 发出 |
| --- | --- | --- |
| Capture Session gate | App、第二实例、SDK/Host 共用的单会话门控，Busy/取消/释放 | 会话 ID、可编辑/结束状态 |
| 捕获与权限 | WGC/D3D11、SCK/CoreVideo、TCC、HDR→8-bit sRGB、stride/方向校验 | 只读底图资源描述与稳定错误 |
| 显示器与窗口 | 指针显示器、全局原点、工作区、scale、候选过滤/Z 序、拓扑失效 | 捕获局部物理候选和投影几何 |
| overlay 生命周期 | 创建隐藏窗口、显式目标显示器 frame、topmost、Spaces/taskbar/Dock、focus、hide/show/dispose | `ready`、保存/取消/完成意图 |
| 全局快捷键与 shell | tray/menu、快捷键事务、睡眠唤醒、单实例、通知、开机启动 | 设置意图和稳定结果 |
| 剪贴板 | 写入最终 PNG 原字节、Windows owner/retry、NSPasteboard、失败恢复 | 完整 PNG binary 与 Session ID |
| 原生保存 | 隐藏 overlay、PNG-only 对话框、扩展名、最近目录、安全写入、恢复 | 建议名和最终 PNG binary |
| IPC 与权限 | 参数/Session 校验、窄 command/channel、资源释放；WebView 无插件 guest 权限 | 领域 command，不接触系统句柄/路径/插件 |
| 隐私与日志 | 稳定事件枚举、限制滚动、敏感数据排除 | 不上报像素、文字、路径、原始异常 |

底图和最终 PNG 的生产传输不得使用 Base64/data URL。Rust 应以 Session 管理的有界二进制资源、Tauri binary response/channel 或经实施 ticket 验证的私有自定义协议交付；前端只拿当前会话可读的 opaque 资源。确切 transport 由仓库结构/IPC ticket 固化，但必须满足：无 guest 文件系统能力、无网络监听、会话结束立即失效、限制尺寸、校验物理尺寸和 PNG/buffer 长度、错误不泄露数据。

## 9. 不可接受风险与明确降级

### 9.1 不可接受，失败即阻断

- 为了截图背景启用透明 WebView、`macOSPrivateApi`、私有 CGS/WindowServer API或新增 Accessibility 权限；
- 用 CSS pixel、`devicePixelRatio` 或主显示器 scale 猜测捕获物理坐标；
- 运行时在 Variant A/B/C 之间 fallback，或让预览和导出走不同渲染语义；
- 使用 Base64/data URL 传输生产底图或最终 PNG，或授予 WebView 直接 clipboard/dialog/filesystem/global-shortcut 权限；
- 为未达 4K 门槛而降低截图分辨率、丢弃 pointer input、限制标注数量、把马赛克改为模糊或导出有损格式；
- 原生独立 Popup、第二 WebView、每对象 DOM 控件或 WebView 截屏导出；
- Canvas/context/底图失败后显示半完成浮层，或在 topology 已变化时继续编辑旧 frame；
- 用无 UI CI、合成背景或平均值替代真实 4K P95、混合 DPI、系统 IME、原生对话框与置顶真机证据。

### 9.2 允许且必须可见的降级

| 风险 | 唯一允许的降级 | 禁止行为 |
| --- | --- | --- |
| 窗口目录局部/整体失败 | 保留完整底图、自由选区和整屏回退，候选为空/减少；内部记录稳定类别 | 让候选失败拖垮捕获，或猜测错误窗口 |
| macOS 任意跨进程 click-through 无公共元数据 | 保守过滤并在真机记录已知缺口；不确定候选可排除，用户仍自由框选 | 私有 API、Accessibility 权限、宣称精确等价 |
| 系统捕获/像素转换失败 | 类型化失败浮层，关闭后释放 gate 并允许重试 | GDI/旧 CG API/社区 crate 静默备用 |
| 剪贴板或文件写入失败 | 保留完整 Session、按钮和历史，显示本地化错误并允许重试 | 退出、丢状态或输出另一张图 |
| 保存取消 | 恢复 overlay、工具、选中、草稿和历史；不显示错误 | 创建空文件、丢失焦点后重建 Session |
| emoji 字形缺失 | 允许外观降级但不得崩溃 | 删除文字对象或阻断其他字符 |

## 10. 4K、快捷键与资源门禁

### 10.1 实现约束

- 捕获底图解码后缓存为只读 `ImageBitmap`/等价资源；常规重绘不得重新解码 PNG，也不得全量 `getImageData`。
- 高频绘制由 `requestAnimationFrame` 驱动，每帧最多合成一次；原始 pointer samples 合并，但最终手势轨迹必须插值，不能出现马赛克断裂。
- 使用 dirty rect；马赛克继续按 128 物理像素瓦片缓存，只重建新增、移动、撤销/重做的损伤瓦片，不能用长轨迹总外接矩形使整屏失效。
- Canvas context 参数、线端/连接、字体、image smoothing 与颜色全部由统一 renderer 设置，禁止依赖前一帧残留状态。
- 首帧只有在底图、Canvas、输入路由、主题与 Session state 全部 ready 后才通知 Rust 显示 overlay，避免白屏/闪烁。
- 完成/复制/保存期间冻结重复输出入口；最终合成使用物理选区大小，完成后把 binary 交给 Rust，成功/失败再由领域工作流决定退出或恢复。

### 10.2 硬门槛

| 指标 | 门槛 | 本决策所需证据 |
| --- | ---: | --- |
| 快捷键→overlay 可交互 | nearest-rank P95 ≤ 150 ms，至少 30 次真实全局快捷键 | 双平台分段：Adapter 捕获、底图交付、WebView ready、首帧显示 |
| 4K 交互 | P95 ≤ 16.667 ms 且稳定 60 FPS | 3840×2160 真机，对象拖动/缩放、持续马赛克、选区调整、撤销/重做；记录 dropped frames 与 long tasks |
| 4K PNG | P95 ≤ 1,000 ms | 最终合成并实际写入临时文件；复制/保存必须同一像素/字节语义 |
| 包体 | 每平台安装包 ≤ 50,000,000 bytes | 完整生产依赖与安装包，不使用原型 `.app` 代替 |
| 托盘空闲内存 | ≤ 100,000,000 bytes | macOS physical footprint / Windows working set，至少做过一次截图后回落 |
| 资源稳定 | 20+ 成功循环尾段增长 ≤ 1% | 最后两个连续 5 次窗口中位数；Canvas、bitmap、WebView、平台 frame/texture 同时检查 |

原型的 0～1 ms 合成、48 MB footprint 与 9.7 MB app 只作为早期参考，不从上述任何生产门槛中豁免。

## 11. 测试与验收矩阵

### 11.1 无头状态与几何

- 输入序列→确定性 Session 快照：初始吸附抑制、窗口候选/桌面回退、任意方向框选、4 逻辑像素阈值、8 物理像素最小值、选区替换、移动锁定、8 控制柄。
- 标注对象：创建顺序、最上层命中、矩形/箭头/文字/马赛克的移动/缩放/样式/删除、预览、撤销/重做和选区裁切。
- 浮动 UI 纯布局：标签、下方/内部右下/上方工具栏、工作区避让、标注后冻结、设置浮层不改变主工具栏宽度。
- 坐标属性：100/125/150/175/200%、独立 X/Y scale、负原点、非整数边界、跨屏候选、显示器热插拔失效。
- 输入路由：Canvas、工具栏、设置、textarea、分隔线、pointer capture/cancel、右键、双击、失焦；浮动层永不改变选区。

### 11.2 渲染与 PNG

- 受控字体与固定底图的像素金图：遮罩、品牌绿选区、8 控制柄、矩形、圆角单头箭头、中英文多行文字、像素马赛克、重叠层级与边界裁切。
- 同一渲染计划分别驱动预览和导出；测试不锁 Canvas API 调用次数，但比较最终像素。
- 选区物理尺寸、8-bit sRGB、alpha/premultiplied 处理、无 EXIF/设备/路径/时间元数据。
- 相同 Session 的完成、复制、保存解码像素一致；复制继续编辑，保存取消/失败完整恢复。
- Canvas context 丢失、底图 decode 失败、PNG encode 失败与超尺寸 binary 都返回稳定错误，不显示半帧。

### 11.3 文字与快捷键

- 简中、英文、数字、符号、多行、自动换行、pre-edit 更新、候选上屏、选区、光标、靠边裁限；emoji 只要求不崩溃。
- macOS 系统简中/英文输入法与 Windows Microsoft Pinyin/英文键盘真机；校验候选窗位置、composition 开始/更新/结束和 `isComposing`。
- `Esc` 任意状态立即退出；`Command/Ctrl+C`、`S`、`Enter`、`Command/Ctrl+Enter`、`Z`、`Shift+Z`、`Delete/Backspace` 和 `R/A/T/M/V` 按本文优先级回归。
- 快捷键切工具不打开设置，工具栏点击会打开对应设置；文字编辑第一次点击画布只提交，第二次才新建文字。

### 11.4 Tauri/WebView 双平台端到端

| 维度 | Windows | macOS |
| --- | --- | --- |
| 系统 | Windows 10 22H2 x64、Windows 11 x64 | macOS 14+ Apple Silicon |
| WebView | 系统 WebView2 | 系统 WKWebView |
| 显示器 | 单屏、双屏、负原点、1080p、4K、混合 DPI、热插拔 | 单屏、双屏、4K、5K Retina、混合 Retina、Spaces/full-screen Space、热插拔 |
| overlay | 完整显示器、任务栏上方、PMv2、无 taskbar icon、topmost、保存前隐藏 | 完整 `NSScreen.frame`、Dock/菜单栏上方、screenSaver level、all Spaces、保存前隐藏 |
| 输入 | 鼠标/笔的 pointer capture、默认与自定义快捷键、Pinyin IME | 鼠标、默认与自定义快捷键、系统简中 IME、TCC 前后焦点 |
| 输出 | 注册 `PNG` exact bytes、clipboard contention、原生 save 取消/失败/成功 | NSPasteboard exact bytes、app-modal save 取消/失败/成功 |

Windows CI 的 Tauri build 和 macOS 原型视觉证据继续作为构建回归，但不能替代表中真实桌面项。

### 11.5 合同映射完成条件

实施 ticket 至少要把功能合同中的 `CAP-04/05`、`SEL-01～04`、`UI-01～04`、`ANN-01～08`、`OUT-01～04`、`IMG-01`、`PERF-01`、`QA-01～04` 映射到自动测试、视觉金图、性能样本或双平台真机记录。所有 `MUST` 通过后，才能把本文“可实施”升级为“功能等价已验收”。

## 12. 实施顺序

1. 建立独立、可序列化的 Overlay Session 状态机、坐标值对象、输入枚举与无头测试，不先耦合 React/Tauri。
2. 建立单可见 Canvas renderer、受控底图 fixture 和像素金图；先完成选区/遮罩，再完成对象与最终选区导出。
3. 建立根 capture-phase 输入路由和浮动 DOM 层，首先固化工具栏 pointer 冒泡回归。
4. 接入复用 `textarea` 的 composition Adapter；简中/英文真机验证作为非阻塞人工建议，未验证时不得声称已覆盖真实 IME。
5. 通过窄 Tauri command/channel 接入 Rust Session 资源；删除原型 data URL/Base64，验证参数、尺寸上限、dispose 和隐私边界。
6. 接 Windows/macOS Platform Adapter 的真实捕获、候选窗口、显式目标显示器 overlay、clipboard 和 save workflow。
7. 实现马赛克瓦片缓存、dirty rect、逐帧输入合并，跑真实 4K 性能并按分段数据优化。
8. 完成双平台混合 DPI、置顶、IME、输出、资源循环与安装包矩阵；把证据逐项绑定功能合同 ID。

## 13. 决策摘要

- **选型**：Tauri 2 + TypeScript/React 壳 + 单个可见 Canvas；临时 `textarea` 是唯一文字/IME Adapter。
- **像素真相**：Rust `CaptureSnapshot` 的物理尺寸与 scale，不是 CSS 或 `devicePixelRatio`。
- **系统真相**：捕获、显示器、窗口、快捷键、overlay、clipboard、save 和权限全部留在 Rust/Platform Adapter。
- **输入真相**：根 capture-phase 路由 + pointer capture；同窗浮动 UI 不能把指针手势冒泡给截图 surface。
- **性能真相**：只以真实双平台 4K P95、30 次快捷键和 20 次资源循环为发布门禁；原型读数不豁免。
- **降级原则**：窗口候选可显式退化为空；截图像素、分辨率、IME、输出语义和 4K 门槛不降级。

在这些边界下，WebView 不是平台捕获后端，也不是系统权限代理；它是一个被 Rust 托管、以单一 Canvas 深模块实现的确定性截图编辑器。这既保留 Tauri 的跨平台 UI 价值，也把平台差异和高权限能力约束在可审计的 Adapter 内。
