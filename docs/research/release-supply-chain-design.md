# Snaploom 应用与 SDK 发布供应链决策

状态：Issue [#31](https://github.com/moch1ce/Snaploom/issues/31) 的实施输入  
适用范围：Windows 10 22H2 / Windows 11 x64、macOS 14+ arm64、Snaploom 与 Capture SDK 1.x  
上位约束：[许可证审计](./tauri-open-source-license-audit.md)、[SDK 封装与分发](./capture-sdk-wrappers-distribution.md)、[Rust workspace 与测试体系](./rust-workspace-module-testing-design.md)、[法律复核事实包](../legal/capture-sdk-legal-review-packet.md)、ADR 0001～0004

> 本文是工程与发布流程决策，不是法律意见。Issue #33 现作为仓库所有者对精确发布候选的风险接受门禁；所有者必须明确记录该发布未经过外部法律复核。

## 1. 最终结论

Snaploom 使用一个版本、一个不可变 tag、一个 GitHub Release，原子公开一组彼此独立的 GPL 产品资产与 Apache SDK 资产：

1. 稳定版本 tag 固定为 `vX.Y.Z`，必须指向默认分支 `main` 可达的精确提交；tag 不移动、不覆盖、不复用。
2. Release 先创建为 draft，所有平台、SDK、源码、许可证、SBOM、NOTICE、校验和与 provenance 全部上传并解包复验后，才进行一次 `draft=false` 的公开动作。
3. Windows Desktop installer、macOS Desktop DMG、Windows/macOS standalone Capture Host 是 GPL-3.0-or-later 产品资产；C SDK、NuGet、Swift wrapper/XCFramework 是 Apache-2.0 SDK 资产。它们使用相同 `X.Y.Z`，但保持不同文件、包内许可证、SBOM 与安装发现边界。
4. Desktop installer/DMG 可以携带同版本独立 Host，方便 Snaploom 自用；standalone Host 仍作为单独下载资产提供。SDK 包永远不携带、下载或链接 Host。
5. 稳定 Windows 产品二进制与 installer 使用 Authenticode SHA-256 签名和 RFC 3161 时间戳；稳定 macOS App、Host 与 DMG 使用 Developer ID、hardened runtime、公证和 stapling。签名或公证失败不得降级为 unsigned/ad hoc。
6. 普通候选构建可以 unsigned/ad hoc，并可产生 prerelease 或 CI artifact；它不需要 #33 批准。供 #33 核对的“法律 RC”必须是最终签名、最终打包、最终 checksum 的 draft Release 实物。
7. #33 所有者批准只阻止 stable publish，不阻止 build、test、签名候选、创建 draft、生成 SBOM/NOTICE 或执行消费测试。没有批准时流程必须停在 draft。
8. GitHub Release 是公共资产的首个不可逆发布点。NuGet.org 发布在 GitHub stable Release 成功后执行；SwiftPM 直接消费同一 Git tag 与 Release XCFramework，不在首版引入第二个 Swift registry。
9. 所有 payload 都有独立 `.sha256`，另发布权威 `SHA256SUMS` 与 `release-manifest.json`；稳定公开资产生成 GitHub artifact attestation。校验和、签名、notarization 和 attestation 是互补证据，不能互相替代。
10. 对未签名中间产物争取 bit-for-bit 可复现；对含 Authenticode 时间戳、Developer ID、公证 ticket 的最终产物只承诺“相同源码与锁定输入可重建、最终 bytes 有可验证来源”，不伪称签名产物逐字节可复现。

## 2. 发布模式与不可逆点

### 2.1 候选构建

`release-candidate` 由 `workflow_dispatch` 接收版本和 `main` 上的完整 commit SHA：

- 恢复锁定依赖、运行完整测试、构建所有资产、生成 manifest/SBOM/NOTICE/checksum；
- 默认 Windows unsigned、macOS ad hoc，不访问稳定签名凭据；
- 可使用 `vX.Y.Z-rc.N` 发布公开 prerelease，但 RC tag 与资产同样不得移动或替换；
- 可创建仅协作者可见的 draft，用于最终签名候选与外部复核；
- 失败只产生短期 Actions artifacts，不创建或公开残缺 Release。

候选构建的成功不是稳定发布批准。候选与稳定必须运行同一构建脚本、同一包内容验证器和同一资产 manifest schema，差异只限签名模式、tag 形状与发布门禁。

### 2.2 稳定 draft

推送 `vX.Y.Z` 后，tag workflow 可以在没有 #33 所有者批准时完成：

1. 校验 tag、版本、默认分支可达性、Release 唯一性和 package manifest。
2. 从 tag 全新构建，不复用开发机 `artifacts/`、缓存中的未验证二进制或另一个 commit 的产物。
3. 使用受保护 signing environment 生成最终签名/公证资产。
4. 汇总全部资产，验证 exact asset set、版本、架构、签名、包内容、许可证、SBOM 与 checksum。
5. 创建 draft Release，上传全部资产，重新下载并复验 GitHub 存储后的 bytes。
6. 生成供 #33 使用的 review bundle：draft URL、tag/commit、`release-manifest.json` digest、全部 payload checksum、包内容清单、签名/公证输出与消费测试结果。

该阶段没有 `contents: write` 以外的发布副作用，也不向 NuGet.org push。

### 2.3 稳定公开

独立 `publish-stable` job 只能接受既有 draft release ID，不重建、不重签、不替换资产。它必须同时证明：

- draft 的 tag、commit、资产集合和 checksum 与所有者批准对象完全相同；
- Issue #33 已 `closed/completed`，且存在仓库所有者的风险接受记录；
- 批准记录包含 tag、commit、draft URL、manifest digest、产物 checksums、所有者身份、日期、结论、决策记录 hash/受控存档位置；
- 结论为接受、修改项为空，且明确确认未经过外部法律复核；
- GitHub `stable-release` environment 允许单人仓库所有者自批并保留管理员 bypass；
- Windows/macOS GitHub-hosted 自动平台门禁、包装、SDK consumer、合规与合同映射 required checks
  全部成功；批准记录明确接受缺少交互桌面与真机性能资格证明。

CI 只校验记录存在、所有者身份、字段和 digest 匹配，不能生成、补写或代签批准记录。任一条件不满足时 draft 保留或删除，但不能公开。

GitHub 官方对 immutable releases 的建议也是“先 draft、附加全部资产、再公开”；公开后 tag 与资产不可修改。项目启用 immutable releases；若仓库计划暂不支持该能力，仍以 tag ruleset 禁止更新/删除并执行同样的不可变策略，不能以缺少平台功能为理由覆盖资产。[GitHub Immutable releases](https://docs.github.com/en/enterprise-cloud@latest/code-security/concepts/supply-chain-security/immutable-releases)

## 3. 权威资产清单

`release-manifest.json` 中的 expected set 是发布真相；GitHub Release 不得多传调试文件、临时 metadata 或未列出的包。

| 资产 | 许可/用途 | 必须包含或满足 |
| --- | --- | --- |
| `snaploom-X.Y.Z-windows-x64-setup.exe` | GPL Desktop installer | 用户级、Win10 build 19045+、x64、Desktop + 独立 Host、签名、≤50,000,000 bytes |
| `snaploom-X.Y.Z-macos-arm64.dmg` | GPL Desktop DMG | `Snaploom.app` + Applications link、Desktop + 独立 Host、arm64、macOS 14+、Developer ID、公证/staple、≤50,000,000 bytes |
| `snaploom-capture-host-X.Y.Z-windows-x64.zip` | GPL standalone Host | 已签名 Host executable、安装/登记说明、GPL、Host SBOM/NOTICE、对应源码指引 |
| `snaploom-capture-host-X.Y.Z-macos-arm64.zip` | GPL standalone Host | 已签名并公证/staple 的 Host `.app`、arm64、macOS 14+、GPL、Host SBOM/NOTICE |
| `snaploom-capture-sdk-c-X.Y.Z-windows-x64.zip` | Apache C/C++ SDK | header、C++17 wrapper、signed DLL/import lib、CMake、示例、Apache LICENSE、SDK SBOM/NOTICE |
| `snaploom-capture-sdk-c-X.Y.Z-macos-arm64.tar.gz` | Apache C/C++ SDK | header、C++17 wrapper、arm64 dylib、CMake/pkg-config、示例、Apache LICENSE、SDK SBOM/NOTICE |
| `Snaploom.Capture.X.Y.Z.nupkg` | Apache C# SDK | `net8.0` assembly、win-x64/osx-arm64 native assets、README、LICENSE、SBOM/NOTICE；不得含 Host |
| `Snaploom.Capture.X.Y.Z.snupkg` | symbols/source link | 仅与同版本 NuGet 匹配的 portable symbols/source mapping |
| `CSnaploomCapture-X.Y.Z.xcframework.zip` | Apache Swift binary target | archive 根为 arm64 XCFramework、header/module map、macOS 14 deployment target；不得含 Host |
| `snaploom-X.Y.Z-corresponding-source.tar.zst` | GPL Corresponding Source | 精确 tag 源码、锁文件、`.proto`、生成器输入、构建/安装/打包脚本、REUSE/许可证、修改版构建说明 |
| `snaploom-X.Y.Z-sbom.cdx.json` | Release 聚合 SBOM | 引用每个产品/SDK component 与各自 SBOM，不把 GPL/Apache 身份混成一个包 |
| `snaploom-X.Y.Z-third-party-notices.txt` | Release 索引 | 按资产列出对应 NOTICE/第三方声明位置；不能替代包内材料 |
| `release-manifest.json` | 权威发布描述 | schema、tag、commit、version、每个资产 size/digest/license/kind、signing/notary、Swift checksum、NuGet identity、builder/run |
| `SHA256SUMS` | 全部 payload 摘要 | 只列 manifest 中的 payload，稳定排序，LF；本文件本身由 attestation 保护 |

除 `release-manifest.json`、`SHA256SUMS` 与聚合 SBOM/NOTICE 外，每个 payload 都有同名 `.sha256`。`.sha256` 不递归拥有自己的 `.sha256`。

Desktop 包内的 Host 与 standalone Host 必须来自同一次已签名 Host staging 输出；Windows 比较 executable hash，macOS 比较规范化 `.app` 内容 manifest。SDK DLL/dylib 在 C archive、NuGet 与 XCFramework 中也必须来自同一次 SDK native staging，不允许各包重新编译出“同版本不同 bytes”。

## 4. 许可证、NOTICE、SBOM 与对应源码

### 4.1 产物身份

- Desktop、Host 与 product workspace 使用 `GPL-3.0-or-later`。
- protocol、C ABI/client、C/C++/C#/Swift wrappers 与示例使用 `Apache-2.0`。
- Apache SDK 的解包扫描必须拒绝 Host、Tauri、`capture-session`、platform Adapter 文件/symbol、GPL LICENSE 声称和下载 Host 的代码。
- GPL 产品可包含它所依赖的 Apache protocol/client，但必须在其第三方声明中准确标注；Apache SDK 不能反向包含 GPL 代码。
- 每个包的 SBOM 只描述实际解包内容；聚合 SBOM 用 composition/dependency relationship 引用各资产，不改变它们的许可证。

### 4.2 SBOM 与 NOTICE 生成

SBOM 以最终 lockfile、最终 enabled features 和最终解包树生成，而不是从预计依赖表抄写：

1. Rust 分别从 `product/Cargo.lock`、`sdk/Cargo.lock` 生成 CycloneDX component/dependency。
2. pnpm 从 frozen `pnpm-lock.yaml` 和 production bundle 生成前端依赖清单。
3. NuGet、Swift/XCFramework、Tauri/plugin、工具携带的 runtime 分别标 scope。
4. 对每个资产生成独立 `sbom.cdx.json` 与 `THIRD-PARTY-NOTICES.txt`，解包后交叉验证 component、license 与实际文件。
5. 运行 CycloneDX schema validate、`cargo deny check licenses`、`reuse lint`；unknown/unlicensed 或 SDK 中强 copyleft 直接失败。
6. 稳定资产同时生成 SBOM attestation；GitHub 说明 artifact attestation 可绑定构建来源，也可附带 SBOM，但 attestation 不是安全或许可证正确性的替代保证。[GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)

### 4.3 Corresponding Source

不把 GitHub 自动生成的 source ZIP/tarball 当唯一 GPL 履约材料。自建 source archive 必须：

- 由 tag checkout 生成，记录 commit、submodule SHA（如有）和 toolchain/runner 清单；
- 包含构建、安装、运行和修改 GPL App/Host 所需的源码、接口定义、生成输入、配置和打包脚本；
- 包含 `Cargo.lock`、`pnpm-lock.yaml`、Windows bindings filter/generator、protobuf schema/build 输入；
- 不包含签名私钥、notary key、NuGet token、开发机缓存、截图 fixture 中的真实用户数据；
- 包含如何生成 unsigned/ad hoc 修改版的说明，不让官方签名成为协议层准入条件；
- 使用固定排序、uid/gid、文件 mode、commit time `SOURCE_DATE_EPOCH` 与固定压缩器生成，第二次构建必须产生相同 digest。

GPLv3 对 object code 分发要求提供对应源码及安装/修改所需脚本；Apache-2.0 要求保留适用版权、专利、归属和 NOTICE。[GNU GPLv3](https://www.gnu.org/licenses/gpl-3.0.html)、[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## 5. 版本、tag 与 Swift checksum

### 5.1 单一版本来源

`release-version` 工具从 tag 解析唯一 `X.Y.Z`，并校验：

- Product/SDK Cargo package version、Tauri config、Windows file/product version、macOS `CFBundleShortVersionString`、NuGet package version 与 Swift package URL 全部一致；
- C ABI major 仍为 1，IPC major/minor 按各自合同校验，不能用 release semver 覆盖 ABI/wire 版本轴；
- release tag commit 可从 `origin/main` 到达，tag/release 名均未使用；
- tag ruleset 禁止非 release principal 创建、更新或删除 `v*`。

GitHub Actions 中所有外部 actions 固定到完整 commit SHA；GitHub 官方说明 full-length SHA 是不可移动的 action 引用，并支持仓库策略强制该要求。[GitHub Actions settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository)

### 5.2 SwiftPM 两阶段准备

远程 binary target 的 checksum 必须写在 tag 所指向的根 `Package.swift` 中，而 checksum 只有最终 XCFramework ZIP 生成后才能确定。稳定发布采用两阶段：

1. release-prep commit 在锁定 toolchain 上构建规范化 XCFramework ZIP，运行 `swift package compute-checksum`。
2. 将同版本 GitHub Release URL 与 checksum 写入 `Package.swift`，形成最终 release commit。
3. 对该 commit 重建 XCFramework ZIP；bytes/checksum 必须与 prep 输出相同才允许打 tag。
4. tag workflow 再次重建并验证相同 checksum，随后把该 ZIP 上传同一 Release。

SwiftPM 官方要求远程 binary target 使用 URL 与 archive checksum，并建议用 `swift package compute-checksum` 计算 SHA-256；archive 根必须是 binary artifact。[Swift checksum](https://docs.swift.org/swiftpm/documentation/packagedescription/target/checksum/)、[Swift package release](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/releasingpublishingapackage/)

XCFramework ZIP 必须使用固定文件顺序、时间、权限和压缩工具。macOS SDK dylib/XCFramework 作为第三方应用的嵌入输入采用可重签的分发签名形态，消费方构建必须验证并以自身应用身份签署嵌入内容；不得把 Snaploom Developer ID 当成闭源宿主运行 SDK 的协议条件。

## 6. 可复现性与构建来源

### 6.1 必须 bit-for-bit 的资产

- protocol golden、生成的 header/bindings；
- C/C++ headers、managed/Swift source；
- 规范化 C SDK archive、XCFramework ZIP、NuGet package；
- corresponding-source archive、SBOM、NOTICE、manifest 与 checksum；
- 同一次 staging 输出进入多个包时的 native binary。

这些资产在相同 tag、toolchain、lockfile 与环境下做双构建 digest compare。无法复现时先定位时间、路径、随机 UUID、archive metadata 或生成器漂移，不允许更新 `Package.swift` checksum 掩盖问题。

### 6.2 不承诺最终 bytes 相同的资产

Authenticode RFC 3161 时间戳、Apple Developer ID 时间戳、notarization/staple 和部分平台 installer metadata 会改变 bytes。对 Windows installer、Developer ID App/Host 与 DMG：

- 保存签名前 payload manifest 和最终 payload digest；
- 记录签名证书 subject/thumbprint、timestamp policy/result、Apple Team ID、notary submission ID/status、stapler 与验证结果；
- 从 tag 重新构建时验证未签名内容、包结构与依赖相同，重新签名后的最终 bytes 由 attestation 绑定到 workflow/commit；
- 发布文案使用“可重建且来源可验证”，不使用“最终签名包 bit-reproducible”。

所有 dependency restore 使用 `cargo --locked`/适用 `--frozen`、pnpm frozen lockfile、NuGet locked restore；工具链由 `rust-toolchain.toml`、Corepack/pnpm version、.NET SDK、Swift/Xcode 与 Windows SDK/Inno/Tauri 版本锁定。workflow 记录 runner image 版本和工具 `--version` 输出。

## 7. Windows 构建、签名与安装

### 7.1 顺序

1. 在 Windows x64 runner 构建 Release Desktop、Host 与 C SDK DLL。
2. 运行 ABI exports、machine type、依赖、无 GPL-in-SDK 与 package consumer 测试。
3. 稳定模式先签 DLL/EXE，使用 SHA-256 file digest 和 RFC 3161 SHA-256 timestamp。
4. 验证每个签名、证书链、timestamp 与预期 signer；warning exit code 也视为失败。
5. 把同一个 signed Host 放入 Desktop installer staging 和 standalone Host ZIP。
6. 构建用户级 installer，再对 installer 本身签名与时间戳。
7. 运行静默安装、启动、覆盖升级、卸载、HKCU 登记、Host 发现、保留设置/日志与普通用户测试。
8. 对最终 installer/ZIP/DLL 重新计算 digest，生成 SBOM/NOTICE 与 attestation。

SignTool 官方要求显式指定 file digest 与 timestamp digest，并推荐 SHA-256；`verify /pa` 使用默认 Authenticode 策略检查签名。[Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)

### 7.2 模式

- `candidate-unsigned`：明确 metadata 与 release notes 标记 unsigned，不访问签名环境。
- `stable-signed`：证书、timestamp、完整 verify 都是 hard gate；不能因 provider 暂时不可用发布 unsigned。
- SDK Windows DLL 与产品 EXE 均签名；`.lib`、header、SBOM、source 不做 Authenticode，依赖 checksum/attestation。

## 8. macOS 构建、签名与公证

### 8.1 顺序

1. 在 macOS arm64 runner 构建最低 macOS 14 的 Desktop、Host、C SDK dylib 和 XCFramework。
2. 验证所有 Mach-O 仅 arm64、deployment target、install name、ABI exports、bundle ID、Info.plist 本地化权限说明和 entitlements。
3. 从最内层 dylib/helper 开始，先签 standalone Host，再签 Desktop bundle；保持 library validation，不携带旧 Swift bridge、`allow-jit`、`disable-library-validation` 或 `get-task-allow`，除非后续 ADR 对 Tauri 实物证明最小例外。
4. 对 Host/应用执行 `codesign --verify --deep --strict`，检查 Developer ID Application identity。
5. 构建 standalone Host ZIP，提交公证；Accepted 后 staple Host `.app`，重新形成最终 ZIP并验证 Host Gatekeeper。
6. 把同一个已签名 Host staging 放入 Desktop，构建并签署 DMG，使用 `notarytool --wait` 公证，staple/validate DMG，并执行 `spctl`。
7. 运行 DMG mount/install/launch/uninstall、TCC 文案、Host 发现、Spaces/Retina 与包内容测试。
8. 保存 notary submission ID、结果 JSON 与验证日志；日志和 artifact 不包含 private key。

Apple 对 Developer ID 的官方流程要求签名、提交 Apple notary service，并可用 `notarytool` 与 `stapler` 完成自定义流水线。[Apple Developer ID](https://developer.apple.com/developer-id/)、[Notarizing macOS software](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)

### 8.2 模式

- 普通 candidate 使用 ad hoc，不公证，并明确 Gatekeeper 风险。
- 法律 RC 与 stable 使用 Developer ID + hardened runtime + notarization + staple；任何一步失败都停在 draft。
- Apache C SDK/XCFramework 必须能被第三方宿主在其产品构建中重新签名，不能依赖与 Snaploom Host 相同 Team ID。

## 9. GitHub Actions 权限、Secrets 与 provenance

workflow 顶层默认：

```yaml
permissions:
  contents: read
```

按 job 最小提权：

| Job | 权限/环境 | 规则 |
| --- | --- | --- |
| build/test/compliance | `contents: read` | 无 secrets，无 write token，不能从 fork/PR 进入签名 |
| attest | `contents: read`, `id-token: write`, `attestations: write` | 只对已验证最终 payload/SBOM生成 provenance |
| assemble-draft | `contents: write` | 只创建/上传 draft；无签名、Apple、NuGet secrets |
| windows-sign | protected `release-signing` | 只取得 Windows provider 所需最小短期凭据；不拥有 Release write |
| macos-sign-notary | protected `release-signing` | ephemeral keychain 与 App Store Connect API key；不拥有 Release write |
| publish-stable | `contents: write`, `issues: read` + `stable-release` | 只把所有者已批准并复验的 draft 公开；允许单人所有者自批 |
| publish-nuget | `id-token: write`, `contents: read` + `nuget-org` | GitHub stable 成功后以 NuGet Trusted Publishing 换短期 key |

安全规则：

- 所有 `uses:` 固定完整 commit SHA并由 Dependabot/人工复核升级。
- checkout `persist-credentials: false`；build job 不保留可写 token。
- 签名和 publish job 不执行来自 PR、fork、外部 artifact 的脚本；只 checkout 已验证 tag。
- PFX/Apple private key 如无法使用 OIDC/托管签名服务，只放 environment secret，写入临时文件/ephemeral keychain，job 结束立即删除。
- 不把 secret 作为命令行回显、metadata、SBOM、日志、cache 或 upload-artifact 内容。
- cache 只缓存下载依赖，不缓存签名后 payload；恢复后仍执行 lock/integrity 校验。
- release concurrency 使用 tag 与 stable environment 双重串行，`cancel-in-progress: false`。

GitHub 建议显式声明最小 token 权限、把外部 action 固定到完整 SHA；environment 可在保护规则通过前阻止 job 获取 environment secrets。[GitHub secure use](https://docs.github.com/en/actions/reference/security/secure-use)、[GitHub deployments/environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)

稳定公开资产使用 `actions/attest` 生成 build provenance；公开仓库的 attestation 使用公共 Sigstore transparency log。消费者可以执行 `gh attestation verify <asset> -R moch1ce/Snaploom`。项目必须在文档中同时说明 attestation 只证明来源，不证明软件无漏洞。[GitHub attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations)

## 10. Draft 原子公开协议

### 10.1 汇总验证

Linux aggregator 从各平台下载 Actions artifacts 后必须：

1. 拒绝重复文件名、symlink 逃逸、绝对路径、空文件和 manifest 外资产。
2. 对每个 artifact 先校验生产 job 输出的 digest，再解包验证内部 LICENSE/NOTICE/SBOM/version/architecture。
3. 校验 `release-manifest.json` 的 tag、commit、version、asset size/digest、license kind、Swift checksum 和 NuGet ID。
4. 校验 Desktop 包含同版本 Host；standalone Host 与内嵌 Host staging 相同。
5. 校验 NuGet 双 RID native assets、XCFramework module import、CMake consumer、wrapper/ABI versions。
6. 校验 corresponding source 与 GPL binary 使用相同 commit/version，且 source archive 可独立恢复 locked build。
7. 生成稳定排序的 `SHA256SUMS`，二次计算验证所有同名 `.sha256`。
8. 创建 draft、上传 exact set，再通过 GitHub API 读取 assets，比较 name/size/server digest；重新下载到空目录并重复 checksum。

GitHub Releases REST API提供 create/update Release 与 upload/list assets；draft 与 prerelease 是独立字段，稳定发布显式使用 `draft=false, prerelease=false, make_latest=true`。[GitHub Releases REST API](https://docs.github.com/en/rest/releases/releases)

### 10.2 公开动作

publish job 不调用 `gh release upload --clobber`，不删除或替换资产。它只：

1. 重新读取 draft 与 manifest；
2. 校验 #33、环境批准与所有 required checks；
3. 比较 exact asset set、size/digest、tag/commit；
4. PATCH 一次 `draft=false, prerelease=false, make_latest=true`；
5. 读取 published Release，验证 `published_at`、immutable 状态和全部 assets；
6. 生成发布后证据 artifact，不修改 Release 内容。

Windows、macOS、Host、SDK、source 或合规资产任一缺失都不得公开“部分可用”Release。

## 11. NuGet 与 Swift 第三方分发

### 11.1 NuGet.org

NuGet publish 是 GitHub stable 成功后的独立 promotion：

1. 从已公开 GitHub Release 按 checksum 重新下载 `.nupkg/.snupkg`，不从 build workspace 取另一份。
2. 使用 nuget.org Trusted Publishing 的 GitHub policy + `release` environment，通过 OIDC 换一小时短期 API key，不保存长期 NuGet API key。[NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
3. push 前解包复验 package ID/version/native assets/license/SBOM 与 manifest digest。
4. 不使用 `--skip-duplicate` 掩盖冲突。若同 ID/version 已存在，下载/校验可获得的已发布包；bytes/签名不一致则 hard fail并触发安全响应，相同才视为幂等成功。
5. NuGet 成功后运行空项目 restore/build/run、win-x64/osx-arm64 RID、trim/NativeAOT smoke。

GitHub 必须先公开，因为 NuGet push 不可与 GitHub Release 建立跨服务事务；反向顺序会产生“只有 NuGet SDK、没有同版本独立 Host/source”的更危险公开状态。NuGet 暂时失败时 GitHub Release 仍包含完整 `.nupkg` 下载，发布状态标记为 external promotion pending，重试同一已验证 bytes，不创建新包。

### 11.2 SwiftPM

Swift 首版不向第三方 registry push。稳定 tag 根 `Package.swift` 与同一 Release 的 `CSnaploomCapture-X.Y.Z.xcframework.zip` 即是分发：

- publish 后从干净 consumer 使用 Git URL + version 解析；
- 验证 remote binary checksum、下载、`swift build/test` 与 Xcode macOS app embed/sign；
- checksum mismatch 必须失败，不能改 tag 或替换 ZIP；修复只能发布新 patch。

Swift Package Index 等目录收录是发布后的可选元数据操作，不是资产完整性门禁，也不得持有签名/Release write 权限。

## 12. 回滚、失败与重发

### 12.1 公开前

- build/sign/notary/package/legal 任一失败：保持或删除 draft，不公开、不 push NuGet。
- 已存在错误 tag：不移动、不删除后复用；修复 commit 使用新版本/tag。
- 已公开 RC 错误：发布新的 `-rc.N+1`，不替换旧 RC URL/asset。
- draft asset 若重建导致 digest 改变，必须生成新 manifest 和新的 #33 审批对象；旧批准自动失效。

### 12.2 公开后

- immutable stable Release、tag、asset、Package.swift checksum 永不覆盖。
- 修复使用新 patch `vX.Y.(Z+1)`，完整重跑 build、法律门禁适用性、签名、资产与 registry promotion。
- 严重安全问题通过 GitHub Security Advisory/明确公告，必要时将 NuGet 旧版本 unlist；不能上传同版本“修正版”。
- NuGet push 成功而后续 consumer smoke 失败：不覆盖包；停止推广、unlist（如必要）并发布新 patch。
- notarization/timestamp 服务故障：停止 stable；不发布 ad hoc/unsigned 替代品。

所谓 rollback 是停止推广、撤下索引可见性或发布新 patch，不是改写已经公开的供应链历史。

## 13. 验收矩阵

### 13.1 全资产共同门禁

- tag/commit/main reachability、版本、asset manifest、checksum 全一致；
- 两个 Rust workspace locked build，pnpm frozen build，generated clean diff；
- `reuse lint`、product/sdk 各自 `cargo deny`、CycloneDX validate；
- payload 无 secrets、绝对开发路径、真实截图/标注文字、调试 dump；
- 每个 package 内 LICENSE/NOTICE/SBOM 与实际文件匹配；
- corresponding source 可从干净环境恢复并构建 unsigned/ad hoc 修改版；
- 每个稳定 payload attestation 可由 `gh attestation verify` 验证。

### 13.2 Windows

- x64、Windows 10 build 19045 minimum、PerMonitorV2、普通用户、≤50 MB；
- Desktop/Host/DLL/installer Authenticode signer、SHA-256/RFC3161 timestamp 与 `/pa` verify；
- 静默 install/launch/overwrite/uninstall、HKCU uninstall/Host registration、设置日志保留；
- SDK DLL exports 精确 7 symbols，C/C++/C# consumer 从最终包运行。

### 13.3 macOS

- 所有 Mach-O 仅 arm64、minimum 14、bundle/install name 正确；
- Desktop/Host leaf-to-root Developer ID、hardened runtime、无未批准 entitlement；
- DMG/Host notarization Accepted、staple validate、`spctl` 通过；
- DMG mount/install/launch/uninstall、权限文案、Host discovery；
- XCFramework import、SwiftPM checksum、consumer embed/re-sign/run。

### 13.4 SDK 与边界

- C archive、NuGet、XCFramework 不含 Host/Tauri/GPL product symbols/files；
- C header layout/cdecl、7 symbols、C++ RAII、C# SafeHandle/GC、Swift continuation/cancel；
- Completed/Canceled/Failed/Busy/crash/timeout、exactly-once、completion free；
- App/Host clipboard/save/SDK result 使用相同 PNG bytes；
- Host locator 只接受登记或显式绝对路径，不下载/从 PATH 猜测。

### 13.5 稳定发布人工门禁

- Issue #33 所有者风险接受记录已完成且精确覆盖当前 tag/commit/draft/checksum；
- `stable-release` 环境由仓库所有者批准，可与触发人为同一人；
- release notes 明确 GPL App/Host 与 Apache SDK 独立、Host 安装/发现、未承诺“IPC 自动消除 GPL 风险”；
- Windows/macOS GitHub-hosted 自动门禁有当前版本证据；所有者明确接受没有真机 4K 性能、20+
  循环资源稳定与安装矩阵证明即发布。人工矩阵继续作为非阻塞建议跟踪。

## 14. 对现有 release workflow 与脚本的迁移要求

现有 `.github/workflows/release.yml` 已有可保留的骨架：

- tag 格式、默认分支可达性、Release 唯一性；
- Windows/macOS 并行构建；
- installer/DMG checksum 与 metadata；
- draft 上传、exact asset set 验证、最后一次 PATCH 公开；
- `cancel-in-progress: false`。

Tauri 迁移时必须替换/加强：

1. 删除 .NET restore/build/test 与旧 `allow-jit`、`disable-library-validation` 假设。
2. 将旧的四资产 prerelease 扩展为本文 exact asset matrix，不再只验证 installer/DMG。
3. 将 `actions/*@vN` 改为 full commit SHA。
4. 拆分 build、sign、attest、draft、stable publish、NuGet promotion，避免同一 job 同时持有源码执行、签名 secret 和 Release write。
5. 旧 Issue #18 的自动合同迁移到 GitHub-hosted qualification；真机/性能矩阵降级为非阻塞人工建议，
   并由 #33 所有者记录明确接受缺少这些证明的发布风险。
6. Windows installer 从 unsigned candidate 增加 stable Authenticode 模式，并在 installer 内安装/登记独立 Host。
7. macOS 脚本删除旧 Swift dylib/.NET entitlement，分别构建/签名 Host 与 Desktop，稳定模式强制 Developer ID + notarization。
8. metadata schema 升级并并入 `release-manifest.json`，记录 commit、builder、signing/notary、license/SBOM/source。
9. 发布后启用 immutable Release/attestation；任何 `--clobber`、覆盖 tag 或同版本重传均由静态 workflow 检查拒绝。

## 15. 明确失败降级

| 失败 | 唯一允许行为 |
| --- | --- |
| Windows/macOS 任一平台失败 | draft 不公开；不发布另一平台 |
| 任一 Host/SDK/source/SBOM/NOTICE 缺失 | draft 不公开 |
| Windows 签名或 timestamp 失败 | stable 停止；不降级 unsigned |
| Apple signing/notary/staple 失败 | stable 停止；不降级 ad hoc |
| #33 未批准、未确认无外部复核，或记录与 checksum 不同 | 保留候选/draft；stable publish 禁止 |
| immutable Release 不可用 | 强制 tag ruleset与不覆盖政策；不能用 overwrite 模拟修复 |
| NuGet.org/OIDC 暂时失败 | GitHub stable 保持；重试相同 nupkg，不创建新 bytes |
| Swift checksum 与 ZIP 不同 | stable 停止；修正 prep commit并使用新 tag |
| SBOM/NOTICE/许可证未知 | stable 停止；不以手工豁免绕过 SDK窄 allowlist |
| 已公开资产发现错误 | 公告/unlist（适用时）+ 新 patch；不覆盖历史 |

## 16. 实施顺序

1. 建立 `release-manifest.json` schema、命名器、checksum 与 exact asset set verifier。
2. 建立 product/sdk/web locked build、双工作区 license/SBOM/NOTICE 和 deterministic source archive。
3. 建立 Windows Desktop/Host/C SDK staging、签名模式、installer 与消费验证。
4. 建立 macOS Desktop/Host/C SDK/XCFramework staging、Developer ID/notary/staple 与消费验证。
5. 建立 C archive、NuGet、Swift Package checksum 的双构建与解包边界扫描。
6. 将 workflow 拆成 candidate、tag-build/draft、stable-publish、NuGet-promotion 四个不可混权阶段。
7. 配置 tag ruleset、immutable releases、full-SHA actions、`release-signing`/`stable-release`/`nuget-org` environments 与 OIDC policies。
8. 生成最终法律 RC draft 与审批包，由仓库所有者在 #33 核对并记录风险接受；批准前只停留在 draft。
9. 所有者批准与全验收通过后，一次公开 GitHub stable，再把同一 nupkg promotion 到 NuGet.org并验证 SwiftPM消费。

任何后续增加应用商店、自动更新、Windows ARM64、Intel/Universal Mac、静态 SDK、Node/Java/Python binding、Host-in-SDK packaging 或新的第三方 registry，都必须新开票重新审查许可、签名、资产原子性与 rollback，不能作为本工作流的隐藏分支。

## 17. 一手资料

- GitHub Release：[REST Releases API](https://docs.github.com/en/rest/releases/releases)、[Immutable releases](https://docs.github.com/en/enterprise-cloud@latest/code-security/concepts/supply-chain-security/immutable-releases)
- GitHub Actions 安全：[Secure use](https://docs.github.com/en/actions/reference/security/secure-use)、[Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)、[Artifact attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations)
- Windows 签名：[SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)、[Authenticode timestamps](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures)
- Apple 分发：[Developer ID](https://developer.apple.com/developer-id/)、[Notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
- NuGet：[Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)、[Native package assets](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages)
- SwiftPM：[Releasing a package](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/releasingpublishingapackage/)、[Binary target checksum](https://docs.swift.org/swiftpm/documentation/packagedescription/target/checksum/)
- 许可证：[GNU GPLv3](https://www.gnu.org/licenses/gpl-3.0.html)、[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)、[CycloneDX](https://cyclonedx.org/specification/overview/)
