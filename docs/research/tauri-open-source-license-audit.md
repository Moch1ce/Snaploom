# Tauri 2 开源许可证与依赖边界审计

- 研究日期：2026-07-20
- 对应 Issue：[#29](https://github.com/liuchuana/Snaploom/issues/29)
- 适用目标：Windows x64、macOS arm64、Tauri 2 桌面应用、独立 Capture Host 与官方 Capture SDK

> 本文是面向工程设计与开源合规流程的研究结论，不是针对任何主体、司法辖区或具体分发行为的法律意见。GPL 中“一个作品”与“独立作品”的判断取决于事实；研究仍建议咨询熟悉开源软件的律师，但当前发布流程由仓库所有者明确接受未经过外部法律复核的风险。

## 结论

Snaploom 可以采用已经确定的分层授权，但必须把它实现成真实的代码、进程与发布边界：

- Snaploom 桌面应用、Capture Host、截图与标注实现、平台 Adapter 使用 `GPL-3.0-or-later`。
- 稳定 C ABI、公开 IPC schema、C/C++ 头文件与封装、C# NuGet、Swift Package/XCFramework、SDK 示例使用 `Apache-2.0`。
- Apache SDK 只能依赖 Apache 或更宽松、且经过准入的代码；不得链接、复制或生成自 GPL 实现代码。
- Apache 代码可以进入 GPLv3 作品，反向不成立。应用/Host 可以依赖 Apache SDK 或协议模块，SDK 不得依赖 GPL 应用/Host。[Apache Software Foundation 对 GPLv3 兼容性的说明](https://www.apache.org/licenses/GPL-compatibility)
- SDK 与 Host 以不同二进制、不同包和版本化本地 IPC 通信；同一 GitHub Release 中也必须是独立资产。官方 SDK 包不得静态/动态链接 Host，不得把 Host 可执行文件塞入 NuGet、XCFramework 或 C/C++ SDK 压缩包。
- 这种独立进程设计显著降低闭源宿主被认定为 GPL 衍生作品的风险，但“进程 + IPC”不是自动免责规则。GNU FAQ 也明确把通信机制、通信语义和耦合程度作为判断因素，因此文档和产品不能承诺“任何闭源集成都绝对不受 GPL 影响”。[GNU GPL FAQ：GPL 软件与专有系统](https://www.gnu.org/licenses/gpl-faq.html#GPLInProprietarySystem)
- 闭源开发者可以按 Apache-2.0 使用官方 SDK；如果其同时再分发 GPL Capture Host，必须履行 Host 的 GPL 分发义务。如果对 Host 进行嵌入、链接、插件化、共享内部数据结构或形成紧密组合，则必须另行评估。
- Tauri 核心代码采用 MIT 或 Apache-2.0，使用 Tauri 本身没有 Qt 式商业授权费。[Tauri 官方仓库许可证](https://github.com/tauri-apps/tauri)
- 纯开源不妨碍收费销售应用、支持、定制、托管或品牌合作，但 GPL 不允许限制接收者再分发 GPL 代码，也不能再向 SDK 使用者收取“闭源授权费”。

## 代码与发布边界

建议迁移后的目录采用以下许可归属；最终由 `REUSE.toml` 和每个包的 SPDX 元数据落地，而不是只依赖一段 README 声明。

| 范围 | 许可证 | 边界要求 |
| --- | --- | --- |
| `apps/snaploom-desktop/**` | GPL-3.0-or-later | 桌面应用、托盘、设置、更新入口 |
| `apps/capture-host/**` | GPL-3.0-or-later | 独立 Tauri Host 与完整交互界面 |
| `crates/capture-core/**` | GPL-3.0-or-later | 截图、选区、标注、合成与导出实现 |
| `crates/platform-windows/**`、`crates/platform-macos/**` | GPL-3.0-or-later | 平台截图、窗口、剪贴板、快捷键和权限 Adapter |
| `sdk/c/**`、`sdk/cpp/**` | Apache-2.0 | 稳定 C ABI、头文件、导入库与 C++ RAII 封装 |
| `sdk/dotnet/**` | Apache-2.0 | 跨平台 C# NuGet，只绑定 C ABI |
| `sdk/swift/**` | Apache-2.0 | Swift Package 与只含 SDK ABI 的 XCFramework |
| `sdk/protocol/**` | Apache-2.0 | 版本化公开 IPC schema、错误码和生成器输入 |
| `examples/sdk/**` | Apache-2.0 | 允许复制进闭源宿主的最小示例 |
| 仅供 Host 与 SDK 共用的基础类型 | Apache-2.0 或拆分 | 只有宽松许可代码才能被两侧共同编译 |
| 仓库其他源码、测试和迁移文档 | GPL-3.0-or-later | SDK 目录除外；第三方素材按原许可单独标注 |

必须保持单向依赖：

```text
GPL App / Capture Host -> Apache SDK protocol/types
Closed-source host -> Apache SDK -> versioned local IPC -> GPL Capture Host
```

禁止出现以下反向路径：

```text
Apache SDK -> GPL capture-core/platform/Host code
Closed-source host -> in-process GPL plugin/library
Generated Apache binding -> copied GPL structs/algorithms
```

为了让“独立作品”的事实基础更清楚，Capture Host 应能作为独立应用运行；IPC 只暴露稳定的服务级命令、请求 ID、结果 PNG、尺寸和错误码，不暴露内部对象图、函数回调、共享内存中的可变领域对象或插件 ABI。SDK 负责进程发现、版本协商、请求/取消和结果传输，不承载截图算法或 UI。

## 依赖许可策略

目前仓库还没有 Tauri `Cargo.lock`、`pnpm-lock.yaml` 和最终平台 crate 选型，所以现在只能确定准入策略与候选主干，不能声称已经完成最终传递依赖清单。Cargo 明确把 manifest 的 `license` 解释为 SPDX 表达式；真正的发布审计必须以锁文件、包内许可证文本和实际构建特性为准。[Cargo manifest 许可证字段](https://doc.rust-lang.org/cargo/reference/manifest.html#the-license-and-license-file-fields)

### 默认允许

以下许可证可默认进入 Apache SDK，也可进入 GPL App/Host，但仍需保留上游版权、许可证文本和 NOTICE（如有）：

- `Apache-2.0`
- `MIT`
- `BSD-2-Clause`
- `BSD-3-Clause`
- `ISC`
- `0BSD`
- `Zlib`
- `BSL-1.0`
- `Unicode-3.0`

双许可证表达式必须按 `OR` 与 `AND` 的真实含义处理。例如 `MIT OR Apache-2.0` 可以选择其中之一；`MIT AND Apache-2.0` 必须同时履行。不得把历史上常见的 `/` 未经核验地当作 `OR`。

### 仅经逐项复核后允许

以下依赖不能自动合入：

- `MPL-2.0`、`EPL-2.0`、`CDDL-*` 等文件级或弱 Copyleft：App/Host 可能可用，但 SDK 默认拒绝；必须确认二进制组合、修改文件和源码提供义务。
- `LGPL-*`：App/Host 只有在动态链接、替换/重新链接能力、通知和源码义务都能满足时才可例外；SDK 默认拒绝。
- `GPL-3.0-only`、`GPL-3.0-or-later`：只可能进入 GPL App/Host，且不能进入 Apache SDK。为了降低源码供应链复杂度，首版原则上仍不引入第三方 GPL 运行时依赖。
- `OpenSSL`、带 exception 的许可证、公共领域声明、`LicenseRef-*`、自定义许可证、字体/图标/媒体许可证：逐个核对原文和分发方式。
- Git 依赖、本地 fork、预编译二进制、系统 runtime bootstrapper：除许可证，还必须固定提交、记录来源、检查 NOTICE 和可再分发条款。

### 禁止

- `AGPL-*`：首版所有发布物都拒绝，避免网络交互触发和边界争议。
- `GPL-2.0-only`：与 Apache-2.0 组合存在兼容问题，不进入任何 Snaploom 发布物。
- SSPL、BUSL、Elastic License、Commons Clause、PolyForm 及其他 source-available 许可证。
- 含“非商业”“禁止衍生”“禁止特定领域”“不可再分发”限制的代码、模型、字体、图标或媒体。
- 无许可证、许可证未知、只在网页口头宣称“免费”、或者包元数据与实际 LICENSE 不一致的依赖。
- 在 Apache SDK 内的任何 GPL/AGPL/LGPL 代码，以及来源于 GPL 实现的生成文件。
- Tauri Logo 等带 `CC-BY-NC-ND` 限制的品牌资产；Snaploom 不需要也不应把 Tauri 商标素材打进产品。

## Tauri、Rust、前端与系统组件

候选主干本身没有发现强 Copyleft，但版本锁定后仍要审计完整依赖图：

| 组件 | 一手资料中的许可/角色 | 决策 |
| --- | --- | --- |
| Tauri 2 | 代码为 MIT 或 Apache-2.0；使用系统 WebView | 可用，按最终锁定版本保留许可文本；官方仓库也提醒下游自行核验依赖与 SBOM。[Tauri 架构说明](https://github.com/tauri-apps/tauri/blob/dev/ARCHITECTURE.md) |
| Wry | 官方仓库包含 MIT 与 Apache 许可证；承接 WebView | 可用，按锁定 tag/包内容复核。[Wry 官方仓库](https://github.com/tauri-apps/wry) |
| Tao | 官方仓库提供 Apache-2.0；承接窗口事件循环 | 可用，按锁定 tag/包内容复核。[Tao 官方仓库](https://github.com/tauri-apps/tao) |
| TypeScript | Apache-2.0 | 可作为前端编译工具。[TypeScript 官方仓库](https://github.com/microsoft/TypeScript) |
| Vite | MIT；支持生成构建所含依赖的 license 文件 | 可用，开启 `build.license` 并纳入发布声明。[Vite 许可证产物](https://github.com/vitejs/vite/blob/main/docs/guide/features.md#license) |
| pnpm | MIT；提供确定性 lockfile | 可用，提交 lockfile 并对生产依赖生成清单。[pnpm 官方仓库](https://github.com/pnpm/pnpm) |
| Windows WebView2 | Tauri/Wry 使用系统 WebView2 | 首版优先使用系统 Evergreen Runtime；如改为捆绑 Fixed Version 或 bootstrapper，必须单独复核微软再分发条款。 |
| macOS WKWebView | 系统框架 | 作为系统库调用，不复制 Apple SDK；构建和签名仍遵守 Xcode/Apple 条款。 |
| Windows/macOS SDK 与签名工具 | 构建环境 | 不作为 Snaploom 开源依赖再分发；CI runner、证书和 notarization 条款由发布供应链票复核。 |

构建工具通常不会因为参与编译就把其许可证施加到输出，但它们可能携带可再分发 runtime、模板、生成代码或 NOTICE，因此不能从审计中删除。SBOM 至少要区分 runtime、build 和 development scope。

## 发布时必须交付的合规材料

### GPL App 与 Capture Host

每个二进制发布必须同时满足 [GNU GPLv3 正文](https://www.gnu.org/licenses/gpl-3.0.html) 第 4、5、6 节：

- 保留适当版权与无担保声明，随包提供 `GPL-3.0-or-later` 完整文本。
- 通过同一下载位置免费提供与二进制**精确对应**的机器可读 Corresponding Source，或在二进制旁给出清楚、等价且长期有效的获取指引。
- Corresponding Source 包含构建、安装、运行和修改所需源码、接口定义、生成器输入、构建/打包脚本与配置；不能只链接到不断变化的 `main`。
- Release 使用不可变 tag，并生成带 submodule/vendor 信息的确定性源码归档；GitHub 自动源码包不能未经验证就视为充分。
- 如果发布物包含第三方 GPL/LGPL 对象代码，也要覆盖其相应源码与通知义务。
- 代码签名私钥不应进入源码，但构建说明必须允许开发者生成和运行自己的未签名/自签名构建；产品机制不得仅因二进制被修改就阻止其正常本地运行。
- 首版只通过 GitHub Release 直接分发，不进入带 DRM 或额外使用限制的应用商店；商店条款需另开评估后才能改变这一决策。

GPL 允许把独立作品放在同一分发介质中而不自动改变其他作品的许可证，但是否“独立”取决于作品之间的关系，而不只取决于文件名或进程数。[GNU GPLv3 第 5 节的 aggregate 定义](https://www.gnu.org/licenses/gpl-3.0.html#section5)

### Apache SDK

依据 [Apache License 2.0 正文](https://www.apache.org/licenses/LICENSE-2.0)，SDK 的源码与二进制包必须：

- 附带 Apache-2.0 完整文本。
- 保留源码中的版权、专利、商标和 attribution notices；修改文件应有显著修改说明。
- 如果 Snaploom 或任一被再分发依赖提供 `NOTICE`，衍生发布物必须在许可证允许的位置保留其相关内容。没有上游 NOTICE 时不应伪造一份，但仍需第三方许可证清单。
- `Cargo.toml`、NuGet `.nuspec`、Swift `Package.swift`、CMake/pkg-config 元数据和发布页面均声明 `Apache-2.0`。
- SDK 文档明确：Apache 授权覆盖 SDK，不重新许可单独分发的 GPL Host，也不对闭源宿主与 Host 的具体组合提供法律保证。

同一 Release 建议至少提供：

```text
Snaploom-<version>-windows-x64.*
Snaploom-<version>-macos-arm64.*
Snaploom-CaptureHost-<version>-windows-x64.zip
Snaploom-CaptureHost-<version>-macos-arm64.tar.gz
Snaploom-CaptureSDK-C-<version>-windows-x64.zip
Snaploom-CaptureSDK-C-<version>-macos-arm64.tar.gz
Snaploom-CaptureSDK-DotNet-<version>.nupkg
Snaploom-CaptureSDK-Swift-<version>.zip
Snaploom-<version>-corresponding-source.tar.zst
Snaploom-<version>-third-party-notices.txt
Snaploom-<version>-sbom.cdx.json
```

App、Host 和 SDK 可以共享版本号与 Release 页面，但必须有独立的包内 LICENSE/NOTICE/SBOM 身份。SDK 包只声明自身和宽松许可依赖；GPL Host 作为另一个下载项，不伪装成 SDK 的“数据文件”。

## 自动化门禁

迁移建立锁文件后，CI 和 Release 必须加入以下失败即阻断的检查：

1. 提交 `Cargo.lock` 与 `pnpm-lock.yaml`，生产构建使用 frozen/locked 模式；Git 依赖必须固定 commit。
2. 使用 [`cargo-deny`](https://github.com/EmbarkStudios/cargo-deny) 的 `cargo deny check licenses` 实施默认拒绝，只显式允许本报告的许可证；unknown/unlicensed 直接失败。
3. 分别为 GPL workspace 与 Apache SDK workspace 执行许可策略；SDK 的 allowlist 更窄，并对工作区内部反向依赖做结构测试。
4. 对 pnpm 生产依赖导出许可证清单，同时开启 Vite `build.license`；包缺少许可证、使用自定义条款或生产 bundle 的依赖未进入清单时失败。
5. 按 [REUSE 3.3](https://reuse.software/spec/) 建立 `LICENSES/GPL-3.0-or-later.txt`、`LICENSES/Apache-2.0.txt` 和 `REUSE.toml`，运行 `reuse lint`，保证每个文件有明确、可机读的许可证归属。
6. Rust 使用 [`cargo-cyclonedx`](https://github.com/CycloneDX/cyclonedx-rust-cargo)，前端使用 [`@cyclonedx/cyclonedx-npm`](https://github.com/CycloneDX/cyclonedx-node-npm)，再以 CycloneDX CLI 合并并校验每个平台的 release SBOM。CycloneDX 是组件与依赖清单格式，不替代许可证文本或 Corresponding Source。[CycloneDX 规范](https://cyclonedx.org/specification/overview/)
7. 从实际产物生成 `third-party-notices.txt`，并与 lockfile/SBOM 交叉核对；禁止手工维护一份会漂移的“预计依赖”表作为发布证据。
8. 依赖升级 PR 必须展示 license diff；新增“需复核”许可证必须有维护者批准和一手资料链接，新增“禁止”许可证直接失败。
9. Release job 验证二进制、对应源码、许可证文本、NOTICE、SBOM、校验和与 tag 版本一致后才允许发布。

现有 [`docs/licenses/dependency-licenses.md`](../licenses/dependency-licenses.md) 只描述将被删除的 .NET/Avalonia 依赖，而且仍以“未来闭源商业软件”为目标。它不能作为 Tauri 发布证据，迁移时应由自动生成的第三方声明与 SBOM 替代。

## 贡献者政策

纯开源方案不需要为了未来闭源重授权向贡献者索取广泛 CLA。建议采用：

- `CONTRIBUTING.md` 规定 inbound = outbound：贡献按目标文件/目录已经声明的许可证进入项目。
- 所有 commit 使用 `Signed-off-by` 接受 [Developer Certificate of Origin 1.1](https://developercertificate.org/)；CI 检查 DCO。
- PR 模板要求声明第三方代码、生成代码、素材来源和许可证；无可靠 provenance 不合并。
- 跨 GPL/Apache 边界的移动或复制必须由维护者复核，不能靠文件移动自动改变许可证。
- 项目名称、Logo 和发布签名不随代码许可证自动授权；另建简短商标政策，防止第三方构建冒充官方版本。

如果未来重新提出“闭源商业授权”，DCO + inbound=outbound 不会自动赋予项目方重授权所有外部贡献的权利；届时必须获得相关权利人同意、替换贡献，或重新设计贡献协议。这是选择纯开源、当前不售闭源例外的直接结果。

## 商业化与集成风险表

| 场景 | 当前判断 | 风险控制 |
| --- | --- | --- |
| 销售官方 GPL 应用 | 允许 | 接收者仍有复制、修改、再分发权；收入依赖品牌、服务、便利性与信任 |
| 闭源聊天系统只链接 Apache SDK | 许可上可行 | SDK 不含 GPL 代码，保持独立包和稳定 ABI |
| 闭源聊天系统启动独立 GPL Host，最终取得 PNG | 设计目标可行，但不是零法律风险 | Host 可独立运行；版本化服务级 IPC；不共享内部对象；发布前法律复核 |
| 闭源厂商再分发未修改 Host | 可分发，但厂商承担 GPL 义务 | SDK 文档提供再分发清单与 exact source 地址 |
| 闭源厂商修改 Host | 修改后的 Host 需按 GPL 提供对应源码 | 不提供闭源例外；把定制和合规支持作为服务 |
| 把 Host 作为 DLL/XCFramework 链入闭源应用 | 不符合本架构，风险高 | 官方 SDK 永不提供这种形式；只提供 Apache C ABI 客户端 |
| 把 Host 二进制内嵌进 SDK 包 | 容易模糊边界 | 同一 Release 独立资产，独立包内许可和 SBOM |
| 发布到带 DRM/额外限制的应用商店 | 需单独评估 | 首版仅 GitHub Release 直发 |
| 使用非开源/限制用途的截图或图像依赖 | 不接受 | CI denylist + lockfile license diff |

## 落地顺序与发布前检查点

1. 在删除旧实现前，先加入双许可证文件、REUSE 映射、贡献政策，并把现有 .NET 代码明确置于 `GPL-3.0-or-later`；随后再创建并推送 `dotnet-final`。否则该 tag 只有“可见源码”，没有清晰的开源授权。
2. 完成秘密与历史资产审计后再把 GitHub 仓库从 private 改为 public。当前仓库仍为 private 且 `licenseInfo` 为空，不能对外宣称已经开源。
3. Tauri 初始化时建立 GPL 与 Apache 两个清晰 workspace/目录层，并立即启用 `reuse lint`、Cargo/pnpm 许可门禁，避免迁移后补标。
4. 平台 Adapter、C ABI/IPC 和语言 SDK 的后续票必须把“实际依赖许可证与包内容”列为验收项。
5. Capture SDK/IPC 定稿后，研究建议由开源软件律师复核一次实际调用、安装、自动更新和再分发流程；当前 [#33](https://github.com/liuchuana/Snaploom/issues/33) 改为记录仓库所有者放弃该外部门禁并接受相应风险，不改变工程上的许可隔离要求。
6. 首个 public Release 以实际二进制反向校验 source、NOTICE 和 SBOM；缺一项就不发布。

## 一手资料

- [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU GPL Frequently Asked Questions](https://www.gnu.org/licenses/gpl-faq.html)
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- [Apache License 2.0 and GPL Compatibility](https://www.apache.org/licenses/GPL-compatibility)
- [Tauri 官方仓库及许可证](https://github.com/tauri-apps/tauri)
- [Tauri Architecture](https://github.com/tauri-apps/tauri/blob/dev/ARCHITECTURE.md)
- [Wry 官方仓库](https://github.com/tauri-apps/wry)
- [Tao 官方仓库](https://github.com/tauri-apps/tao)
- [Cargo manifest 参考](https://doc.rust-lang.org/cargo/reference/manifest.html)
- [TypeScript 官方仓库](https://github.com/microsoft/TypeScript)
- [Vite 官方仓库](https://github.com/vitejs/vite)
- [pnpm 官方仓库](https://github.com/pnpm/pnpm)
- [cargo-deny 官方仓库](https://github.com/EmbarkStudios/cargo-deny)
- [REUSE Specification 3.3](https://reuse.software/spec/)
- [Developer Certificate of Origin 1.1](https://developercertificate.org/)
- [CycloneDX specification](https://cyclonedx.org/specification/overview/)
- [CycloneDX Rust Cargo plugin](https://github.com/CycloneDX/cyclonedx-rust-cargo)
- [CycloneDX npm plugin](https://github.com/CycloneDX/cyclonedx-node-npm)
