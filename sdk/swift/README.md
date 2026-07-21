# Snaploom Capture for Swift

`SnaploomCapture` 是 macOS 14+、Apple Silicon 专用的 Swift 5.9+ 源码封装。根目录
`Package.swift` 通过 HTTPS Release ZIP 和 SHA-256 checksum 消费仅含 macOS arm64 的
`CSnaploomCapture.xcframework`。

```swift
import SnaploomCapture

let client = try CaptureClient()
defer { try? client.close() }

let result = try await client.capture(
    options: CaptureOptions(disableClipboard: false)
)
try result.pngData.write(to: outputURL, options: .atomic)
```

调用方取消 `Task` 后，封装只发送 native cancel intent，并等待 native callback 确认终态；
因此 `CancellationError` 表示 native 已确认取消。Capture Host 中的 Esc/右键取消会返回
`CaptureError.canceledByUser`。`CaptureError.failure` 保留稳定错误码与 retryable 标志，
`CaptureErrorCode.busy` 不会自动排队或重试。

Capture Host 是独立安装的 GPL 应用，不在 Swift package/XCFramework 内。若未安装，调用会以
`CaptureErrorCode.hostNotFound` 失败；包装层不会下载 Host，也不会从网络或临时目录寻找替代库。
`close()` 幂等，但可能等待已接受的 callback 返回，应在应用受控的非 callback executor 上调用。
