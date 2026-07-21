# Snaploom.Capture for .NET 8

`Snaploom.Capture` 是 Apache-2.0 的官方 C# wrapper。单一 NuGet 包携带
`win-x64` 与 `osx-arm64` 的共享 C SDK；Capture Host 是独立安装和分发的
GPL-3.0-or-later 程序，本包不包含、下载或从 `PATH` 搜索 Host。

```csharp
using Snaploom.Capture;

using var client = new CaptureClient();
using var cancellation = new CancellationTokenSource();
CaptureResult result = await client.CaptureAsync(
    new CaptureOptions { DisableClipboard = false },
    cancellation.Token);
await File.WriteAllBytesAsync("selection.png", result.Png.ToArray());
```

支持 .NET 8+，首版只承诺 Windows 10 22H2/Windows 11 x64 与 macOS 14+
Apple Silicon。其他 RID 在构建期（显式 `RuntimeIdentifier`）或首次构造 client
时以 `SNAPLOOM001` 明确失败，不会加载替代库。

默认把最终 PNG 写入系统剪贴板；`DisableClipboard = true` 可关闭。Busy、Host
crash、transport failure 均作为单次请求失败返回，wrapper 不排队、不自动重试。
`CancellationToken` 只发送 cancel intent；返回的 Task 必须等 native terminal
callback 后才会取消。用户在 Host 中取消时抛 `CaptureCanceledException`，调用方
token 取消且 native 确认后抛 `OperationCanceledException`。

`CaptureClient.Dispose()` 幂等并可能阻塞，直到已接受的 callback 返回。请在应用
受控线程主动释放，不依赖终结器。callback 内会先复制 PNG 到托管 `byte[]`，随后
恰好一次调用 native completion free；结果不再引用 native 内存。

`CaptureException.ErrorCode` 保留稳定原始数值，未来未知值也可诊断；异常消息不
包含截图、窗口标题、完整路径或 Host 内部堆栈。`RuntimeVersion` 暴露实际 ABI 与
native semver；wrapper 接受 ABI 1 且 package major 相同的 native 版本。

重新分发独立 Capture Host 时，集成方必须单独履行其 GPL 义务。本 SDK 的进程边界
不构成法律意见或自动豁免。
