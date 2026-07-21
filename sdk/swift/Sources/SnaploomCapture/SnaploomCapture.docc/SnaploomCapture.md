# ``SnaploomCapture``

在 macOS 14+ Apple Silicon 应用中，以 Swift concurrency 调用独立 Snaploom Capture Host。

## Overview

``CaptureClient/capture(options:)`` 使用 checked continuation 等待 native 终态，并在 callback
内把 PNG 拷贝为 Swift 拥有的 `Data`。调用方应显式调用 ``CaptureClient/close()``。
