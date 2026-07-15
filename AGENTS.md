## Agent skills

### Issue tracker

Issue 和 PRD 使用 GitHub Issues 管理。详见 `docs/agents/issue-tracker.md`。

### Triage labels

使用默认的五类 triage 标签。详见 `docs/agents/triage-labels.md`。

### Domain docs

使用 single-context 领域文档布局。详见 `docs/agents/domain.md`。

### Screenshot UI

截图浮层及后续编辑工具必须遵循 `docs/ui/screenshot-ui.md`，并复用项目内统一的主题、工具栏和图标组件。

### Git workflow

- 每完成一项用户任务，必须创建对应的 Git commit，并立即将当前分支推送到 `origin` 一次。
- 不得把多个已经独立完成的任务积压到后续任务再统一推送。
- 如果提交或推送受到权限、网络、冲突或远端状态阻止，必须明确报告原因和仍未完成的 Git 操作。
