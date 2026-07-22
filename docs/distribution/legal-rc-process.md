# 签名法律 RC draft 流程

`.github/workflows/release-legal-rc.yml` 是 Issue #51 的唯一法律 RC 入口。它只创建协作者可见的
GitHub draft，不包含把 draft 改为 stable、公开 prerelease 或推送 NuGet.org 的步骤。

## 启动前置条件

1. release commit 已合入 `main`，仓库版本、Tauri、NuGet、Swift URL/checksum 与 `X.Y.Z` 一致。
2. 不可移动的 `vX.Y.Z` tag 已指向该 commit；该 tag 尚无任何 GitHub Release，`v*` ruleset 禁止
   更新和删除 tag。
3. 同一 commit 的完整 `CI` 已成功，包含 Windows/macOS、四组 .NET consumer 与 `Atomic release contract`。
4. 同一 version/commit 的 `Release qualification` 已在 GitHub-hosted `windows-2025` 与 `macos-15`
   完成自动平台门禁；它不声称具备交互桌面或真机性能证据。
5. `release-qualification`、`release-signing` 与 `release-draft` environment 都只允许 `main`
   deployment。单人仓库允许所有者批准自己触发的 deployment，并保留 admin bypass；签名 secrets 只存在于
   `release-signing`：

   - `WINDOWS_SIGNING_CERTIFICATE_BASE64`
   - `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`
   - `WINDOWS_RFC3161_TIMESTAMP_URL`
   - `APPLE_DEVELOPER_ID_P12_BASE64`
   - `APPLE_DEVELOPER_ID_P12_PASSWORD`
   - `APPLE_DEVELOPER_IDENTITY`
   - `APPLE_NOTARY_KEY_BASE64`
   - `APPLE_NOTARY_KEY_ID`
   - `APPLE_NOTARY_ISSUER_ID`

PFX/P12/P8 只在临时文件和临时 keychain 中存在，清理步骤无论成功失败都会执行。签名 job 没有
Release 写权限；`release-draft` 中创建 draft 的 job 不接触签名 secrets。工作流只允许从 `main`
dispatch，所有构建仍显式 checkout 已验证 tag commit。

当前已有的公开 `v0.1.0` prerelease 占用了该 tag，不能删除、移动或替换后复用。首个签名法律 RC
必须在合入后使用新的 patch 版本完成 release prep。

## 运行输入与阶段

手动运行 `Signed legal RC draft` 时提供 `version`、完整 `commit`、成功的 `ci_run_id` 和
`qualification_run_id`。工作流依次执行：

1. 复验 tag/commit/main 可达性、版本、无同 tag Release、CI 与资格 workflow 的精确 head commit、
   required job 集及两平台 GitHub-hosted qualification 证据。
2. Windows 构建并签署 Desktop、Host、installer 与 C SDK DLL，验证 Authenticode 和 RFC3161。
3. macOS 构建 Developer ID hardened-runtime App/Host/DMG，等待 notarization，staple 后用
   `codesign`、`stapler` 与 Gatekeeper 复验；SDK dylib/XCFramework 保持 consumer-resignable。
4. 用已签名 Windows DLL 和同次 macOS dylib 生成双 RID NuGet，并在 Windows/macOS 分别执行
   .NET 8/.NET 10 的 package-only build/run、trim 与 NativeAOT；C/C++、Swift consumer 也直接消费
   同次最终 archive。
5. 保存 Windows/macOS runner image 与工具链版本、Apple 原始 notarization JSON，以及 codesign、
   stapler、Gatekeeper 复验日志；对应源码独立构建两次并逐字节比较。
6. 汇总精确 12 个 payload、独立 checksum、`SHA256SUMS`、SBOM/NOTICE 与 stable-signed manifest，
   再生成 GitHub provenance 和 SBOM attestations。
7. 创建 `draft=true, prerelease=false` 的 Release，每个文件只上传一次；工作流没有 `--clobber`。
8. 再次从远端 tag ref 解析真实 commit，通过 Release asset API 重新下载全部 26 个权威文件，复验名称、size、GitHub `sha256:` digest、
   manifest、签名/公证证据、许可证边界和 Swift checksum。
9. 上传 `snaploom-vX.Y.Z-issue-33-owner-approval` Actions artifact，其中含 draft URL、tag/commit、manifest
   digest、全部 payload checksums、签名/公证原始结果与日志、builder identity、完整 CI、最终签名
   consumer 与 hosted 资格证据，并保留无交互桌面/无真机性能证明的限制。

## 失败与所有者风险接受门禁

签名、公证、上传、重下载或消费校验任一步失败都不会公开 Release。若上传中途失败，保留的 partial
draft 不得补传或覆盖；在尚未提交所有者批准记录时可删除该 draft 后重新运行同一不可变输入。
一旦所有者批准记录已覆盖该候选，任何 byte 变化都必须使用新的 patch、manifest 和批准记录。

Issue #33 由仓库所有者本人填写日期、风险接受、结论、修改项、公开风险文案、决策记录
SHA-256/受控位置与签名，并明确设置 `acknowledgedWithoutExternalLegalReview=true` 和
`acknowledgedWithoutRealMachineQualification=true`。CI 和 Agent 不能代填，
本工作流成功也不能自动把 draft 改为 stable。stable publish 是后续独立事项，必须只消费同一已批准
draft，不能重建、重签或替换资产。记录格式、`stable-release` 环境与唯一公开入口见
[所有者批准后的稳定 Release 公开流程](./stable-release-process.md)。
