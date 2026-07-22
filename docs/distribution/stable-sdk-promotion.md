# 稳定 SDK 的 NuGet.org promotion 与 SwiftPM 验证

`.github/workflows/release-promote-sdk.yml` 是 GitHub stable Release 公开之后唯一的 SDK promotion
入口。它只从该不可变 Release 重新下载 `Snaploom.Capture` 的 `.nupkg`/`.snupkg` 与
`CSnaploomCapture` XCFramework，不重新构建、重新打包、替换 Release 资产或移动 tag。

## 一次性外部配置

在首次调度前，仓库管理员和 NuGet.org package owner 必须完成以下真实配置；不得提交占位用户名、长期
API key 或个人访问令牌：

1. GitHub environment `nuget-org` 只允许 `main`。单人仓库允许所有者批准自己触发的 deployment，并保留
   admin bypass。environment secret `NUGET_ORG_USER` 保存 NuGet.org profile 的真实用户名；它不是
   API key。
2. 在该 NuGet.org 用户的 Trusted Publishing 页面创建 GitHub Actions policy，字段必须是：
   repository owner `Moch1ce`、repository `Snaploom`、workflow file `release-promote-sdk.yml`、
   environment `nuget-org`。首次发布可以使用 pending policy，但成功后必须确认它已绑定
   `Snaploom.Capture`。
3. 当前由仓库所有者同时承担 NuGet.org package owner、GitHub environment reviewer 和实际发布人；
   OIDC policy 或 username 变化时先更新受控配置，再调度工作流。

工作流只给 `publish-nuget` job `id-token: write`。固定到完整 commit 的 `NuGet/login` action 使用 GitHub
OIDC 换取短期 API key；仓库及 environment 都不保存长期 NuGet API key。参考
[NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) 和
[GitHub OIDC 权限说明](https://docs.github.com/actions/security-for-github-actions/security-hardening-your-deployments/about-security-hardening-with-openid-connect)。

## 调度与验证顺序

从 `main` 手动运行 `Promote stable SDK packages`，输入稳定 Release 的 `version`、完整 40 位
`commit` 和 `public_release_id`。只有以下顺序存在：

1. 只读 job 验证 tag 精确指向该 commit、commit 可从 `main` 到达，并从 GitHub 重新下载完整资产集；
   Release 必须是 `draft=false`、`prerelease=false`、`immutable=true`，manifest 必须是同一 commit 的
   `stable-release` 模式，GitHub asset size/digest 与重新下载的 bytes 必须一致。
2. `nuget-org` environment 批准后，publish job 再次从公开 Release 下载 `.nupkg`、`.snupkg` 及各自
   checksum sidecar。若 registry 已有同 ID/version，必须验证 NuGet.org repository signature，并在排除
   repository signature entry 后逐项比较 ZIP 内容；不同则 hard fail，相同才进入幂等恢复。新版本仅 push
   主包一次；symbol package 始终用已复验的 `.snupkg` 显式提交，修复主包成功而 symbols 未提交的部分失败。
   两条命令都没有 `--skip-duplicate`，不会覆盖或静默忽略冲突。
3. NuGet.org 可见该版本后，在干净的临时 global-packages 目录中只配置官方 v3 source。矩阵覆盖
   Windows `win-x64`、macOS `osx-arm64` 与固定 .NET `8.0.423`/`10.0.302`，分别执行 restore、
   locked restore、build/run、trim 和 NativeAOT。每个 normal/trim/AOT consumer 都有独立 `global.json`
   （`rollForward=disable`）和与 publish 属性完全相同的 locked restore；`dotnet publish` 必须使用
   `--no-restore`。
4. 独立 macOS job 从公开 Release 重新下载 XCFramework，使用 `swift package compute-checksum` 对照根
   `Package.swift` 的精确 URL/checksum，再创建只依赖公开 GitHub 仓库 exact SemVer 的空白 consumer。
   `Package.resolved` 必须只有 Snaploom 且 revision 等于输入 commit，然后执行 `swift build` 和
   `swift test`。
5. 上述全部成功后才上传只读 promotion evidence artifact。

每个执行 job 都把 `ImageOS`、`ImageVersion`、runner architecture 与实际 Node/Python/.NET 或
Xcode/Swift CLI 版本写入 Actions step summary；OIDC job 还在独立目录用 `global.json` 锁定并断言
`.NET SDK 8.0.423`，避免 runner 预装的更高 SDK 接管 registry 命令。

## 失败与不可重放边界

- GitHub stable Release 必须先成功；NuGet 失败不会回滚、改写或删除 GitHub Release。修复外部 OIDC、
  environment 或网络问题后可以重试相同 Release bytes：主包不存在时首次 push；已存在时只有 repository
  signature 有效且去签名内容完全相同才视为幂等成功，否则触发安全响应。
- 主包成功但 symbols 或后续 consumer 失败时，重跑不会重推主包；它会再次显式提交同一已验证
  `.snupkg`，然后重新运行公开 consumer。不得使用 `--skip-duplicate` 伪造成功。若相同 symbols 的提交或
  consumer 仍失败，保留失败 run，停止推广，并按发布策略调查、unlist 或发布新的 patch 版本。
- SwiftPM 没有第二个 registry。公开 tag、根 `Package.swift` 和同版本 Release XCFramework 是唯一消费
  路径；tag 与 Release asset 都不得更新或替换。
- 任何 version/commit/release ID、asset digest、checksum、package source、resolved revision 或运行结果
  不一致都 fail closed，不生成成功证据。
