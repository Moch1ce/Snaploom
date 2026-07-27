# Snaploom Capture SDK

`sdk/**` 是稳定 IPC schema、C/C++ 接口与官方语言封装的 Apache-2.0 许可边界。

迁移实现必须保持以下约束：

- SDK 只依赖 Apache-2.0 或更宽松且经审计的代码，不得依赖或复制 GPL 截图实现。
- SDK 与 GPL Capture Host 通过版本化本地 IPC 通信，二者是独立二进制与独立发布资产。
- SDK 的公开 ABI、错误码与 wire schema 变更必须有兼容性测试。

详细边界见 [`docs/adr/0001-open-source-license-boundaries.md`](../docs/adr/0001-open-source-license-boundaries.md)。
