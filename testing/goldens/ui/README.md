# 截图浮层 UI 金图

这里的 PNG 来自实际生产入口 `/src/main.ts`，覆盖固定浅色、品牌令牌、尺寸标签、九按钮工具栏、工具设置浮层、指针捕获和 IME 组合输入期间的 textarea 几何。

金图在 Ubuntu 24.04 x86_64、固定摘要的 Playwright 测试镜像和测试专用 Inter/Noto Sans SC 字体下生成。生产代码仍使用系统无衬线字体；受控字体只消除跨运行器的截图噪声。

更新命令：`cargo run --locked --bin xtask -- goldens-update`。CI 只比较已提交金图。
