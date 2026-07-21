# Snaploom Capture SDK

`sdk/**` 是稳定 IPC schema、C/C++ 接口与官方语言封装的 Apache-2.0 许可边界。

该目录拥有独立 `Cargo.toml`、`Cargo.lock` 与依赖许可策略，不与 GPL 产品共享 Cargo workspace。

迁移实现必须保持以下约束：

- SDK 只依赖 Apache-2.0 或更宽松且经审计的代码，不得依赖或复制 GPL 截图实现。
- SDK 与 GPL Capture Host 通过版本化本地 IPC 通信，二者是独立二进制与独立发布资产。
- SDK 的公开 ABI、错误码与 wire schema 变更必须有兼容性测试。

详细边界见 [`docs/adr/0001-open-source-license-boundaries.md`](../docs/adr/0001-open-source-license-boundaries.md)。

## v1 Capture SDK 基础

- `protocol/`：使用 vendored `protoc` 生成的 protobuf v1 schema、12-byte 有界帧和 PNG 聚合校验。
- `client/`：每 client 串行 callback executor、恰好一次终态、幂等取消、有界交互超时，以及双向 peer-authenticated named pipe/UDS transport。
- `c-abi/` 与 `include/snaploom_capture.h`：固定结构布局和恰好 7 个导出符号的 C ABI。
- `testkit/fake-host/`：从 C 入口贯通 callback/free 的可脚本化 Apache-2.0 测试替身。

在仓库根目录执行 `pnpm run check:abi` 会构建 release 动态库并将导出表与
`c-abi/abi-symbols-v1.txt` 的冻结 allowlist 比较。
