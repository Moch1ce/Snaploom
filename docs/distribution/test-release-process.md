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

## 真机与性能门禁

`.github/workflows/release-qualification.yml` 只调度带交互桌面的专用 self-hosted runner：

- Windows 10 22H2 x64；
- Windows 11 x64；
- macOS 14+ Apple Silicon。

每台 runner 提供 `SNAPLOOM_MACHINE_EVIDENCE_PATH` 指向本机原始 JSON。校验器要求证据精确匹配
version/commit，并覆盖普通用户安装/升级/卸载、复制/保存、单双屏、4K、混合 DPI/Retina、热插拔、
睡眠唤醒及失败恢复。PERF-01 必须来自独占的真实 3840×2160 Desktop、真实捕获 backend 和真实
WebView compositor；synthetic/headless 数据不能通过。原始样本要求至少 30 次快捷键/编辑和 20 次
PNG/成功资源循环，按 nearest-rank P95 与尾段两个 5 次窗口中位数重新计算。

## 稳定版不可降级规则

稳定资产必须使用 Authenticode SHA-256 + RFC3161、Developer ID + hardened runtime + notarization +
stapling，并通过 Gatekeeper/签名复验。缺少任一签名、公证、真机、PERF-01、合规、SDK consumer、
源码或 exact manifest 证据时只能保留 draft，不能发布 unsigned/ad hoc 或部分 Release。

Issue #33 的真实外部律师签署只阻止 stable publish，不阻止构建和 draft；CI 不生成、补写或代签
法律结论。稳定发布还必须使用同一 tag/commit 的已审核 draft 实物，不重建、不替换、不移动 tag。

最终签名候选的环境、Secrets、原子 draft、重下载复验与外部复核包操作见
[签名法律 RC draft 流程](./legal-rc-process.md)。
