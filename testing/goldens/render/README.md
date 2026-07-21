# 最终 PNG 像素金图

这里的 PNG 固化 QA-01、QA-02 和 IMG-01 的真实浏览器 Canvas 输出：矩形、箭头、文字、马赛克、对象叠加，以及 100%/125%/150%/175%/200% DPI。DPI 组覆盖左上到右下、右下到左上、右上到左下、左下到右上的选区方向。

CI 使用固定摘要的 Playwright 测试镜像与 Linux x86_64 Chromium headless shell，只比较已提交金图，不会自动更新。每个 PNG 旁的 JSON sidecar 记录生成器版本、浏览器 revision、实际二进制版本、操作系统、架构、夹具哈希、物理尺寸和契约 ID。

只有在确认渲染变化符合预期后，才可显式运行：

```bash
cargo run --locked --bin xtask -- goldens-update
```

`pnpm run goldens:update` 是同一入口的便捷包装。基线应在与 CI 相同的 Ubuntu 24.04/固定 Chromium 环境中生成，并在提交前再次运行 `pnpm --filter @snaploom/overlay-editor run test:visual` 验证零像素差。
