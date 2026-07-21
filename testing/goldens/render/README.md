# 最终 PNG 像素金图

这里的 PNG 固化 QA-01、QA-02 和 IMG-01 的真实浏览器 Canvas 输出：矩形、箭头、文字、马赛克、对象叠加，以及 100%/125%/150%/175%/200% DPI。

CI 使用锁定版本的 Playwright 与 Chromium headless shell，只比较已提交金图，不会自动更新。每个 PNG 旁的 JSON sidecar 记录生成器版本、浏览器 revision、夹具哈希、物理尺寸和契约 ID。

只有在确认渲染变化符合预期后，才可显式运行：

```bash
pnpm run goldens:update
```

基线应在与 CI 相同的 Ubuntu 24.04/固定 Chromium 环境中生成，并在提交前再次运行 `pnpm --filter @snaploom/overlay-editor run test:visual` 验证零像素差。
