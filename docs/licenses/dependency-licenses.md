# Snaploom 依赖许可证审计

- 审计日期：2026-07-14
- 审计范围：`Snaploom.sln` 当前的直接依赖与传递依赖
- 目标：确认依赖允许 Snaploom 未来以闭源商业软件形式分发

## 结论

当前运行时依赖采用 MIT 或 BSD 3-Clause 宽松许可证；测试依赖采用 MIT 或 Apache-2.0 许可证。未发现 GPL、AGPL、LGPL 或其他要求公开 Snaploom 源码的强 Copyleft 依赖。

这些许可证允许闭源商业使用与再分发，但发布安装包时仍需保留相应版权声明和许可证文本。项目尚未添加开源 `LICENSE` 文件，避免误示 Snaploom 自身已按开源许可证授权。

## 运行时依赖

| 依赖 | 当前版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| Avalonia、Avalonia.Desktop、Avalonia.Skia、Avalonia.Themes.Fluent | 12.1.0 | MIT | 跨平台桌面 UI、桌面后端与渲染 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM 命令生成 |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD 3-Clause | Windows ANGLE 原生运行库 |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia 构建支持 |
| HarfBuzzSharp | 8.3.1.3 | MIT | 文本塑形 |
| MicroCom.Runtime | 0.11.6 | MIT | COM 互操作支持 |
| SkiaSharp | 3.119.4 | MIT | 2D 图形渲染 |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux/FreeDesktop 传递支持；当前产品目标不含 Linux |

Avalonia 的其他传递包（FreeDesktop、HarfBuzz、Native、Remote.Protocol、Win32、X11）均为 12.1.0，包清单声明为 MIT。

## 仅测试与构建使用的依赖

| 依赖 | 当前版本 | 许可证 |
| --- | --- | --- |
| xunit.v3 | 3.2.2 | Apache-2.0 |
| Microsoft.Testing.Platform 及 Microsoft 测试宿主组件 | 1.9.1 | MIT |

## 复核方法

版本树可通过以下命令重新生成：

```bash
dotnet list Snaploom.sln package --include-transitive
```

许可证以还原后 NuGet 包内的 `.nuspec` 与许可证文件为准。每次新增或升级运行时依赖时，都应更新本文件；发布流程还需把运行时依赖的许可证文本汇总到安装包的第三方声明中。
