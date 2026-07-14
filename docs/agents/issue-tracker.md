# Issue tracker：GitHub

本仓库的 Issue 和 PRD 使用 GitHub Issues 管理。所有操作均使用 `gh` CLI。

## 约定

- **创建 Issue**：`gh issue create --title "..." --body "..."`。多行正文使用 heredoc。
- **读取 Issue**：`gh issue view <number> --comments`，同时获取标签，并根据需要使用 `jq` 过滤评论。
- **列出 Issue**：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，按需添加 `--label` 和 `--state` 过滤条件。
- **评论 Issue**：`gh issue comment <number> --body "..."`
- **添加或移除标签**：`gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **关闭 Issue**：`gh issue close <number> --comment "..."`

通过 `git remote -v` 推断仓库；在已配置远程仓库的本地 clone 中运行时，`gh` 会自动完成推断。

## 将 Pull Request 作为 triage 入口

**PRs as a request surface: no.**

设置为 `yes` 时，PR 将通过与 Issue 相同的标签和状态流转，并使用对应的 `gh pr` 命令：

- **读取 PR**：`gh pr view <number> --comments`，使用 `gh pr diff <number>` 获取差异。
- **列出待 triage 的外部 PR**：运行 `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`，只保留 `authorAssociation` 为 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE` 的 PR。
- **评论、标记或关闭 PR**：使用 `gh pr comment`、`gh pr edit --add-label`、`gh pr edit --remove-label` 和 `gh pr close`。

GitHub 的 Issue 和 PR 共用编号空间，因此 `#42` 可能是任意一种类型。先运行 `gh pr view 42`，失败后再运行 `gh issue view 42`。

## 技能要求“发布到 issue tracker”时

创建一个 GitHub Issue。

## 技能要求“获取相关 ticket”时

运行 `gh issue view <number> --comments`。

## Wayfinding 操作

供 `/wayfinder` 使用。一个 map 对应一个主 Issue，ticket 则对应其子 Issue。

- **Map**：使用标签 `wayfinder:map` 的单个 Issue，正文包含 Notes、Decisions-so-far 和 Fog。通过 `gh issue create --label wayfinder:map` 创建。
- **子 ticket**：使用 GitHub sub-issue API 将 Issue 关联到 map。若仓库未启用 sub-issue，则在 map 正文中维护任务列表，并在子 Issue 顶部添加 `Part of #<map>`。标签采用 `wayfinder:<type>`，其中类型为 `research`、`prototype`、`grilling` 或 `task`。ticket 被领取后，分配给负责推进的开发者。
- **阻塞关系**：优先使用 GitHub 原生 Issue dependencies。通过 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>` 添加依赖；`<blocker-db-id>` 必须是阻塞 Issue 的数字数据库 ID，可通过 `gh api repos/<owner>/<repo>/issues/<n> --jq .id` 获取。若原生依赖不可用，则在子 Issue 顶部添加 `Blocked by: #<n>, #<n>`。所有阻塞 Issue 关闭后，ticket 才算解除阻塞。
- **查询可执行 ticket**：按 map 顺序列出未关闭的子 Issue，排除仍有未关闭阻塞项或已有 assignee 的 Issue，选择剩余结果中的第一个。
- **领取**：运行 `gh issue edit <n> --add-assignee @me`。这是会话中的首次写操作。
- **解决**：运行 `gh issue comment <n> --body "<answer>"`，随后运行 `gh issue close <n>`，最后在 map 的 Decisions-so-far 中追加上下文指针和链接。
