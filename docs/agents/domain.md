# 领域文档

工程技能探索代码库时，应按以下规则读取和使用本仓库的领域文档。

## 探索前读取

- 读取仓库根目录的 `CONTEXT.md`。
- 如果根目录存在 `CONTEXT-MAP.md`，则按其中的指引读取与当前主题相关的各个 `CONTEXT.md`。
- 读取 `docs/adr/` 中与即将处理区域相关的 ADR。
- 对于 multi-context 仓库，还应检查 `src/<context>/docs/adr/` 中特定上下文的决策。

如果这些文件不存在，静默继续，不要提前建议创建。`/domain-modeling` 技能会在术语或架构决策真正明确时按需创建它们。

## 文件结构

本仓库采用 single-context 布局：

```text
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-event-sourced-orders.md
│   └── 0002-postgres-for-write-model.md
└── src/
```

如果未来改为 multi-context，根目录应使用 `CONTEXT-MAP.md` 指向各上下文的 `CONTEXT.md`：

```text
/
├── CONTEXT-MAP.md
├── docs/adr/
└── src/
    ├── ordering/
    │   ├── CONTEXT.md
    │   └── docs/adr/
    └── billing/
        ├── CONTEXT.md
        └── docs/adr/
```

## 使用词汇表中的术语

当输出中出现领域概念时，例如 Issue 标题、重构提案、假设或测试名称，应使用 `CONTEXT.md` 中定义的术语，不要改用词汇表明确排除的同义词。

如果需要的概念尚未收录，应判断这是项目并未采用的表达，还是领域模型确实存在缺口；对于后者，将其记录给 `/domain-modeling` 处理。

## 标明与 ADR 的冲突

如果输出与现有 ADR 冲突，应明确指出，不得静默覆盖：

> 与 ADR-0007（事件溯源订单）冲突，但值得重新讨论，因为……
