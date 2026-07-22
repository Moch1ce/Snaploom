# 候选构建与 Release 门禁

Snaploom 的普通 CI 会生成完整但不可作为稳定版发布的候选资产。Windows 明确为
`candidate-unsigned`，macOS 明确为 `adhoc` 且不公证；候选资产只作为短期 Actions artifact，
不能因为构建成功就转换为稳定 Release。

## 每次 CI 的原子资产合同

`Atomic release contract` 等待 Windows、macOS、C/C++、.NET、Swift consumer 和合规作业全部
成功，再汇总同一 commit 的以下 12 个 payload：

- Windows x64 Desktop 用户级 installer 与 standalone Capture Host；
- macOS 14+ arm64 Desktop DMG 与 standalone Capture Host；
- Windows/macOS C SDK、双 RID NuGet 与 symbols、Swift XCFramework；
- 精确 commit 的 corresponding source、聚合 CycloneDX SBOM 和 NOTICE 索引。

汇总器拒绝重复、缺失、额外或空资产，检查 SDK 不含 Host/Tauri/GPL 产品路径，为每个 payload
生成独立 `.sha256`，并生成稳定排序的 `SHA256SUMS` 与 `release-manifest.json`。输出目录非空时
直接失败，不覆盖已有资产。所有 Actions 引用必须固定到完整 commit SHA，workflow 静态检查拒绝
`gh release upload --clobber`。

对应源码从精确 commit 以固定 prefix 生成，两次独立构建必须逐字节相同；它包含三个 workspace、
锁文件、协议/生成输入、打包脚本和许可证，但不包含签名凭据、缓存或用户数据。

## 平台包验证

Windows CI 使用 Inno Setup 6.7.1 构建普通用户安装包，并真实执行静默安装、Desktop 启动、
覆盖升级、Host 登记、一致性比较和卸载。macOS arm64 CI 构建并挂载 DMG，验证 Desktop 内嵌 Host
与 standalone Host 文件树完全相同、全部 Mach-O 为 arm64/minOS 14、权限文案、签名、启动和卸载。
两个应用安装包都硬限制为 `50,000,000` bytes。

## GitHub-hosted 自动平台门禁

`.github/workflows/release-qualification.yml` 只使用 GitHub-hosted `windows-2025` 与 `macos-15`。
两个 job 从精确 release commit 构建并验证候选包、平台 Rust 测试和 release contract tooling，随后生成
绑定 version/commit、runner image 与 architecture 的两份 qualification JSON。集合校验器只接受
`windows-x64` 和 `macos-arm64`，并明确记录 `no-interactive-desktop` 与
`no-real-machine-performance`；hosted 结果不得冒充 Windows 10/11 或 macOS 14 真机兼容性证明。

Windows 10/11 普通用户安装、真实 WGC/WebView2、macOS TCC/SCK/WKWebView、单双屏、混合
DPI/Retina、热插拔、睡眠唤醒以及 PERF-01 的真实桌面采样仍保留为人工验证建议。项目不配置 Windows
或 macOS self-hosted runner，这些人工项不再阻止稳定 RC 或 stable publish；未完成状态必须继续在
#44/#45 和审批包中如实披露。

## 稳定版平台信任规则

稳定 Windows 资产必须使用 `stable-unsigned`，确认 installer、Desktop、Host 与 SDK DLL 均未签名，并在
Release notes 与审批包披露未知发布者/SmartScreen 风险。稳定 macOS 资产也使用 `stable-unsigned`，只保留
无发布者身份的 ad hoc 结构签名且不提交公证，并披露 Gatekeeper 风险。双平台来源和完整性都依靠 GitHub
attestation、逐文件 `.sha256` 与 `SHA256SUMS`。缺少任一双平台风险披露、GitHub-hosted 自动平台门禁、
合规、SDK consumer、源码或 exact manifest 证据时只能保留 draft，不能发布部分 Release。
缺少真机与真实桌面 PERF-01 证据不会阻止发布；hosted qualification 与审批包必须如实披露这一限制。

Issue #33 的所有者风险接受记录只阻止 stable publish，不阻止构建和 draft；CI 不生成、补写或代签
该记录。稳定发布还必须使用同一 tag/commit 的已批准 draft 实物，不重建、不替换、不移动 tag。

最终稳定候选的环境、Secrets、原子 draft、重下载复验与所有者审批包操作见
[稳定 RC draft 流程](./legal-rc-process.md)；所有者记录通过后的唯一公开动作见
[稳定 Release 公开流程](./stable-release-process.md)。
