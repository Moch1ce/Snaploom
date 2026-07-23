# `dotnet-final` 只读基线

- 冻结日期：2026-07-21
- 对应 Issue：[#35](https://github.com/moch1ce/Snaploom/issues/35)
- 目标：在删除 .NET/Avalonia/Swift 产品树前，保留可复现、已授权的最后实现。

## 验证命令

```shell
dotnet test Snaploom.sln -c Release --no-restore
CLANG_MODULE_CACHE_PATH=/private/tmp/snaploom-swift-module-cache \
  bash tests/native/macos/run-frame-orientation-test.sh
reuse lint
```

冻结要求：

- .NET Release 测试必须为 185/185 通过。
- macOS native frame orientation 测试必须通过。
- `reuse lint` 必须通过且所有旧实现文件映射到 `GPL-3.0-or-later`。
- annotated tag `dotnet-final` 必须直接指向包含本文、双许可证文本、REUSE 映射与 DCO 政策的提交。
- Issue #35 的完成评论记录最终 commit SHA、tag object/target 和逐项验证结果；远端 tag 是最终事实来源。

该标签仍只用于审计、行为对照与历史源码获取，不移动也不在标签上继续开发。2026-07-23 起，当前分支通过 [ADR 0005](../adr/0005-restore-dotnet-avalonia.md) 从该标签恢复 .NET/Avalonia，并在新的提交上继续开发。
