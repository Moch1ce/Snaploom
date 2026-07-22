# 外部法律复核后的稳定 Release 公开流程

`.github/workflows/release-stable.yml` 是 Snaploom 将已复核 draft 转换为稳定 GitHub Release 的唯一入口。
它不构建、不签名、不上传、不删除或替换资产，只复验 Issue #33 的外部法律记录后执行一次
`draft=false` 转换。

## 前置条件

1. 签名法律 RC workflow 已为同一 `vX.Y.Z`/commit 创建完整的不可见 draft，并从 GitHub 重新下载
   复验全部资产。
2. tag 指向 `main` 可达的精确 commit；仓库已启用 Immutable Releases，`v*` ruleset 禁止更新和删除。
3. Issue #33 已由维护者在收到真实外部法律复核后关闭。
4. 外部复核人使用可访问 draft 的 GitHub collaborator 身份，在 Issue #33 留下一个版本化记录评论；
   该 login 存在于仓库变量 `LEGAL_REVIEWER_LOGINS`。GitHub 只向具备 push access 的用户显示 draft，
   因此工作流要求 `User`、`author_association=COLLABORATOR`、`permission=write` 且
   `role_name=write`。owner、admin、映射为 write 的 maintain、自定义角色、Bot 及无权限身份都不能批准发布。
5. `stable-release` environment 只允许 `main`，required reviewer 不能是 workflow 触发人，并关闭
   self/admin bypass。

## 外部复核记录

评论必须只包含一个 `snaploom-legal-review:v1` marker 和一个 JSON code fence。字段由真实外部复核人
填写；Agent、CI 和维护者不得代填或把有条件接受解释成发布批准。

```html
<!-- snaploom-legal-review:v1 -->
```

```json
{
  "schemaVersion": 1,
  "kind": "snaploom-external-legal-review",
  "legalReviewIssue": 33,
  "reviewer": {
    "name": "<真实姓名>",
    "organizationOrLicenseIdentity": "<机构或执业身份>",
    "githubLogin": "<allowlisted-login>"
  },
  "reviewDate": "YYYY-MM-DD",
  "jurisdictionAndLimitations": "<司法辖区与限定>",
  "conclusion": "accepted",
  "requiredChanges": [],
  "approvedPublicRiskLanguage": "The Apache Capture SDK communicates with a separately distributed GPL-3.0-or-later Capture Host; IPC does not automatically eliminate GPL compliance obligations.",
  "opinionDocument": {
    "sha256": "<64 位小写 SHA-256>",
    "controlledLocation": "<受控存档位置>"
  },
  "signature": "<真实签署记录>",
  "release": {
    "id": 0,
    "url": "<draft URL>",
    "draft": true,
    "tag": "vX.Y.Z",
    "commit": "<40 位 commit>",
    "version": "X.Y.Z",
    "manifestSha256": "<release-manifest.json SHA-256>",
    "sha256SumsSha256": "<SHA256SUMS SHA-256>",
    "payloadChecksums": {"<payload name>": "<payload SHA-256>"}
  }
}
```

`release` 对象中列出的字段必须与法律 RC 复核包 `review-subject.json` 的对应字段一致。发布验证器只接受
`accepted` 且 `requiredChanges` 为空的记录；有条件接受必须先生成满足条件的新候选并取得新的完整记录。
`approvedPublicRiskLanguage` 还必须逐字存在于已复核 draft 的公开 Release notes；公开 workflow 不会在
律师复核后改写标题或正文。评论的最终编辑时间必须晚于 draft 最后更新时间且早于 Issue #33 关闭时间；
关闭 Issue 后再编辑评论会使发布失败。

## 调度与不可变转换

从 `main` 手动运行 `Publish reviewed stable release`，输入 `version`、完整 `commit`、
`draft_release_id` 和 `review_comment_id`。工作流分为两个阶段：

1. 只读 job 解析 tag/main、Issue #33、评论身份、draft metadata，并重新下载 exact asset set；
   `verify-stable-publish.mjs` 对 manifest、checksums、签名模式和复核记录做 fail-closed 校验。仓库级
   Immutable Releases 设置由管理员预先配置；`GITHUB_TOKEN` 没有 Administration(read)，工作流不注入
   额外管理 token 读取该设置。
2. `stable-release` environment 批准后，写权限 job 从 GitHub 再次获取全部状态与 bytes，重复同一校验，
   然后只执行一次 Release PATCH。PATCH 后回读必须是 `draft=false`、`prerelease=false`、
   `published_at` 非空、`immutable=true`，且 ID、tag、commit、标题、正文和资产 metadata 与复核 draft 相同。

受保护 publish job 只把发布结果写入 Actions step summary/job output，不执行上传。它成功后，一个独立的
`contents: read` job 才上传 `snaploom-vX.Y.Z-stable-publication` 证据 artifact；该 job 无法再修改 Release。
静态门禁除验证唯一精确 PATCH 和常见短/长参数绕过外，还固定整个受保护 publish job 的 command-plan
SHA-256 及其在 write-token 环境中执行的 verifier 完整传递模块闭包 SHA-256；任何新增命令或验证器依赖
变更都必须经过显式审查并同步更新 allowlist。
任何字段、身份、时间顺序、digest、资产、tag、main 可达性、Issue 状态或 immutability 不匹配都会在公开前
失败；重复调度已公开 Release 也会因不再是 draft 而失败。

NuGet.org promotion 与公开 tag 的 SwiftPM 消费验证属于后续独立 workflow；不得在本入口中混入 registry
凭据或重新打包。
