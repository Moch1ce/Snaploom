# Snaploom 一次性 Tauri 迁移与切换路线

状态：Issue [#32](https://github.com/moch1ce/Snaploom/issues/32) 的实施输入

适用目标：在主线以 Tauri 2、Rust 与 TypeScript/单 Canvas 一次性替换 .NET/Avalonia Snaploom，交付 Windows x64、macOS arm64、独立 GPL Capture Host 与 Apache Capture SDK。

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、[Tauri/Canvas 决策](./tauri-overlay-canvas-feasibility.md)、[ABI/IPC 决策](./capture-sdk-c-abi-ipc.md)、[仓库与测试体系](./rust-workspace-module-testing-design.md)、[发布供应链](./release-supply-chain-design.md)、ADR 0001～0004。

## 1. 最终结论

迁移不保留双实现主线，不做运行时 feature flag，不用新前端包装旧 .NET 后端。执行分为三个不可逆的阶段：

1. **冻结旧实现**：先为现有源码建立明确 GPL/REUSE 身份，复跑基线，创建并推送 annotated `dotnet-final` tag。
2. **单向切换**：立即删除 .NET/Avalonia、Swift bridge 和旧 packaging/workflow，一次建立 `product/`、`sdk/`、`web/` 三 workspace 外壳。从此主线只修新实现。
3. **纵向恢复能力**：按可运行 tracer slices 恢复协议、Host gate、单 Canvas、标注/输出、双平台、Desktop lifecycle、SDK wrappers 和供应链；每个 slice 都有入口、可观察结果、合同映射、测试、独立 commit 与立即 push。

所谓 rollback 只是从 `dotnet-final` 另开恢复分支或放弃整个迁移分支；不允许在新主线重新加回旧 `.csproj` 作为 fallback。

## 2. 执行 DAG

```text
M00 old-license/baseline
  -> M01 dotnet-final tag pushed
     -> M02 remove legacy + workspace shell
        -> M03 protocol/C ABI tracer
           -> M04 Host gate + fake session
              -> M05 frame -> Canvas -> cancel
                 -> M06 selection/window -> PNG
                    -> M07 annotations/history
                       -> M08 output/recovery

M02 -> M09 Windows real adapter ----\
M02 -> M10 macOS real adapter -------+-> M12 desktop lifecycle
M03 -> M11 SDK wrappers -------------/       |
M08 + M09 + M10 + M11 + M12 -> M13 compliance/package/performance
                                 -> M14 signed legal RC draft
                                    -> #33 owner risk acceptance
                                       -> M15 stable publish
```

`M09/M10/M11` 可在共享合同固定后并行；同一工作树内仍必须保持一个任务一个 commit/push。`M15` 是唯一被 #33 阻塞的阶段；没有所有者风险接受记录不阻止完成 M00～M14。

## 3. M00：旧实现授权与基线

在创建 tag 之前必须先提交并推送：

- `LICENSES/GPL-3.0-or-later.txt`、`LICENSES/Apache-2.0.txt`、`REUSE.toml` 与贡献政策；
- 现有 .NET/Avalonia/Swift/product 文件映射为 `GPL-3.0-or-later`，未来 `sdk/**` 映射为 `Apache-2.0`；
- README 明确分层授权和 `dotnet-final` 的只读历史用途；
- 旧依赖许可证、用户资产、secret、签名材料和大文件审计。

基线验证：

```sh
dotnet test Snaploom.sln -c Release --no-restore
CLANG_MODULE_CACHE_PATH=/private/tmp/snaploom-swift-module-cache \
  bash tests/native/macos/run-frame-orientation-test.sh
git status --short
```

保存精确测试计数、环境、提交 SHA 和证据文档。当前已知基线为 185/185 .NET tests 与 macOS frame-orientation test 通过；打 tag 前仍从最终 commit 再跑一次。

## 4. M01：创建并推送 `dotnet-final`

前置：工作树干净，M00 commit 已在 `origin`，基线全绿，远端不存在同名 tag。

```sh
git tag -a dotnet-final -m "Final GPL-licensed .NET/Avalonia implementation before Tauri 2 migration"
git push origin dotnet-final
git ls-remote --tags origin refs/tags/dotnet-final refs/tags/dotnet-final^{}
```

tag 对象的 commit 必须记入迁移证据与 Issue #32。推送 tag 失败时立即停止，不允许先删旧代码再补标签。

## 5. M02：删除旧实现并建立外壳

tag 推送后使用显式 `git rm` 目标，不用宽泛递归命令。

删除：

```text
Snaploom.sln
Directory.Build.props
Directory.Packages.props
global.json
src/Snaploom.*
tests/Snaploom.*
tools/Snaploom.*
tools/Snaploom.PerformanceHarness
tests/native/macos/FrameOrientationTest.swift
tests/native/macos/run-frame-orientation-test.sh
packaging/windows/Snaploom.iss
packaging/windows/Languages/**
packaging/macos/Snaploom*.entitlements
scripts/build-windows-installer.ps1
scripts/build-macos-dmg.sh
tests/packaging/verify-*.{ps1,sh}
.github/workflows/ci.yml
.github/workflows/release.yml
docs/testing/*-t02-validation.md
docs/testing/*-t03-validation.md
docs/testing/*installer-validation.md
docs/testing/macos-dmg-validation.md
docs/testing/performance-compatibility-report.md
docs/distribution/*installation.md
docs/distribution/test-release-*.md
docs/licenses/dependency-licenses.md
```

保留但标为历史的 PRD/验收文档，只要新合同明确覆盖它们；不要为了“干净”删除 ADR、#21～#33 决策、功能等价合同或 Screenshot UI 规范。

同一 commit 建立最小可构建外壳：

```text
product/Cargo.toml + Cargo.lock + deny.toml
product/apps/desktop
product/apps/capture-host
product/capture-session
product/platform/{contract,fake,windows,macos}
sdk/Cargo.toml + Cargo.lock + deny.toml
sdk/{protocol,client,c-abi,include,cpp,dotnet,swift,examples,testkit}
web/package.json + pnpm-lock.yaml
web/{overlay-editor,screenshot-ui,desktop-settings,testkit}
testing/{contract-map,scenarios,goldens,desktop-e2e,performance,packaging}
tools/xtask
.github/workflows/ci.yml
```

基线验证必须包含两个 Rust workspace `fmt/clippy/test --locked`、pnpm frozen install/typecheck/test/build、Tauri Desktop/Host 双平台 build 以及依赖方向检查。该 commit 可以暂时只显示“尚未连接 Capture Host”的开发状态，但必须是无 .NET 依赖的真正可启动产物。

## 6. M03：公开协议与 C ABI tracer

实现 Apache workspace 的：

- proto3 schema、12-byte framing、稳定 error/capability registry、golden fixtures 与 schema lint；
- bounded control frame/PNG chunks、版本协商、partial read/write 与 allocation limits；
- 7-symbol C ABI、`#[repr(C)]` mirror、`struct_size`、completion allocator/free、symbol allowlist；
- client request table、callback executor、exactly-once terminal slot、cancel/destroy 竞态；
- fake Host/in-memory transport。

Tracer：`C start -> client -> in-memory wire -> fake Host -> Completed/Busy/Canceled/Failed -> callback/free`。

合同：`SDK-01～04`、`APP-03`、`OUT-01`、`IMG-01`。

验证：Rust tests、proto golden/fuzz smoke、C/C++ header/layout/export、old/new minor matrix、ASan/内存清零 instrumentation。

## 7. M04：真 IPC、Host 单例与全局 gate

实现 Windows named pipe 与 macOS UDS 的 endpoint、peer identity、secure spawn/bootstrap、leader election、idle grace 与 Host locator。Host ingress 接 `CaptureSession + ScriptedPlatform + OverlayHarness`。

Tracer：App 与多个 SDK client 并发，恰好一个 Accepted，其他 SDK 一次 `Busy`，App 静默忽略；disconnect/crash/cancel 后 gate 只在资源清理完成时释放。

合同：`APP-01～03`、`ERR-01`、`SDK-01～04`。

验证：20 进程并发 launch、peer auth negative cases、每个 frame/chunk 断连、Host/client kill、无重放。

## 8. M05～M08：统一截图编辑链路

### M05 Frame-to-overlay

`ScriptedPlatform snapshot -> Session-scoped bounded binary -> one visible Canvas -> ready -> overlay show -> Esc/cancel -> dispose`。禁止 Base64/data URL，WebView 无 clipboard/dialog/filesystem/global-shortcut 权限。

合同：`CAP-01～03`、`ERR-01`、`UI-01～04`、`OUT-04`。

### M06 Selection/window-to-PNG

实现首移吸附抑制、窗口 Z 序候选、自由框选、8 物理像素下限、8 控制柄、整体移动锁定、混合 DPI/负原点、浮动 UI 布局与首个 decoded-pixel golden。

合同：`CAP-04～05`、`SEL-01～04`、`UI-01～04`、`IMG-01`。

### M07 Annotation/history

以纵向子提交依次实现矩形、箭头、文字/IME、马赛克，每个都贯穿对象模型、命中/缩放/移动、样式、history、统一 render plan 与最终 PNG。不提交只有 toolbar button 的“假完成”。

合同：`ANN-01～08`、`QA-01～04`。

### M08 Output/recovery

实现绿色完成/双击/Enter、copy-and-continue、PNG-only save、overlay hide/restore、clipboard/save 错误重试。App clipboard、save 与 SDK result 使用同一最终 PNG bytes。

合同：`OUT-01～04`、`ERR-01`、`IMG-01`。

验证：状态 snapshot、Canvas RGBA golden、工具栏 pointer 冒泡回归、简中/英文 IME 真机、clipboard/save/SDK 原字节一致、保存取消完整恢复。

## 9. M09：Windows 真实 Adapter

替换 fake 的顺序：

1. PerMonitorV2 + 指针显示器 + WGC/D3D11 单帧 + HDR/stride/orientation；
2. UWP/CoreWindow 与 desktop Z 序窗口目录、产品窗口过滤与 click-through；
3. 显式显示器 overlay、topmost、任务栏/DPI/topology；
4. Win32 注册 `PNG` clipboard、native save dialog、global shortcut、wake、autostart、notification。

捕获失败不降级 GDI/DXGI/社区 capture crate。每替换一个领域操作，重跑同一 scenario 与平台 contract suite。

合同：`PLAT-01～04`、`CAP-01～05`、`OUT-01～03`、`PERF-01`。

## 10. M10：macOS 真实 Adapter

替换顺序：

1. TCC 预检/请求 + `SCScreenshotManager` + CVPixelBuffer 方向/步幅/sRGB；
2. SCK + CG 候选合并、产品窗口过滤、公共 API click-through 保守降级；
3. `NSScreen.frame` 全显示器 overlay、screenSaver level、Spaces/菜单栏/Dock、Retina/topology；
4. NSPasteboard、app-modal save、global shortcut、SMAppService、workspace wake、notification。

禁止 Swift dylib bridge、私有 API、Accessibility 权限、透明 WebView 和旧 CG capture fallback。

合同：`PLAT-05～08`、`CAP-01～05`、`OUT-01～03`、`PERF-01`。

## 11. M11：官方 SDK wrappers

依次交付：

- C archive/header/CMake 与 C++17 header-only RAII；
- `Snaploom.Capture` net8.0，`win-x64` + `osx-arm64`，LibraryImport/SafeHandle/Task/CancellationToken；
- Swift source wrapper + `CSnaploomCapture.xcframework` + `async throws`/checked continuation；
- 每种语言的最小闭源形状 consumer 示例。

只绑定 7-symbol ABI；callback 内复制 PNG 并且恰好一次 free；取消只发 intent 并等待 terminal callback。SDK 包不含 Host、Tauri、GPL object/symbol 或 Host downloader。

合同：`SDK-01～04`、`IMG-01`、`OUT-01`。

## 12. M12：Desktop lifecycle

在 Desktop composition root 接入：

- tray-only 启动、单实例、默认/自定义全局快捷键事务；
- 设置持久化、简中/英文、固定浅色、开机启动；
- 睡眠唤醒、稳定隐私日志、类型化失败、手动 GitHub Release 更新检查；
- App 只作 Host client，不保留 CaptureSession 旁路。

合同：`APP-01～04`、`SET-01～04`、`LOG-01`、`UPD-01`。

## 13. M13：合规、包、性能与完整验收

必须同时完成：

- `reuse lint`、product/sdk 分开 `cargo deny`、pnpm production license、NOTICE 与 CycloneDX；
- Windows x64 user installer 和 standalone Host，macOS arm64 DMG 和 standalone Host；
- C SDK archives、NuGet、Swift/XCFramework、corresponding source、manifest/checksums；
- 混合 DPI/Retina、IME、置顶、权限、clipboard/save、安装/升级/卸载真机矩阵；
- 快捷键→overlay 30+ 样本 P95 ≤ 150 ms，4K 编辑 P95 ≤ 16.667 ms，4K PNG P95 ≤ 1,000 ms，空闲内存 ≤ 100 MB，20+ 循环尾段增长 ≤ 1%，安装包 ≤ 50 MB；
- `testing/contract-map/contracts.yml` 中所有 MUST 都指向当前 Rust/TS/平台证据，不再引用 .NET 通过记录。

## 14. M14～M15：Release candidate 与 stable

M14 创建签名的不可见 draft：

1. 同一 tag/commit 生成 exact asset set；
2. Windows Authenticode/RFC3161，macOS Developer ID/hardened runtime/notary/staple；
3. 上传后重新下载，复验 manifest、checksum、架构、包边界、SBOM/NOTICE/source 和 consumer tests；
4. 生成 #33 review bundle：draft URL、tag/commit、manifest digest、payload checksums、签名/公证记录。

M15 只在 #33 所有者批准记录精确覆盖该 RC 且明确确认未经过外部法律复核后执行：publish job 不重建、不重签、不替换资产，只复验后一次把 draft 改为 stable，再 promotion 同一 `.nupkg` 到 NuGet.org；SwiftPM 直接消费同 tag/Release。

## 15. 每个 slice 的提交合同

每个实施 Issue 必须按同一模板完成：

```text
Scope: 一条可运行入口 -> 可观察结果
Contracts: 本次覆盖的稳定合同 ID
Seams: 只列真实平台/进程/许可证边界
Tests: red -> green 的公共 Interface 行为
Evidence: 自动/golden/真机/性能/包中适用类型
Forbidden: 本次不允许的 fallback/旁路/双实现
Git: 独立 commit，立即 push 当前分支
Tracker: 链接 commit/证据，关闭 Issue，更新 Spec 映射
```

一个 slice 只有在用户可观察行为通过时才算完成；“crate 存在”“API 能编译”或“按钮显示”不是单独完成条件。

## 16. 失败、中断与回滚

| 情形 | 唯一允许处理 |
| --- | --- |
| `dotnet-final` push 失败 | 停在 M01，不删旧实现 |
| M02 外壳不能构建 | 修正新外壳，不在主线恢复 `.csproj` |
| 某平台 Adapter 暂时不通 | 分支保持不可发布，修正 Adapter；不保留旧 backend |
| Canvas/IME/性能不达标 | 优化单路径或先更新 ADR；不在运行时 fallback B/C variant |
| SDK/Host 协议缺陷 | 升级未发布 protocol；不加未鉴权旧协议降级 |
| 完整迁移决定被撤销 | 从 `dotnet-final` 另开恢复分支；不会在新实现分支反向 merge 旧树 |
| #33 未完成 | 完成 M00～M14，保留 draft；stable publish 禁止 |
| 已发布 stable 有问题 | 停止推广/unlist（适用时）并发新 patch；不改 tag/资产 |

## 17. 路线完成判定

Issue #32 可以关闭的条件是路线已经足以直接生成 Spec 和 tracer tickets，不是整个代码迁移已经完成。转换后的 Spec/tickets 必须：

- 保持 M00～M15 阻塞边和每个合同 ID；
- 把 M07、M09、M10、M11、M13 拆为小型纵向 tickets，而不把一整个平台/语言放入一张大票；
- 每张票声明公共可观察验收、测试层级、真机/签名前置与禁止 fallback；
- 把 #33 作为 stable publish 一条终态阻塞边，不再阻塞 RC 产物的实施。

新 Spec 发布后，旧 Issue #1 和 #19 应以“已被 Tauri 迁移 Spec/发布 tickets 取代”关闭，不再继续接收 .NET 实施。
