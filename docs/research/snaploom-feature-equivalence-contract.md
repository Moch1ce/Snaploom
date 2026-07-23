# Snaploom 功能等价合同

状态：Issue [#24](https://github.com/liuchuana/Snaploom/issues/24) 的迁移前基线  
适用范围：`dotnet-final` 标签之后的 Tauri 2 / Rust / TypeScript 重构  
合同对象：Snaploom 应用与复用同一交互能力的 Capture Host

## 1. 目的与使用方式

本文件把最后一版 .NET/Avalonia Snaploom 的外部可观察行为固化为迁移合同。旧实现从主分支删除后，新实现不得依赖“回头运行旧代码”才能知道正确结果；每条 `MUST` 条款都必须能直接转换为自动测试、像素金图、性能基准或目标系统真机验收。

证据路径的 `dotnet-final:` 前缀表示未来 `dotnet-final` 标签中的文件。创建该标签前，这些路径均对应本文件所在提交的仓库路径。证据引用用于说明合同来源，不表示必须保留 .NET 类型、Avalonia 控件或当前内部模块结构。

迁移验收遵循以下判定：

- `MUST`：功能等价的硬门槛；未通过不得删除对应迁移 ticket 的旧行为基线。
- `SHOULD`：已存在的兼容行为；除非后续 ADR 或明确产品决定覆盖，否则应保留。
- “自动”：可在无交互 CI 中验证。
- “视觉”：通过确定性截图或像素/几何断言验证。
- “真机”：必须在相应操作系统与真实桌面会话验证，不能用 mock 或 CPU 代理替代。

## 2. 证据优先级与冲突解析

出现冲突时按“更晚、更具体、且被实现或测试证明”的来源优先：

1. 父路线 [#21](https://github.com/liuchuana/Snaploom/issues/21) 已确定的 Tauri、Capture Host 与 Capture SDK 方向，以及 `CONTEXT.md` 的领域语言。
2. `docs/ui/screenshot-ui.md` 对截图浮层的当前产品规则。
3. `dotnet-final` 的外部行为测试与实际产品代码。
4. `docs/testing/`、`docs/distribution/` 与发布流水线中的验收规则。
5. 较早的 `docs/snaploom-v1-spec.md`；仅在不与以上来源冲突时作为补充。

已解析的冲突如下，迁移时不得重新解释：

| 主题 | 旧描述 | 最终合同 |
|---|---|---|
| `Esc` | v1 PRD 描述为逐层取消 | `Esc` 在截图浮层任意状态立即退出，不复制、不保存；右键同样立即退出。证据：`docs/ui/screenshot-ui.md`、`ScreenshotAnnotationShortcutTests.EscapeClosesTheOverlayImmediately*`。 |
| `Command/Ctrl+C` | v1 PRD 描述为复制后退出 | 复制与保存使用同一最终 PNG，但浮层、选区、标注和历史保留。证据：`docs/ui/screenshot-ui.md`、`ScreenshotCompletionWorkflowTests.CopyKeepsEditingStateAndSaveWritesTheIdenticalPng`。 |
| 固定浅色 | v1 PRD 允许常规窗口或工具栏跟随系统主题 | 应用窗口与截图浮层固定浅色，不提供主题切换；旧 `theme` 设置被忽略。证据：`docs/ui/screenshot-ui.md`、`App.Initialize`、`AppSettingsTests.LegacyThemePreferenceIsIgnored`。 |
| 工具栏底部空间不足 | v1 PRD 只描述翻转到上方 | 选区能容纳时先放入选区右下角，距选区实际右/下边各 16 逻辑像素，即使跨 Dock/任务栏也不按工作区裁剪；不能容纳才翻到上方。证据：`docs/ui/screenshot-ui.md`、`ScreenshotFloatingUiLayoutTests`。 |
| 保存对话框 | 早期描述未规定置顶浮层与对话框的关系 | 打开原生保存对话框前必须隐藏截图浮层；取消或失败后恢复全部编辑状态。工具设置不得使用独立原生 Popup。证据：`docs/ui/screenshot-ui.md`、`ScreenshotCompletionWorkflowTests.SaveDialogIsNotCoveredAndCancelRestoresTheOverlay`。 |

## 2.1 父路线不可变边界

### ARCH-01 授权与二进制边界（MUST）

- 应用与独立 Capture Host 使用 `GPL-3.0-or-later`；Capture SDK、稳定 C ABI、C/C++、C#、Swift 官方封装与示例使用 `Apache-2.0`。
- Apache-2.0 Capture SDK 只能单向依赖自身协议/启动接口，不得包含、静态或动态链接 GPL 实现；Capture Host 保持独立进程和独立二进制，通过版本化本地 IPC 调用。
- 应用安装包与 SDK 分发资产必须在同一版本 GitHub Release 发布，但保持独立文件、独立许可证材料和可分别集成/安装的边界。Capture Host 最终随哪一分发资产交付、完整资产清单与供应链门禁由 #28/#31 细化，不得削弱本条边界。
- 验证：依赖方向自动门禁、实际二进制/包内容审计、许可证/NOTICE/SBOM 检查，以及同一 Release 的资产与版本一致性检查。
- 证据：父路线 #21、Issue #29、`docs/adr/0001-open-source-license-boundaries.md`、`0003-isolate-capture-host.md`、`docs/research/tauri-open-source-license-audit.md`。

## 3. 应用生命周期与入口

### APP-01 常驻形态（MUST）

- 启动后只显示 Windows 通知区域图标或 macOS 菜单栏图标，不创建主窗口、不显示在 Dock/任务栏中；进程只在显式“退出”时结束。
- 菜单至少提供：开始截图、设置、开机启动、退出。截图平台不可用时仅禁用开始截图，其余管理入口仍可用。
- 验证：自动检查启动计划；双平台真机检查可见入口和无主窗口。
- 证据：`dotnet-final:src/Snaploom.App/App.cs`、`ApplicationLaunchPlan.cs`、`AppLifecycleTests.AppStartsInTheTrayAndTheExitMenuShutsItDown`。

### APP-02 单实例（MUST）

- 同一用户会话只运行一个应用实例；第二实例向主实例发送“开始截图”意图后退出。
- 第二实例信号失败不得启动第二个常驻应用。
- 验证：自动启动两实例并观察主实例收到一次意图；双平台真机复核。
- 证据：`dotnet-final:src/Snaploom.App/SingleInstanceCoordinator.cs`、`SingleInstanceCoordinatorTests.SecondaryInstanceSignalsCaptureAndDoesNotBecomePrimary`。

### APP-03 单 Capture Session（MUST）

- 应用快捷键、托盘入口、第二实例和 Capture SDK 最终都受同一个 Capture Session 门控。
- 已有 Capture Session 时，应用侧重复触发静默忽略，不叠加浮层、不闪烁工具栏；SDK 侧的 `Busy` 外部语义由 #28 细化。
- 验证：核心并发测试与双平台真机重复触发。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotActivationGate.cs`、`ScreenshotActivationGateTests.RepeatedActivationIsIgnoredUntilTheSessionEnds`、父路线 #21。

### APP-04 默认与可配置快捷键（MUST）

- Windows 默认 `Alt+Shift+A`；macOS 默认 `Command+Shift+A`。
- 自定义快捷键必须至少包含一个修饰键。注册冲突时保持并恢复上一组可用快捷键，并向用户显示明确状态。
- 系统睡眠唤醒后重新注册当前快捷键；恢复失败时发系统通知并写隐私日志。
- 验证：平台适配自动测试；双平台真机检查冲突、唤醒和前台应用不受限。
- 证据：`dotnet-final:src/Snaploom.Platform.Abstractions/IScreenshotPlatform.cs`、`ScreenshotHotKeyManager.cs`、`HotKeyLifecycleTests`。

## 4. 捕获、权限与窗口吸附

### CAP-01 当前显示器捕获（MUST）

- 以触发瞬间指针所在显示器为唯一捕获范围，不支持跨显示器选区。
- 捕获当前可见像素，不合成鼠标指针，不恢复被遮挡窗口内容。
- 捕获帧同时携带物理尺寸、逻辑尺寸、显示器全局原点、指针相对坐标、步幅和窗口候选。
- 工作格式为只读的预乘 BGRA8888/sRGB；物理/逻辑比例可在 X、Y 方向独立计算。
- 验证：平台适配自动测试；单屏、双屏、混合 DPI 真机。
- 证据：`dotnet-final:src/Snaploom.Core/CapturedFrame.cs`、`CapturedFrameTests`、`CapturedScreen`、`MacOSNativeBridgeTests`、`DesktopPlatformTests`。

### CAP-02 平台权限（MUST）

- macOS：捕获前检查“屏幕与系统音频录制”权限；未授权时显示本地化说明，可继续请求权限或打开对应系统设置，并提示必要时重启应用。
- Windows：正常桌面捕获不要求管理员权限或额外捕获授权。
- 锁屏、UAC 安全桌面、DRM/受保护内容不可绕过，不为截图请求提权。
- 验证：macOS 14+ 与 Windows 10/11 真机；自动测试只检查无提示的权限查询与错误映射。
- 证据：`dotnet-final:src/Snaploom.App/ScreenshotController.cs`、`MacOSNativeBridgeTests.NativeBridgeReportsScreenCapturePermissionWithoutPrompting`、`DesktopPlatformTests.WindowsDesktopCaptureDoesNotRequireElevatedPermission`、`docs/testing/macos-t02-validation.md`、`windows-t03-validation.md`。

### CAP-03 捕获错误恢复（MUST）

- 权限拒绝进入权限引导；系统捕获失败显示可理解的“无法截取当前屏幕”浮层；意外异常显示通用失败信息。
- 失败浮层关闭后释放门控，用户可再次开始 Capture Session。
- 错误信息和日志不得包含截图像素、标注文字、剪贴板内容或完整个人路径。
- 验证：自动注入三类失败；平台真机复核权限拒绝和受保护桌面。
- 证据：`dotnet-final:src/Snaploom.App/ScreenshotController.cs`、`CaptureFailureOverlayWindow.cs`、资源文件与 `PrivacyLogTests`。

### CAP-04 窗口候选与桌面回退（MUST）

- 只吸附可见、未最小化、有正尺寸的顶层正常窗口；排除 Snaploom 自身、系统 UI、工具/菜单/提示窗口、透明、点击穿透、隐藏、最小化、非正常窗口。
- 以视觉 Z 顺序命中最上层候选；同 Z 顺序用稳定 ID 保证确定性。越过当前显示器的窗口先裁切到显示器。
- 指针在无候选的桌面区域时，单击选择当前显示器全范围。
- 验证：核心自动测试与双平台真机窗口矩阵。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotWindowSelection.cs`、`ScreenshotWindowSelectorTests`、Windows/macOS 窗口枚举实现。

### CAP-05 初始吸附抑制（MUST）

- 浮层刚出现且指针仍在捕获时的原位置时，不显示窗口吸附预览，也不允许原地单击直接吸附；指针实际移动后才启用吸附。
- 尚无选区且没有吸附预览时显示像素放大镜；只显示放大像素和品牌绿十字，不显示 RGB/HEX。
- 参考几何：卡片 132×132 逻辑像素、采样直径 55 物理像素、距指针 16、距屏幕边 8，约 2.4× 最近邻放大。
- 验证：Headless 指针序列测试与视觉金图。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotSelectionCanvas.cs`、`ScreenshotPixelInspector.cs`、`ScreenshotSelectionCanvasTests.WindowSnapWaitsUntilThePointerLeavesItsCaptureOrigin`、`PixelInspectorZoomTests`。

## 5. 选区交互

### SEL-01 框选阈值与坐标（MUST）

- 单击/拖动判定阈值为 4 逻辑像素；自由选区最小尺寸为 8×8 物理像素。
- 支持从任意方向拖出选区；所有点限制在当前捕获帧内。无效小选区回到可重新选择状态，不能复制或保存。
- 尺寸标签显示真实物理像素，例如 `514 × 295`。
- 验证：核心状态测试、100/125/150/175/200% DPI 参数化测试。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotPointerGesture.cs`、`ScreenshotSession.cs`、对应 Core 与 Headless 测试。

### SEL-02 选区显示与缩放（MUST）

- 选区内保持捕获画面原始亮度；外部为半透明黑色遮罩；选区使用 2 逻辑像素品牌绿描边。
- 四角与四边中点共 8 个品牌绿控制点。拖动边只改对应边，拖动角改相邻两边，结果不小于最小尺寸且不越界。
- 验证：状态几何自动测试和视觉金图。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotSelectionCanvas.cs`、`ScreenshotSessionTests.EverySelectionHandleResizesTheExpectedEdges`。

### SEL-03 整体移动与标注锁定（MUST）

- 尚未开始任何标注编辑时，选区内部悬停显示四向移动光标；拖动移动整个选区，尺寸不变且不能移出捕获画面。
- 一旦开始过标注编辑，即使撤销或删除到对象为空，也永久禁用该 Capture Session 的整体移动选区。
- 调整选区边缘仍允许；已有标注保持原屏幕坐标，超出新选区的部分在画布和 Capture Result 中共同裁切。
- 验证：核心与 Headless 自动测试。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotSelectionCanvasTests.SelectionStaysLockedAfterItsAnnotationsAreUndone`、`ResizingSelectionFromTopLeftKeepsAnnotationAtItsScreenPosition`。

### SEL-04 替换选区（MUST）

- 仅在尚未完成选区的可框选状态开始新选区；选区完成后，选区外蒙版显示系统禁用光标，单击、拖拽和绘制不得替换选区或改变标注、文字草稿及撤销/重做历史。
- 双击选区或选区外蒙版均完成当前选区；绿色选区边框及控制点仍优先响应缩放。
- 取消保存不是替换选区，必须保留上述全部状态。
- 验证：核心/Headless 状态快照。
- 证据：`dotnet-final:src/Snaploom.App/ScreenshotSelectionCanvas.cs`、`ScreenshotSelectionCanvasTests.CompletedSelectionMaskIsInertAndUsesTheSystemNoCursor`、`ScreenshotCompletionWorkflowTests.DoubleClickingTheMaskCopiesTheCurrentSelectionAndExits`、`CancelingSavePreservesSelectionAnnotationsAndHistory`。

## 6. 截图浮层视觉合同

### UI-01 固定浅色与品牌令牌（MUST）

- 应用窗口和截图浮层固定浅色，不跟随系统深浅色，不提供主题设置。
- 核心颜色：品牌绿 `#07C977`、危险红 `#FF4D4F`、主文字 `#202124`、浮层表面 `#FAFAFA`、边框 `#D8DADF`、Hover `#F2F2F2`、选中 `#EDEDED`、遮罩 `rgba(0,0,0,115/255)`。
- 视觉常量必须来自统一主题层；新前端可以更换实现名称，但不得散落复制。
- 验证：设计令牌单元测试与确定性截图。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotUiTheme.cs`。

### UI-02 尺寸标签与工具栏（MUST）

- 尺寸标签：近黑圆角底、白字，默认位于选区左上方 4 逻辑像素；上方空间不足时放入选区；外部时距工作区至少 8。
- 工具栏：44 高，水平内边距 14、无额外垂直内边距；表面、1px 边框、8px 圆角与轻阴影。
- 每个工具命中区 40×40，图标 18；相邻工具中心距 40。Hover 背景 28×28/4px 圆角，选中背景 24×24/4px 圆角，不能给整个按钮铺底。
- 组间 1px 分隔线，左右各 10；普通操作深色、退出红色、完成绿色。工具顺序为矩形、箭头、文字、马赛克｜撤销、重做、保存｜退出、完成。
- 只有有效选区完成后才显示工具栏并启用选区操作；复制或保存进行中临时禁用可能产生重复输出的操作。
- 验证：几何单元测试与视觉截图。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotUiTheme.cs`、`ScreenshotToolbar.cs`。

### UI-03 浮动 UI 定位（MUST）

- 工具栏默认在选区下方 8 并与右边缘对齐；外部位置与工作区边缘至少 8。
- 底部不足且选区可容纳时进入选区右下，距选区实际右/下边各 16；不因 Dock/任务栏裁切内部位置。不能容纳才置于选区上方 8。
- 用户开始首个标注后，尺寸标签与主工具栏位置冻结，不随标注或选区调整跳动。
- 验证：纯几何参数化测试，覆盖 Dock、任务栏与所有屏幕边。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotFloatingUiLayout.cs`、`ScreenshotFloatingUiLayoutTests`、`ScreenshotAnnotationShortcutTests.ToolbarPositionFreezesAfterAnnotationStarts`。

### UI-04 工具设置浮层（MUST）

- 点击矩形、箭头、文字或马赛克工具时，在对应按钮下方显示带三角指示的同窗白色设置浮层；打开不得改变主工具栏宽度或按钮位置。
- 快捷键切换工具只激活，不主动打开设置浮层。
- 设置浮层必须位于截图窗口根布局中，不使用独立原生 Popup；保存对话框期间随截图浮层一起隐藏，恢复后还原。
- 未实现的工具若未来暂时出现，必须明确禁用，不得伪装可点击。
- 验证：Headless 结构/几何测试与保存对话框真机。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotAnnotationShortcutTests.ToolSettingsOpenWithoutChangingTheMainToolbarWidth`、`RectangleSettingsFlyoutOpensWhenTheToolbarIsAttached`。

## 7. 标注对象与编辑

### ANN-01 对象模型与渲染顺序（MUST）

- 矩形、箭头、文字、马赛克均为相对选区的非破坏性对象；捕获底图在 Capture Session 内只读。
- 后创建对象位于更上层；命中测试只选择视觉最上层对象。
- 预览、完成对象和 Capture Result 必须共享同一渲染语义，不能为每个对象创建独立 UI 控件或修改底图。
- 验证：核心状态与最终位图金图。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotAnnotations.cs`、`ScreenshotAnnotationRenderer.cs`、`SelectionPngEncoder.cs` 及对应测试。

### ANN-02 工具与快捷键（MUST）

- `R` 矩形、`A` 单头箭头、`T` 文字、`M` 像素化马赛克、`V` 选择；有 Control/Command/Alt 修饰时不触发工具快捷键。
- 矩形、箭头或马赛克从按下到释放形成一个对象；零长度矩形或箭头不提交。马赛克点按或拖动都形成非空轨迹，并插值避免断裂。
- 验证：Headless 输入事件序列。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotAnnotationShortcutTests.RAndAAndTSelectAnnotationToolsAndVReturnsToSelection`、`ScreenshotSelectionCanvasTests`。

### ANN-03 样式预设（MUST）

- 色板固定六色：红 `#FF4D4F`、黄 `#FADB14`、绿 `#07C977`、蓝 `#1677FF`、黑 `#202124`、白 `#FFFFFF`；默认红。
- 矩形/箭头线宽 2、4、8 逻辑像素，默认 4；矩形仅实线轮廓；箭头仅单头实线，圆角连接和端点。
- 文字字号 16、24、32，默认 24，使用系统无衬线字体。
- 马赛克画笔 16、32、64，默认 32；对应旧实现像素块强度参考为 8、12、16。只允许像素化，不允许高斯模糊。
- 最近颜色、线宽、字号和马赛克画笔写入本机设置，并作为下一 Capture Session 默认值。
- 验证：核心构造约束、设置往返与像素金图。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotAnnotations.cs`、`AppSettings.cs`、`ScreenshotPreferenceTests`。

### ANN-04 选择、移动与缩放（MUST）

- 选择工具从上到下命中最上层对象；点击空白取消对象选择。
- 矩形显示 8 个控制柄，可整体移动、四边缩放、四角缩放并改颜色/线宽。
- 箭头显示起点和终点控制柄，可整体移动、拖动任一端点改变长度方向并改颜色/线宽。
- 文字可移动、改颜色/字号、双击重新编辑；马赛克可移动、删除、改强度，但不可缩放。
- 矩形、箭头和文字的整体移动不得越出截图选区；矩形四边及四角缩放仅在该矩形已经选中后生效，首次命中未选中矩形时只执行对象选中与整体拖动。
- 绘制矩形/箭头工具仍激活时，单击已有同类型对象优先选中，自动返回选择工具并打开对应设置；样式修改立即作用于已选对象。矩形工具不得误选马赛克。
- 验证：核心/Headless 输入序列与历史快照。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotAnnotationSessionTests`、`ScreenshotAnnotationShortcutTests.ClickingADrawnRectangleSelectsItAndAppliesStyleChanges`。

### ANN-05 光标反馈（MUST）

- 绘制工具为空白区域时用十字光标；文字工具为空白区域用文本光标。
- 可移动对象在悬停、按下和整体拖动期间都使用四向移动，不使用 `pointer`；文字工具悬停已有文字也使用移动，离开后恢复文本光标。
- 只有已选中矩形的边柄才使用水平/垂直缩放，角柄使用对应对角缩放；已选中箭头端点使用端点拖动光标。
- 验证：Headless 指针反馈枚举与平台真机视觉复核。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotSelectionCanvasTests.HoverFeedbackDistinguishesSelectionObjectsHandlesAndEditedBlankSpace`。

### ANN-06 文字输入与 IME（MUST）

- 文字工具单击空白开始多行输入；单击已有文字直接编辑；按住已有文字超过统一 4 逻辑像素阈值则移动，不进入编辑。
- 自动换行后的每一行都属于同一文字对象的命中范围。文字工具下第一次点击其他空白只提交并退出当前编辑，第二次点击才开始新文字。
- 临时系统文字输入框不是标注控件。它从紧凑尺寸开始，透明背景、2px 圆角中性边框、四边统一 8px 内边距、四个小圆控制点、品牌绿插入光标、低透明品牌色选择，不显示系统蓝色焦点框；输入行高与最终渲染保持一致。
- 编辑框按已提交文本与 IME 预编辑串的完整内容实时增长；候选确认时宽度不得立即收缩，避免输入法确认后光标沿用失效布局；普通退格或删除使行数减少时，高度必须按当前文本实时回缩。候选上屏前不得横向滚动隐藏开头，靠近选区边缘时裁限在选区内。内部水平和垂直滚动条必须禁用且不可见，鼠标滚轮不得滚动输入内容；选区不足以容纳完整单行及四边内边距时不得启动文字输入。
- 文字对象的绿色外框仅在按下并拖拽文字期间显示，松开后隐藏；外框必须复用灰色编辑框的完整 chrome 几何，编辑期间不得叠加显示。
- `Command+Enter`/`Ctrl+Enter` 提交；空白文本不创建对象。编辑中的已提交对象从统一渲染层暂时隐藏，提交后回到统一渲染器。
- 验证：Headless 文本、换行、IME 预编辑、焦点视觉与边界测试；中英文输入法真机。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotAnnotationShortcutTests` 的文字编辑系列、`ScreenshotAnnotationRendererTests.MultilineTextWrapsAndIsClippedToItsLogicalMaximumWidth`。

### ANN-07 撤销、重做与删除（MUST）

- 对象创建、移动、缩放、样式修改、文字编辑和删除均进入命令历史。
- `Command/Ctrl+Z` 撤销，`Command/Ctrl+Shift+Z` 重做；现有 `Command/Ctrl+Y` 重做别名 `SHOULD` 保留。
- `Delete` 或 `Backspace` 删除已选对象。新变更清空重做栈；预览、文字编辑或变换进行中不执行历史跳转。
- 验证：核心历史状态与 Headless 快捷键测试。
- 证据：`dotnet-final:src/Snaploom.Core/ScreenshotAnnotations.cs`、`ScreenshotOverlayWindow.cs`、`ScreenshotAnnotationSessionTests.CreationMoveResizeStyleAndDeleteAreUndoableAndRedoable`。

### ANN-08 马赛克缓存语义（MUST）

- 轨迹和像素强度以非破坏性对象保存；最终效果只改变轨迹覆盖区域，外部像素不变。
- 实时预览按 128 物理像素瓦片缓存，只重建新增、移动、撤销/重做实际损伤的瓦片；不能用整条长轨迹外接矩形失效导致 4K 性能回退。
- 验证：瓦片损伤自动测试、金图和 4K 基准。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.Rendering/ScreenshotMosaicTileCache.cs`、`ScreenshotMosaicTileCacheTests`、性能报告。

## 8. 完成、复制、保存与 Capture Result

### OUT-01 完成并退出（MUST）

- 点击绿色完成、双击有效选区或按 `Enter`：提交可提交的文字草稿，生成最终 PNG，复制到系统剪贴板，成功后退出浮层。
- Capture Result 是选区底图与所有标注合成后的同一 PNG；应用模式和 SDK 模式不得返回不同图像。
- 复制失败时不退出，恢复操作按钮并显示本地化失败状态。
- 验证：Headless 工作流与最终 PNG 字节/像素断言，双平台剪贴板真机。
- 证据：`CONTEXT.md`、`docs/ui/screenshot-ui.md`、`ScreenshotCompletionWorkflowTests.EnterCopiesPhysicalPixelsAndExitsAtTwoHundredPercentDpi`、`DoubleClickingTheSelectionCopiesAndExits`。

### OUT-02 复制但继续编辑（MUST）

- `Command+C`/`Ctrl+C` 生成与保存完全相同的最终 PNG 并写剪贴板，但保留浮层、选区、标注、选中态和撤销/重做历史。
- 文字编辑中触发时先提交当前草稿，再复制并继续编辑会话。
- 验证：同一状态分别复制、保存，比较 PNG 字节或解码像素与全部编辑状态。
- 证据：`docs/ui/screenshot-ui.md`、`ScreenshotCompletionWorkflowTests.CopyKeepsEditingStateAndSaveWritesTheIdenticalPng`。

### OUT-03 PNG 另存为（MUST）

- `Command+S`/`Ctrl+S` 或保存按钮打开平台原生对话框，只允许 PNG；默认名为本地时间 `Snaploom_yyyy-MM-dd_HH-mm-ss.png`。
- 打开对话框前隐藏置顶截图浮层及同窗设置浮层，避免遮挡。后续对话框从最近成功选择的目录开始。
- 无论用户输入何种扩展名，最终写入 `.png`。
- 成功后退出浮层；取消时恢复原截图、选区、标注、活动工具、对象选择、文字草稿与撤销/重做历史；保存失败也恢复并允许重试。
- 验证：Headless 状态快照、平台原生对话框真机、不可写目录失败测试。
- 证据：`docs/ui/screenshot-ui.md`、`dotnet-final:src/Snaploom.App/ScreenshotOverlayWindow.cs`、`ScreenshotCompletionWorkflowTests`。

### OUT-04 取消与资源释放（MUST）

- `Esc`、右键或红色退出按钮立即取消 Capture Session，不复制、不保存、不产生 Capture Result。
- 浮层关闭后释放捕获帧、渲染位图、马赛克缓存、对象/历史和光标资源；包含截图数据的托管缓冲区在释放时清零。
- 只有成功复制完成或保存完成才计为成功输出周期；取消/关闭不计入性能循环样本。
- 验证：自动生命周期与缓冲区清零测试，20+ 次真机循环。
- 证据：`docs/ui/screenshot-ui.md`、`CapturedFrameTests.DisposeZerosTheOriginalPixelBuffer`、`ScreenshotCompletionWorkflowTests.ClosingWithoutCopyOrSaveDoesNotCompleteAnOutputCycle`。

### IMG-01 最终图像格式（MUST）

- PNG 尺寸等于选区真实物理像素；例如 200% DPI 下逻辑 20×10 导出 40×20。
- 输出为 8 位 sRGB、预乘处理正确，不携带 EXIF、设备、用户、完整路径、捕获时间或其他私有元数据。
- 矩形、箭头、文字、马赛克按创建顺序合成并裁切到选区；重叠时后创建对象在上。
- 系统字体缺失 emoji glyph 时允许外观降级，但不得崩溃；受控字体下中英文与多行文字需有稳定金图。
- 验证：解码 PNG 头/色彩空间/元数据、DPI 参数化像素金图。
- 证据：`dotnet-final:tests/Snaploom.Rendering.Tests/SelectionPngEncoderTests.cs`、`ScreenshotAnnotationRendererTests.cs`。

## 9. 设置、隐私、日志与更新

### CFG-01 设置持久化（MUST）

- 仅持久化：快捷键、开机启动、最近标注样式、最近保存目录、语言；不持久化截图、标注文字或历史。
- 设置使用版本化 schema；缺失、损坏、无权限或未来 schema 安全回退平台默认值，不阻止应用启动。写入失败时当前会话仍使用用户刚选择的有效值。
- 开机启动默认关闭；平台修改失败时 UI 状态和持久化值不应假装成功，并通知用户。
- 验证：设置往返、无效/未来配置、权限失败自动测试。
- 证据：`dotnet-final:src/Snaploom.App/AppSettings.cs`、`AppSettingsTests`、`TrayMenuViewModelTests`。

### CFG-02 本地化（MUST）

- 提供简体中文与英文；默认跟随系统语言，显式选择可切换；不支持的系统文化回退英文。
- 所有用户可见文本来自资源，不在业务逻辑硬编码。平台权限用途说明同时提供英文与简体中文。
- 验证：资源键一致性和 fallback 自动测试；双语言真机抽查。
- 证据：`dotnet-final:src/Snaploom.App/Resources/`、macOS `InfoPlist.strings`、`AppSettingsTests.ResourceFallbackUsesEnglishForUnsupportedCultures`。

### PRIV-01 本地处理与隐私日志（MUST）

- 截图、标注、合成、复制和保存完全在本机完成；不上传截图、不收集遥测、不保留截图历史。
- 日志最多 5 个文件，每个最多 2 MiB；只记录 UTC 时间、级别、稳定事件枚举和异常类型名。不得记录异常消息/堆栈中的敏感内容、像素、标注文字、剪贴板内容或完整路径。
- 设置提供打开日志目录和清除日志；日志读写失败不得使应用崩溃。
- 验证：敏感异常注入、滚动上限与清除失败自动测试。
- 证据：`dotnet-final:src/Snaploom.App/PrivacyLog.cs`、`PrivacyLogTests`。

### UPDATE-01 仅手动检查更新（MUST）

- 构造服务或后台空闲时不访问网络；只有用户点击“检查更新”才访问 GitHub Releases，不自动下载或安装。
- 忽略草稿，允许当前测试版流水线发布的 prerelease，选择最高有效 `vX.Y.Z`；Release 页面必须是本仓库、同 tag 的 HTTPS GitHub URL。
- 明确区分：有更新、已最新、网络失败、限流、响应无效；只有用户再次点击“打开 Release”才调用默认浏览器。
- 仓库若仍为私有，匿名 API 不能作为面向外部用户的可用更新通道。
- 验证：HTTP 边界自动测试，不使用真实网络。
- 证据：`dotnet-final:src/Snaploom.App/UpdateCheckService.cs`、`UpdateCheckServiceTests`、`SettingsUpdateWorkflowTests`、`docs/distribution/test-release-process.md`。

### ERR-01 错误模式与可恢复状态（MUST）

| 失败 | 用户可观察结果 | 必须保留的状态与恢复 |
|---|---|---|
| 启动时快捷键冲突 | 系统通知引导用户从托盘打开设置或查看日志 | 应用继续常驻，设置与退出仍可用；用户可注册新快捷键 |
| 睡眠唤醒后快捷键重注册失败 | 独立的系统通知与稳定日志事件 | 不崩溃；设置仍显示当前期望快捷键，可再次修改 |
| macOS 捕获权限缺失/拒绝 | 本地化权限引导，可继续授权或打开系统设置 | 不创建空白 Capture Session；关闭引导后可重试 |
| 系统捕获失败 | 可理解的捕获失败浮层 | 关闭后释放门控，可开始新的 Capture Session |
| 意外捕获异常 | 与系统失败区分的通用错误文案 | 不泄露异常内容；关闭后可重试 |
| 保存对话框取消 | 无错误提示 | 完整恢复选区、标注、草稿、活动工具、选中态与历史 |
| PNG 写入失败 | 浮层内显示“保存失败” | 同上，并允许换位置再次保存 |
| 剪贴板写入失败 | 浮层内显示“复制失败” | 不退出、不计成功输出，恢复按钮并允许重试 |
| 开机启动修改失败 | 系统通知/设置反馈 | UI 与持久化值保持失败前状态 |
| 设置损坏、未来 schema 或读取失败 | 不打断启动 | 使用当前平台安全默认值；不得覆盖为半有效状态 |
| 设置写入失败 | 不使当前交互崩溃 | 当前进程采用有效新值；下次启动允许回到磁盘旧值/默认值 |
| 日志打开、清除或写入失败 | 设置中显示日志操作失败，后台记录尽力而为 | 不影响截图主路径，不递归记录敏感异常 |
| 更新网络失败、限流、响应无效 | 三种独立本地化状态 | 保持设置窗口可用，不自动重试、下载或打开浏览器 |
| 平台能力不可用 | 通知用户并禁用“开始截图” | 托盘、设置、日志与退出仍可用 |

以上错误文案允许在新 UI 中调整措辞，但错误类别、是否退出、状态保留范围、重试入口和隐私边界必须等价。证据：`dotnet-final:src/Snaploom.App/` 的控制器、设置、日志与更新实现，以及对应 Integration/Headless 测试。

## 10. 双平台差异合同

| 项目 | Windows | macOS | 验证 |
|---|---|---|---|
| 支持范围 | Windows 10 22H2 x64、Windows 11 x64 | macOS 14+ Apple Silicon arm64 | 包元数据 + 真机 |
| 默认快捷键 | `Alt+Shift+A` | `Command+Shift+A` | 自动 + 真机 |
| 捕获权限 | 普通用户直接捕获，不提权 | 屏幕与系统音频录制权限，引导系统设置/重启 | 真机 |
| 捕获实现语义 | 当前显示器、物理像素、窗口候选 | 同左，Retina 坐标与帧方向统一 | 平台集成 + 真机 |
| 保存 | Windows 原生另存为，只允许 PNG | macOS `NSSavePanel`，只允许 PNG | 真机 |
| 剪贴板 | 注册 `PNG` 格式；被占用时有限重试，失败可恢复 | `NSPasteboard` PNG；系统拒绝时失败可恢复 | 真机 |
| 常驻入口 | 通知区域 | 菜单栏，应用不显示 Dock 图标 | 真机 |
| 开机启动 | 当前用户 Run/等价用户级机制 | 登录项 | 自动 + 真机 |
| 系统唤醒 | 重新注册快捷键 | 重新注册快捷键 | 真机 |
| 安全边界 | 不捕获/绕过 UAC 安全桌面 | 不绕过系统屏幕录制权限 | 真机 |

跨平台差异只能出现在系统入口、权限、原生对话框、系统通知、路径与打包；选区、标注、Capture Result、快捷键语义和错误恢复必须一致。

## 11. 性能与资源合同

### PERF-01 硬门槛（MUST）

| 指标 | 合同门槛 | 口径 |
|---|---:|---|
| 应用/安装包 | 每个平台应用安装包 ≤ 50,000,000 bytes | `1 MB = 1,000,000 bytes` |
| 托盘空闲内存 | ≤ 100,000,000 bytes | macOS physical footprint；Windows working set |
| 快捷键到浮层可交互 | nearest-rank P95 ≤ 150 ms | 至少 30 个真实全局快捷键样本；从系统回调到平台配置完成且浮层可交互 |
| 4K 交互 | P95 ≤ 16.667 ms 且真机稳定 60 FPS | 3840×2160；对象拖动、持续马赛克、移动、撤销/重做；CPU 代理不替代真机合成 |
| 4K PNG | P95 ≤ 1,000 ms | 最终合成并实际写入临时文件 |
| 资源稳定性 | 尾段增长 ≤ 1% | 至少 20 次成功复制/保存；比较最后两个连续 5 次窗口中位数；Windows 循环用 private memory，macOS 用 physical footprint |

基准必须独占运行，不能与构建/测试并行争抢 CPU。取消或直接关闭不计成功循环；每次采样前应等待 Capture Session 资源释放，并按实现语言使用可解释的回收/稳定化流程。

证据：`docs/testing/performance-compatibility-report.md`、Issue #18、`dotnet-final:tools/Snaploom.PerformanceHarness/`、性能测试。

### PERF-02 参考基线（SHOULD，不是逐数等价要求）

2026-07-16 Mac mini M4 / macOS 26.5 / 单屏 1080p 的旧实现参考：空闲 67.99 MB；快捷键 P95 140.1554 ms；4K 对象拖动 14.5605 ms；马赛克绘制/移动/撤销重做分别 0.8932/13.5522/10.2312 ms；PNG 202.8086 ms；30 次 native memory 尾段增长 0.09%。

这些数值用于发现迁移回退，硬验收仍以 PERF-01 为准。旧报告的“149 项测试”不是功能合同；本合同提交时现有全量测试为 185 项，也不要求新技术栈保持测试数量相等。

证据：`docs/testing/evidence/2026-07-16-macos-m4/*.json`。

## 12. 构建、安装与发布验收

### DIST-01 CI（MUST）

- 每次主分支 push 和 PR 在 Windows x64 与 macOS Apple Silicon 上恢复依赖、Release 构建、运行所有无头测试；macOS 额外运行原生帧方向测试。
- 两个平台必须构建并验证安装包；任一平台失败不能发布部分 Release。
- Tauri 迁移后的具体命令可以变化，但覆盖平台、测试层级、架构和失败原子性不变。
- 证据：`dotnet-final:.github/workflows/ci.yml`、`release.yml`。

### DIST-02 Windows 用户级安装包（MUST）

- 资产名 `snaploom-<version>-windows-x64-setup.exe`，同名 `.sha256`；x64、self-contained、≤ 50,000,000 bytes。
- Windows 10 最低构建 19045；普通用户安装，不请求管理员权限；默认 `%LocalAppData%\Programs\Snaploom`，注册 `Snaploom.Desktop` 当前用户卸载项。
- 安装后可启动常驻；新版本覆盖升级；卸载移除应用与卸载项，但保留用户设置和日志。
- 首批测试版未签名，发布页必须说明 SmartScreen 风险和 SHA256 验证。
- 验证：自动静默安装/启动/覆盖/卸载 + Windows 10/11 普通用户真机。
- 证据：`docs/testing/windows-installer-validation.md`、`docs/distribution/windows-installation.md`、`dotnet-final:tests/packaging/verify-windows-installer.ps1`。

### DIST-03 macOS DMG（MUST）

- 资产名 `snaploom-<version>-macos-arm64.dmg`，同名 `.sha256`；所有 Mach-O 仅 arm64、≤ 50,000,000 bytes，Bundle ID `com.snaploom.app`，最低 macOS 14。
- DMG 包含 `Snaploom.app` 与 `/Applications` 快捷方式；自动验证安装、启动、卸载、本地化权限说明和图标。
- 当前测试版必须显式 ad hoc 签名、未公证，并在发布页说明 Gatekeeper 风险与 SHA256。取得 Developer ID 后，正式模式必须同时完成 Developer ID 签名、公证、stapling 与 Gatekeeper 验证，失败不得降级为 ad hoc。
- 验证：自动 DMG 脚本 + 全新 macOS 14+ Apple Silicon 真机。
- 证据：`docs/testing/macos-dmg-validation.md`、`docs/distribution/macos-installation.md`、`dotnet-final:tests/packaging/verify-macos-dmg.sh`。

### DIST-04 Release 原子性与门禁（MUST）

- 版本 tag 严格为 `vX.Y.Z` 且提交可从默认分支 `main` 到达；同 tag 不覆盖、不移动、不复用已有草稿或公开 Release。
- Issue #18 必须以 `closed/completed` 通过；`not planned` 不放行。
- Windows EXE、macOS DMG 及各自 SHA256 四个资产全部存在、非空、校验和/元数据一致后，才把草稿公开为 prerelease；失败最多留下不可见草稿，不能留下看似完整的公开 Release。
- Tauri 迁移完成后的同一版本 Release 必须同时包含彼此独立的应用分发资产与 SDK 分发资产；#31 确定的完整资产集合必须全部存在、版本一致、非空且各自通过校验后才能公开。不得先发布应用、后补 SDK，也不得用新增 SDK 资产削弱现有应用门禁。
- 验证：工作流级测试或受控 dry-run，并审查 Release API 结果。
- 证据：`docs/distribution/test-release-process.md`、`dotnet-final:.github/workflows/release.yml`、Issue #21/#31。

## 13. 验收矩阵与迁移完成定义

### QA-01 自动测试接缝（MUST）

新实现至少保留三类对外接缝，名称可变化但观察面不能降低：

1. 核心 Capture Session：确定性输入事件 → 状态快照，覆盖门控、选区、窗口候选、坐标、标注编辑和历史。
2. 最终渲染输出：对象列表 + 捕获帧 → 最终位图/PNG，覆盖工具金图、层级、裁切、DPI、颜色空间和元数据。
3. 平台适配：统一接口 → Windows/macOS 系统能力，覆盖快捷键、捕获、窗口枚举、权限、剪贴板、保存、单实例、开机启动和唤醒。

测试只断言公共行为，不锁定 Rust 模块、Tauri command、TypeScript 类名、Canvas 调用次数或 WebView 内部结构。

证据：`docs/snaploom-v1-spec.md` 的 Testing Decisions、`docs/adr/0002-adopt-tauri-2.md`、#30。

### QA-02 必须执行的自动矩阵（MUST）

- 选区/DPI：100%、125%、150%、175%、200%，任意方向框选、8 物理像素下限、8 控制柄、边界夹紧。
- 标注：矩形、箭头、文字、马赛克各自金图；重叠层级、选区裁切、移动/缩放/样式/删除/撤销/重做。
- 文字：简中、英文、数字、符号、多行、自动换行、IME 预编辑；emoji 只要求不崩溃。
- 输出：复制与保存同图、物理像素尺寸、8 位 sRGB、无私有元数据、取消/失败完整恢复。
- 生命周期：单实例、重复触发、快捷键冲突与唤醒、设置损坏、隐私日志、手动更新错误分类。
- 性能：PERF-01 的自动部分，保存原始样本与可复算聚合结果。

### QA-03 真机矩阵（MUST）

- Windows 10 22H2 x64、Windows 11 x64；macOS 14+ Apple Silicon。
- 单屏、双屏、1080p、4K、5K Retina、混合 DPI、显示器热插拔、睡眠唤醒。
- 权限缺失、快捷键冲突、重复启动、取消保存、保存失败、剪贴板失败、受保护桌面。
- Windows 用户级未签名安装/覆盖/卸载；macOS ad hoc DMG/Gatekeeper/权限/卸载；未来正式签名时补公证链。

历史 Issue #18 的人工完成状态不免除 Tauri 新实现重新执行与风险相匹配的迁移真机验收。

### QA-04 完成判定（MUST）

一个 Tauri 迁移 ticket 只有在以下条件同时满足时才可声明功能等价：

- 本文件涉及的每个合同 ID 都映射到新实现的自动测试、视觉证据、性能报告或明确真机记录；不存在“仅凭旧实现看起来如此”的条目。
- 所有 `MUST` 通过，`SHOULD` 的偏离有后续 ADR/Issue 明确批准。
- 自动代理项没有被当作真实 4K 帧率、系统权限、原生对话框或安装体验的替代品。
- 新 Capture Host 在应用入口与 SDK 入口产生相同交互和 Capture Result；SDK 专属 ABI、IPC、Busy/取消/崩溃语义按 #28 另行验收。
- `dotnet-final` 标签已创建并推送；主分支不恢复双实现，回滚只回到标签。

## 14. 不属于等价要求的实现细节

以下内容不得因“与旧代码不同”被判失败：

- .NET 10、Avalonia、SkiaSharp、C# 类型名、项目布局、Win32/Swift bridge 的具体封装方式。
- 单次内部方法调用数、控件树私有节点、异常英文原文、未被使用的资源字符串。
- 旧实现测试数量、某台机器的逐项参考耗时、托管堆指标本身。
- Tauri WebView 与 Rust 的职责切分、Capture SDK ABI/IPC、许可证材料与供应链的具体实现；这些分别由 #22、#23、#25、#28、#29、#30、#31 决定，但不得削弱 ARCH-01 或本文其他外部行为。

## 15. 产品范围外

本合同不把以下能力带入首版：OCR/翻译、滚动长截图、贴屏、录屏/GIF/视频、自由画笔、编号、形状填充、虚线/双头箭头、高斯模糊、图层面板、右键菜单、循环选择重叠对象、截图历史、云同步、遥测、自动上传/分享链接、JPG/WebP、跨显示器选区、绕过 UAC/锁屏/DRM、Linux、Windows ARM64、Intel Mac、便携版、应用商店、后台自动更新。

## 16. 一手证据索引

- 领域与路线：`CONTEXT.md`、Issue #21/#24、`docs/adr/0002-adopt-tauri-2.md`、`0003-isolate-capture-host.md`、`0004-replace-dotnet-from-main.md`。
- 产品与 UI：`docs/snaploom-v1-spec.md`、`docs/ui/screenshot-ui.md`。
- 当前实现：`dotnet-final:src/Snaploom.Core/`、`Snaploom.App/`、`Snaploom.Rendering/`、`Snaploom.Platform.*`。
- 行为测试：`dotnet-final:tests/Snaploom.Core.Tests/`、`Snaploom.App.HeadlessTests/`、`Snaploom.Rendering.Tests/`、`Snaploom.IntegrationTests/`。
- 性能：`docs/testing/performance-compatibility-report.md`、`docs/testing/evidence/2026-07-16-macos-m4/`、Issue #18。
- 平台真机：`docs/testing/macos-t02-validation.md`、`windows-t03-validation.md`。
- 发布：`docs/testing/macos-dmg-validation.md`、`windows-installer-validation.md`、`docs/distribution/`、`dotnet-final:.github/workflows/`、`dotnet-final:tests/packaging/`。
