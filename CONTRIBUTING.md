# 为 Snaploom 做贡献

感谢参与 Snaploom。提交贡献即表示你同意以下规则。

## 许可证与来源

- 贡献采用 inbound = outbound：修改按目标文件或目录已经声明的许可证授权。默认产品树为 `GPL-3.0-or-later`，`sdk/**` 与 `examples/sdk/**` 为 `Apache-2.0`。
- 不得提交来源不明、无许可证、限制商业使用或与目标目录许可证不兼容的代码、生成物、字体、图标、媒体或模型。
- PR 必须披露复制、改写或生成的第三方内容，并附来源、版本和许可证。
- 跨 GPL/Apache 边界复制或移动代码不会自动改变许可证；必须在 PR 中说明并由维护者复核。
- 项目名称、Logo、官方发布签名和商标不随代码许可证自动授权。

## DCO 1.1

每个 commit 都必须包含 `Signed-off-by`，表示提交者接受 [Developer Certificate of Origin 1.1](https://developercertificate.org/) 并确认有权按对应许可证提交贡献。可用以下命令签署：

```shell
git commit -s
```

Snaploom 当前不要求 CLA；DCO 不会额外授予项目方闭源重授权权利。

## 开发与验证

1. 从最新目标分支创建短生命周期分支。
2. 为可观察行为先添加或更新测试，再实现变更。
3. 运行受影响工作区的格式、静态检查、测试和 `reuse lint`。
4. 保持一次提交只完成一个可独立验收的任务，并在 PR 中列出实际运行过的命令。

UI 变更必须遵循 [`docs/ui/screenshot-ui.md`](docs/ui/screenshot-ui.md)，领域术语与架构决策以 [`CONTEXT.md`](CONTEXT.md) 和 [`docs/adr/`](docs/adr/) 为准。
