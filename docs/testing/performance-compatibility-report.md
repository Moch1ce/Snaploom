# Snaploom v1 性能与兼容性报告

本报告随所在 Git commit 固定，验收目标来自 #18。自动性能门槛已有可重复执行的工具；兼容性矩阵只把实际完成的组合标记为通过。

> 下方 2026-07-16 数据来自迁移前 .NET/Avalonia 实现，只保留为 PERF-02 回退参考，不是当前
> Rust/Tauri 实现的发布证据。当前 Release qualification 只使用 GitHub-hosted `windows-2025` 与
> `macos-15`，验证自动平台合同并精确绑定 release version/commit；它明确不证明交互桌面或真机
> PERF-01。`tools/release/verify-machine-evidence.mjs` 仍可校验自愿收集的人工证据，但该人工矩阵不是
> 签名 RC 或 stable publish 的前置条件。

## 指标口径

- `1 MB = 1,000,000 bytes`。
- macOS 空闲与循环原生内存使用 Activity Monitor 同口径的 `physical footprint`；Windows 空闲使用 working set，循环稳定性使用 private memory。
- 快捷键延迟从系统全局快捷键回调开始，到截图浮层完成平台配置并可交互为止；至少采集 30 次，按 nearest-rank 计算 P50、P95 和最大值。
- 循环内存至少采集 20 次成功复制或保存；取消和直接关闭不计数。在每次浮层释放截图资源后强制完成一次诊断性 GC，比较最后两个连续 5 次窗口的中位数，尾段增长不超过基线的 1%。
- 4K 渲染基准使用 3840×2160 BGRA/sRGB 合成帧。对象拖动、马赛克持续绘制、移动和撤销/重做的 P95 上限为 16.667 ms；PNG 最终合成并写入临时文件的 P95 上限为 1 秒。
- 选区状态更新不包含 Avalonia 实际画布合成，只用于排除核心状态机瓶颈；4K 显示器上的真实选区移动帧率必须由真机矩阵确认。
- 性能基准必须独占运行，不能与构建或测试并行争抢 CPU。

## 自动复现

先运行全部测试和独占 4K 基准：

```bash
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" test Snaploom.sln -c Release
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" run \
  --project tools/Snaploom.PerformanceHarness/Snaploom.PerformanceHarness.csproj \
  -c Release -- \
  --output artifacts/performance/rendering-report.json
```

桌面真机采样只在显式设置环境变量时启用，不改变普通用户运行路径：

```bash
SNAPLOOM_PERFORMANCE_REPORT="$PWD/artifacts/performance/desktop-probe.json" \
  /path/to/Snaploom.App
```

应用空闲至少 10 秒后，真实触发默认全局快捷键至少 30 次，并在每次浮层可交互后成功复制或保存；取消不会增加循环计数。退出应用后评估原始报告：

```bash
DOTNET_ROOT="$HOME/.dotnet" "$HOME/.dotnet/dotnet" run \
  --project tools/Snaploom.PerformanceHarness/Snaploom.PerformanceHarness.csproj \
  -c Release -- \
  --evaluate-desktop artifacts/performance/desktop-probe.json \
  --output artifacts/performance/desktop-evaluation.json
```

报告只包含数值、操作系统与运行时信息，不包含机器名、用户名、个人路径、截图像素或标注文本。

## 已验证基线

验证日期：2026-07-16。

| 项目 | 基线 |
|---|---|
| 设备 | Mac mini（Mac16,10），Apple M4，24 GB |
| 系统 | macOS 26.5（25F71），arm64 |
| 显示 | 单显示器，1920×1080，100% 逻辑缩放，75 Hz |
| 运行时 | .NET 10.0.9，self-contained osx-arm64 |
| 包 | ad hoc `snaploom-0.1.0-macos-arm64.dmg`，45,902,044 bytes |
| SHA256 | `b3d8acbe48c80fd356cd9327d800e7b149b31fb6c128867479f4b6e4ab582e71` |

自动测试共 149 项，全部通过。4K 原始结果见 [`evidence/2026-07-16-macos-m4/rendering-report.json`](evidence/2026-07-16-macos-m4/rendering-report.json)；桌面端的 30+30 原始样本见 [`evidence/2026-07-16-macos-m4/desktop-probe.json`](evidence/2026-07-16-macos-m4/desktop-probe.json)，聚合判定见 [`evidence/2026-07-16-macos-m4/desktop-evaluation.json`](evidence/2026-07-16-macos-m4/desktop-evaluation.json)。

| 指标 | 结果 | 门槛 | 结论 |
|---|---:|---:|---|
| 空闲 physical footprint | 67.99 MB | ≤ 100 MB | 通过 |
| 快捷键到可交互 P50 / P95 / max | 113.28 / 140.16 / 316.85 ms | P95 ≤ 150 ms | 通过 |
| 4K 选区状态更新 P95 | 0 ms | CPU 代理项 | 通过；真机合成待验证 |
| 4K 对象拖动并光栅化 P95 | 14.5605 ms | ≤ 16.667 ms | 通过 |
| 4K 马赛克持续绘制缓存 P95 | 0.8932 ms | ≤ 16.667 ms | 通过 |
| 4K 马赛克移动缓存 P95 | 13.5522 ms | ≤ 16.667 ms | 通过 |
| 4K 马赛克撤销/重做缓存 P95 | 10.2312 ms | ≤ 16.667 ms | 通过 |
| 4K PNG 合成并写盘 P95 | 202.8086 ms | ≤ 1,000 ms | 通过 |
| 30 次完整截图托管堆尾段增长 | 0%（-1,688 bytes） | ≤ 1% | 通过 |
| 30 次完整截图 physical footprint 尾段增长 | 0.09%（245,760 bytes） | ≤ 1% | 通过 |

首次截图存在 316.85 ms 冷启动尖峰，但 30 次样本的 P95 为 140.16 ms，符合已确认的 P95 门槛。当前自动性能指标均通过，因此不触发纯 Rust 方案复评；实际 4K 画布帧率仍按下方矩阵保留为待真机项。

## 兼容性矩阵

| 平台与显示组合 | 状态 | 证据或剩余操作 |
|---|---|---|
| macOS 26.5 / Apple M4 / 单屏 1080p / 100% | 性能通过 | 本报告的真实全局快捷键与 30 次成功复制循环结果 |
| macOS 14 / Apple Silicon | 待真机 | 按 [`macos-t02-validation.md`](macos-t02-validation.md) 完整执行 |
| macOS 14+ / 双屏、4K、5K Retina、热插拔 | 待真机 | 覆盖当前显示器选择、睡眠唤醒和热插拔 |
| macOS 14+ / 混合 DPI | 待真机 | 覆盖逻辑到物理坐标、窗口吸附与 PNG 尺寸 |
| Windows 10 22H2 x64 / 100%–200% | 待真机 | 按 [`windows-t03-validation.md`](windows-t03-validation.md) 完整执行 |
| Windows 11 x64 / 单屏、双屏、1080p、4K、混合 DPI | 待真机 | 覆盖热插拔、睡眠唤醒和重复启动 |
| 快捷键冲突、权限缺失、取消保存 | 待矩阵复核 | 自动测试已覆盖状态与错误路径，仍需各目标系统真机复核 |

因此，#18 的自动性能工具和当前 macOS 基线已经完成，但跨平台、跨显示器的兼容性验收尚未全部完成。
上述“待真机”项继续作为非阻塞人工建议跟踪；缺少它们不阻止正式发布，但不得据此声称已经完成目标系统
真机兼容性或真实桌面 PERF-01 资格认证。
