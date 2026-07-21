# Snaploom Capture C/C++ SDK

本归档提供 Apache-2.0 的共享 C SDK、权威 C17 header、CMake targets 与
C++17 header-only RAII。`Snaploom::Capture` 是 C shared target，
`Snaploom::CaptureCpp` 仅封装该 target，不增加 C++ binary ABI。

Capture Host 是独立的 GPL-3.0-or-later 安装资产，不包含在本归档中，SDK
也不会下载或从 `PATH` 搜索 Host。`HOST_NOT_FOUND` 表示用户需先安装
Snaploom Capture Host，或在 client config 中提供经过验证的绝对路径。

```cmake
find_package(SnaploomCapture CONFIG REQUIRED)
target_link_libraries(my_consumer PRIVATE Snaploom::CaptureCpp)
```

C API 的 `capture_start == OK` 只表示异步请求已被接受；终态由恰好一次
callback 报告。completion 及 PNG 必须且只能通过
`snaploom_capture_completion_free_v1` 释放。C++ wrapper 在 callback 内复制
PNG，随后立即完成该释放。取消是 intent，调用者仍需等待同一终态。

默认会把最终 PNG 写入剪贴板；只有显式设置
`SNAPLOOM_CAPTURE_FLAG_DISABLE_CLIPBOARD` 才关闭。SDK 不自动重试 Busy、
Host crash 或 transport failure，也不创建临时截图文件。
