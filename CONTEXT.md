# Snaploom

Snaploom 是面向终端用户和第三方宿主应用的跨平台交互式截图产品。本词汇表统一应用、开发者分发与商业授权中的领域语言。

## Language

**Capture SDK**:
采用宽松开源许可证、面向 Windows 与 macOS 宿主应用的开发者分发；负责启动交互式截图，并把最终图片结果返回给宿主。
_Avoid_: 插件、截图 DLL

**Capture Host**:
由 Capture SDK 启动的独立截图进程；拥有截图权限和交互界面，并通过本地 IPC 将最终图片结果返回宿主。
_Avoid_: 嵌入式截图界面、SDK 进程内界面

**Capture Session**:
一次完整的交互式截图生命周期，从应用或 Capture SDK 发起，最终进入完成、取消或失败状态；应用模式与 SDK 模式共享相同的选区和标注能力。
_Avoid_: 截图任务、截图窗口

**Capture Result**:
用户确认 Capture Session 后得到的最终合成 PNG，包含选区内所有标注；同一份图片返回宿主，并在默认配置下写入系统剪贴板。
_Avoid_: 原始截图、屏幕原图

**闭源宿主**:
不公开自身产品源码但使用 Capture SDK 的第三方应用；它可以免费集成 Capture SDK，无须购买商业许可证或公开宿主源码。
_Avoid_: 付费商业集成
