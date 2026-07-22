# Capture SDK 集成与再分发风险说明

状态：发布前风险披露；未经外部法律复核，不得宣称为法务批准或法律意见。

Snaploom Capture SDK 按 `Apache-2.0` 提供，可被开源或闭源宿主使用。交互式截图、标注、平台捕获与用户界面位于独立的 Snaploom Capture Host；Host 按 `GPL-3.0-or-later` 提供，不因通过 SDK 调用而被重新授权。

SDK 与 Host 是不同二进制和不同下载资产，只通过公开、版本化的本地服务协议交换截图请求、取消、稳定错误和最终 PNG。SDK package 不捆绑、链接、自动下载或静默安装 Host。

## 只使用 SDK

如果应用只分发和链接 Apache-2.0 SDK，而由用户单独获取 Host，请按 SDK package 内的 Apache-2.0、NOTICE 和第三方通知履行对 SDK 的义务。`HOST_NOT_FOUND` 表示 Host 尚未安装；SDK 不会自动下载它。

## 再分发 Capture Host

如果应用、installer、设备或下载页面同时再分发 Capture Host，再分发者必须独立评估并履行 GPLv3 义务，包括附带许可证、保留通知，以及为所分发的精确 Host binary 提供机器可读的 Corresponding Source。如果修改 Host，修改后的 Host 也属于需要复核的 GPL 分发物。

不得使用 Apache SDK 的许可声明代替 Host 的 GPL 义务，也不得将 Host binary 隐藏在 NuGet、XCFramework 或 C SDK archive 中。

## 紧密组合和修改版

进程分离和 IPC 本身不构成对任何组合的自动法律保证。如果集成方把 Host 改成进程内库/插件，共享内部对象、函数或可变内存，扩展协议为专用的细粒度控制面，或以其他方式形成紧密组合，必须重新获得针对实际事实的法律建议。这些方式不属于官方 SDK 支持面。

## 品牌、签名和担保

软件许可证不自动授予 Snaploom 名称、Logo 或官方签名身份的使用权。第三方构建应当清楚区分于官方发布。官方签名只证明官方产物的来源，不阻止用户按许可证构建和运行修改版。

Apache-2.0 和 GPL-3.0-or-later 都含无担保条款。本说明不构成项目方对某个专有集成、司法辖区、再分发方式或商业计划的法律意见或补偿承诺。集成方对自己的实际使用和分发方式负责，并应在需要时寻求合格律师的意见。

## 官方资料

- [Capture SDK 与 IPC 设计](../research/capture-sdk-c-abi-ipc.md)
- [SDK 包与封装设计](../research/capture-sdk-wrappers-distribution.md)
- [GNU GPLv3 正文](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU GPL FAQ：GPL 程序与专有系统](https://www.gnu.org/licenses/gpl-faq.html#GPLInProprietarySystem)
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
