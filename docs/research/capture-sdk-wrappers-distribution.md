# Capture SDK 官方封装与开发者分发决策

状态：Issue [#26](https://github.com/moch1ce/Snaploom/issues/26) 的实施输入  
适用范围：Capture SDK 1.x、Windows 10 22H2 / Windows 11 x64、macOS 14+ arm64  
上位约束：[C ABI 与本地 IPC 决策](./capture-sdk-c-abi-ipc.md)、[功能等价合同](./snaploom-feature-equivalence-contract.md)、[许可证审计](./tauri-open-source-license-audit.md)、ADR 0001～0004

## 1. 最终结论

首版官方开发者分发固定为四层，不再并行维护其他绑定方案：

1. 权威底层是 Apache-2.0 的共享 C SDK 与手写 `snaploom_capture.h`。Windows x64 提供 DLL + import library，macOS arm64 提供 dylib；两者只导出 #28 固化的 7 个 `_v1` C 符号。
2. C++ 是只依赖权威 C header 的 C++17 header-only RAII 封装，提供 move-only client/operation、`std::future` 终态和显式 cancel，不增加二进制 ABI。
3. C# 以单一 `Snaploom.Capture` NuGet 包同时携带 `win-x64` 与 `osx-arm64` native assets，公共 API 是 `CaptureClient`、`Task<CaptureResult>`、`CancellationToken`、`SafeHandle` 和稳定异常类型。
4. Swift 以仓库根 `Package.swift` 的源码 wrapper + 远程 `binaryTarget` 分发。binary target 是只含 macOS arm64 C SDK、C header 与 module map 的 `CSnaploomCapture.xcframework`；公共 API 是 `CaptureClient.capture(...) async throws -> CaptureResult`。
5. 三种 wrapper 都只绑定 7 个 C ABI 符号，不读取 protobuf、不实现 Host discovery/IPC、不链接 GPL capture-core，也不把 Capture Host 打入自己的包。
6. 所有 wrapper 都在 native callback 内复制 PNG 到语言拥有的 buffer，随后在 `finally`/RAII/defer 中调用一次 `snaploom_capture_completion_free_v1`。首版不做 zero-copy，避免把 completion 的跨分配器生命周期泄漏到公共语言 API。
7. `capture_start` 的 `out_request_id` 在 C 中继续可为 `NULL`；C++ convenience API 与 `CancellationToken.None` 的 C# 常用路径不取 ID。Swift async Task 天生可被取消，因此 Swift wrapper 始终请求 ID，但不改变底层 C 参数可空合同。
8. C ABI semver、ABI major 与 IPC version 仍是独立版本轴。wrapper/package 与同一 Release 共用 semver；运行时只要求 ABI major 1，minor/patch 不精确相等不应破坏 ABI 1.x 兼容。
9. 首版明确不提供 Node.js、Java、Python、Objective-C 专用层、静态库、Windows ARM64、Intel Mac 或 Universal XCFramework。第三方可自行绑定公开 C ABI，但不属于官方兼容承诺。

## 2. 不可越过的共同合同

### 2.1 唯一 native interface

所有包最终只调用：

```text
snaploom_capture_version_v1
snaploom_capture_client_create_v1
snaploom_capture_start_v1
snaploom_capture_cancel_v1
snaploom_capture_client_destroy_v1
snaploom_capture_completion_free_v1
snaploom_capture_error_name_v1
```

wrapper 不得导入未公开 Rust symbol、平台 API、Tauri command 或 IPC symbol。C header 是 ABI layout、数值和所有权的唯一权威输入；C++、C# 与 Swift mirror 必须从该 header 的冻结测试生成/校验，不能各自解释结构。

### 2.2 类型映射

| C ABI | C | C++17 | C# `net8.0` | Swift 5.9+ |
| --- | --- | --- | --- | --- |
| `uint32_t` status/kind/error/flags | `uint32_t` + 常量宏 | `enum class : uint32_t` | `enum : uint` / `uint` flags | `struct RawRepresentable` / `UInt32` |
| `uint64_t` request/length/timeout | `uint64_t` | `std::uint64_t` | `ulong` | `UInt64` |
| opaque client pointer | incomplete struct pointer | move-only `CaptureClient` | `SafeCaptureClientHandle` | private `OpaquePointer` guarded by client state |
| UTF-8 byte view | pointer + explicit length | `std::string_view`，调用时借用 | pinned UTF-8 bytes，调用结束即解除 | `String.withUTF8` 临时借用 |
| completion pointer | callback 内取得，匹配 free | callback thunk 中 RAII guard | unmanaged callback 中 `try/finally` | C callback 中 `defer` |
| PNG bytes | pointer + `uint64_t` length | `std::vector<std::byte>` | `byte[]` / `ReadOnlyMemory<byte>` | `Foundation.Data` |
| C callback + `void *` | function + caller state | `std::promise` heap state | `UnmanagedCallersOnly` function pointer + `GCHandle` | noncapturing C callback + `Unmanaged.passRetained` state |

.NET 官方互操作指南建议精确匹配 native 签名、在 .NET 7+ 优先 `LibraryImport`、对 callback 优先 unmanaged function pointer，并用 `SafeHandle` 管理 native handle；本方案采用这些边界。[.NET native interoperability best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices) [P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)

### 2.3 异步、取消和终态

- C ABI 的同步 status 只说明参数/入队。`start == OK` 后 wrapper 必须等待恰好一次 `Completed`、`Canceled` 或 `Failed`；不得把 `OK` 当截图完成。
- C++ future、C# Task 与 Swift continuation 都由这一次 callback 完成。callback thunk 必须捕获所有语言异常，任何 exception/panic 都不能穿过 C callback。
- cancel 是 intent，不是同步完成。`cancel == OK` 后仍等待终态；finalization 已提交时 `Completed` 可以赢得竞态。
- 无取消调用不为“可能以后取消”强制创建公开 operation ID：C 传 `NULL`，C++ convenience API 传 `NULL`，C# 仅在 token 可取消时取 ID。Swift async Task 始终具有取消通道，所以 Swift 始终取 ID。
- C# `CancellationToken` 或 Swift Task 被取消时，wrapper 记录 intent；若 ID 已发布立即调用 `cancel`，否则在 `start` 成功写回 ID 后调用。不能因“取消早于 ID”丢失取消，也不能在 native callback 前擅自完成 Task/continuation。
- 用户在 Capture Host 中按 Esc/右键产生 `Canceled`。若语言取消令牌确已请求，则 C# 映射为 `OperationCanceledException`、Swift 映射为 `CancellationError`；否则映射为明确的 `CaptureCanceledException` / `CaptureError.canceledByUser`，避免把用户取消伪装成调用者取消。
- Apple 的 checked continuation 要求每条路径恰好 resume 一次，正好与 native terminal slot 对齐；Swift wrapper 使用 checked 而非 unsafe continuation。[Apple CheckedContinuation](https://developer.apple.com/documentation/swift/checkedcontinuation)

### 2.4 所有权与销毁

1. config/options/UTF-8 views 只在 native 调用期间借用；SDK 在返回前复制需要保留的内容。
2. `start != OK`：没有 callback，wrapper 自己释放 `user_data` state；若请求了 ID，其值必须为 0。
3. `start == OK`：native 恰好回调一次，callback 取得 `user_data` 的最后一个 owning reference。
4. callback 先验证 completion invariant，再复制 PNG；无论转换成功与否都调用一次 `completion_free`。禁止 `free`、`delete`、`Marshal.FreeHGlobal` 或 Swift allocator 释放 completion/PNG。
5. wrapper 返回的 vector/array/Data 完全由调用语言拥有，native completion 已释放；这允许 client 之后安全销毁。
6. C++ destructor、C# `Dispose`/SafeHandle release 与 Swift `close` 最终调用 `client_destroy`。该调用会取消未完成请求并等待已承诺 callback 返回，所以可能阻塞；不得在 callback executor 上调用。
7. C# public client 实现 `IDisposable`；Swift public client提供幂等 `close()`，`deinit` 仅作最后防漏。文档要求在应用自身受控线程/actor 主动关闭，不依赖 GC/deinit 决定时机。

### 2.5 错误模型

wrapper 必须保留原始稳定数值，未知新值也不得崩溃：

| native 结果 | C/C++ | C# | Swift |
| --- | --- | --- | --- |
| 同步 `INVALID_ARGUMENT` / `INVALID_STRUCT_SIZE` | status / `CaptureStartError` | `ArgumentException`，并保留 `NativeStatus` | `CaptureError.invalidArgument(status:)` |
| 同步 `CLIENT_CLOSED` | status / `CaptureStartError` | `ObjectDisposedException` | `CaptureError.clientClosed` |
| 同步 `OUT_OF_MEMORY` | status / `std::bad_alloc` | `OutOfMemoryException` | `CaptureError.outOfMemory` |
| 异步 `Canceled` | `CaptureCanceled` outcome | user cancel exception 或 `CaptureCanceledException` | `CancellationError` 或 `.canceledByUser` |
| 异步 `Failed(code, retryable)` | `CaptureFailure` value | `CaptureException(ErrorCode, IsRetryable)` | `CaptureFailure(code:retryable:)` |
| 未知 status/error/kind | 原始整数 + `UNKNOWN` | `CaptureSdkException` / `Unknown` raw value | `.unknown(rawValue:)` |

`error_name` 只用于稳定诊断名，不成为本地化用户文案，也不把 raw OS error、路径或堆栈带入异常消息。`Busy` 是一次异步失败，不排队；wrapper 不自动重试。Host crash/transport failure 不自动重放。

## 3. C archive 与权威 header

### 3.1 交付形状

权威 header 同时服务 C、C++、C# layout test、Swift module map 与第三方自定义绑定：

```text
sdk/c/
├── include/snaploom/snaploom_capture.h
├── cmake/SnaploomCaptureConfig.cmake.in
├── cmake/SnaploomCaptureConfigVersion.cmake.in
├── pkgconfig/snaploom-capture.pc.in
├── examples/c/capture.c
├── tests/abi/
└── README.md
```

header 规则：C11/C17 可编译、C++ `extern "C"`、Windows 固定 `__cdecl`、只含固定宽度整数/指针/opaque struct；init macro 必须 zero-initialize 并设置 `struct_size`。所有 public define 带 `SNAPLOOM_` 前缀。动态库默认隐藏 symbol，只 allowlist 7 个导出。

CMake 包导出 `Snaploom::Capture` imported shared target，并传播 include directory；C++ target 见下一节。CMake 官方建议以 config file 和 imported target 向消费方表达外部二进制位置与使用要求。[CMake Importing and Exporting Guide](https://cmake.org/cmake/help/latest/guide/importing-exporting/index.html)

### 3.2 平台 archive

```text
snaploom-capture-sdk-c-<version>-windows-x64/
├── include/snaploom/snaploom_capture.h
├── bin/snaploom_capture.dll
├── lib/snaploom_capture.lib
├── lib/cmake/SnaploomCapture/*.cmake
├── examples/c/capture.c
├── LICENSES/Apache-2.0.txt
├── NOTICE
├── THIRD-PARTY-NOTICES.txt
├── sbom.cdx.json
└── README.md

snaploom-capture-sdk-c-<version>-macos-arm64/
├── include/snaploom/snaploom_capture.h
├── lib/libsnaploom_capture.dylib
├── lib/cmake/SnaploomCapture/*.cmake
├── lib/pkgconfig/snaploom-capture.pc
├── examples/c/capture.c
├── LICENSES/Apache-2.0.txt
├── NOTICE
├── THIRD-PARTY-NOTICES.txt
├── sbom.cdx.json
└── README.md
```

macOS dylib 的 install name 为 `@rpath/libsnaploom_capture.dylib`，Mach-O 只能含 arm64，最低系统 macOS 14。Windows DLL 只能含 x64 machine type，import library 与 DLL 的 export table 必须一致。archive 不包含 Capture Host；README 将 `HOST_NOT_FOUND` 指向独立 Host 安装文档。

### 3.3 C 使用示例

```c
static void SNAPLOOM_CALL on_capture(
    snaploom_capture_client_v1 *client,
    snaploom_capture_completion_v1 *completion,
    void *user_data) {
  struct app_state *state = user_data;

  if (completion->kind == SNAPLOOM_COMPLETION_COMPLETED_V1) {
    app_copy_png(state, completion->png_data, completion->png_size,
                 completion->pixel_width, completion->pixel_height);
  } else if (completion->kind == SNAPLOOM_COMPLETION_FAILED_V1) {
    app_report_error(state, completion->error_code,
                     snaploom_capture_error_name_v1(completion->error_code));
  } else {
    app_report_canceled(state);
  }

  snaploom_capture_completion_free_v1(completion);
  app_signal_terminal(state); /* user_data 在 callback 返回前持续有效 */
}

snaploom_capture_client_v1 *client = NULL;
snaploom_status_v1 status =
    snaploom_capture_client_create_v1(NULL, &client); /* 安全默认 */
if (status != SNAPLOOM_STATUS_OK_V1) return 1;

status = snaploom_capture_start_v1(
    client, NULL, on_capture, &state, NULL); /* 常用路径不要求 request ID */
if (status != SNAPLOOM_STATUS_OK_V1) return 1; /* 此路径绝不会 callback */

app_wait_terminal(&state);
snaploom_capture_client_destroy_v1(client);
```

取消示例只把最后一个参数换为 `&request_id`，随后调用 `cancel(client, request_id)` 并继续等待同一个 callback。

## 4. C++17 header-only RAII

### 4.1 公共 Interface

```cpp
namespace snaploom {
struct CaptureResult {
  std::vector<std::byte> png;
  std::uint32_t pixel_width;
  std::uint32_t pixel_height;
  bool clipboard_written;
};
struct CaptureCanceled {};
struct CaptureFailure { Error error; bool retryable; };
using CaptureOutcome =
    std::variant<CaptureResult, CaptureCanceled, CaptureFailure>;

class CaptureOperation {
 public:
  CaptureOperation(CaptureOperation&&) noexcept;
  std::future<CaptureOutcome> take_future();
  std::error_code request_cancel() noexcept;
};

class CaptureClient {
 public:
  explicit CaptureClient(ClientOptions = {}); // 同步失败抛 CaptureSdkError
  CaptureOperation start(CaptureOptions = {}); // 取 ID，可取消
  std::future<CaptureOutcome> capture(CaptureOptions = {}); // 传 NULL ID
  void close();
  ~CaptureClient();
};
}
```

- `CaptureClient` 与 `CaptureOperation` 都 move-only；复制 client 会模糊 destroy 与 callback 等待语义，因此禁止。
- `capture()` 是最短安全路径，内部 `out_request_id == NULL`；`start()` 才返回有 request ID 的 operation。
- `CaptureOperation` 析构不隐式取消，避免临时对象销毁意外关闭用户 UI；shared callback state 保持到终态并总会 free completion。
- callback thunk 的第一条资源动作是建立 completion RAII guard；复制/`promise.set_value` 抛异常时以 `set_exception` 收口，thunk 最外层 `catch (...)`，绝不跨 C ABI。
- C++ wrapper 不导出独立动态库。`snaploom_capture.hpp` 与 C header 一起进入两个 C archives，CMake 同时导出 header-only `Snaploom::CaptureCpp`，其 link interface 依赖 `Snaploom::Capture`。

### 4.2 C++ 示例

```cpp
snaploom::CaptureClient client;
auto operation = client.start();

// 可由其他线程请求；返回 OK 仍不代表已经取消。
// operation.request_cancel();

auto outcome = operation.take_future().get();
std::visit(overloaded{
  [](const snaploom::CaptureResult& result) { upload(result.png); },
  [](snaploom::CaptureCanceled) { show_canceled(); },
  [](const snaploom::CaptureFailure& failure) { report(failure.error); }
}, outcome);
client.close();
```

## 5. 跨平台 C# NuGet

### 5.1 支持面与包布局

首版 package ID 为 `Snaploom.Capture`，目标框架 `net8.0`，managed assembly 为 AnyCPU，但只承诺以下 RID：

| RID | native asset | 系统 |
| --- | --- | --- |
| `win-x64` | `snaploom_capture.dll` | Windows 10 22H2 / Windows 11 x64 |
| `osx-arm64` | `libsnaploom_capture.dylib` | macOS 14+ Apple Silicon |

NuGet 官方约定从 `runtimes/{rid}/native/` 选择 native assets；`win-x64` 与 `osx-arm64` 是 portable RID。包不使用 `contentFiles` 或自定义复制脚本，也不声称支持无对应 asset 的 RID。[Native files in .NET packages](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages) [.NET RID catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)

```text
Snaploom.Capture.<version>.nupkg
├── lib/net8.0/Snaploom.Capture.dll
├── lib/net8.0/Snaploom.Capture.xml
├── runtimes/win-x64/native/snaploom_capture.dll
├── runtimes/osx-arm64/native/libsnaploom_capture.dylib
├── buildTransitive/Snaploom.Capture.targets   # 只做不支持 RID 的诊断
├── LICENSES/Apache-2.0.txt
├── NOTICE
├── THIRD-PARTY-NOTICES.txt
├── sbom.cdx.json
├── README.md
└── icon.png
```

不放 `CaptureHost.exe`、`.app`、GPL source 或 installer。包恢复后使用 `.deps.json`/RID probing 找 native library；不搜索 `PATH`、当前目录或下载缺失 binary。不支持平台在首次构造 client 前抛 `PlatformNotSupportedException`。

### 5.2 公共 API

```csharp
public sealed class CaptureClient : IDisposable
{
    public CaptureClient(CaptureClientOptions? options = null);
    public CaptureSdkVersion RuntimeVersion { get; }
    public Task<CaptureResult> CaptureAsync(
        CaptureOptions? options = null,
        CancellationToken cancellationToken = default);
    public void Dispose();
}

public sealed record CaptureResult(
    ReadOnlyMemory<byte> Png,
    uint PixelWidth,
    uint PixelHeight,
    bool ClipboardWritten);
```

`CaptureClientOptions` 只暴露 Host absolute override、launch timeout 与 handshake timeout；`CaptureOptions` 只暴露 `DisableClipboard` 和 nullable interaction timeout。默认对象与传 `null` 都保持 native 安全默认：剪贴板开启、真人交互无 deadline。

内部实现规则：

- `LibraryImport("snaploom_capture")` 精确声明 7 个 `cdecl` 入口；struct 使用 `LayoutKind.Sequential`、固定宽度字段与显式 reserved fields，不自动 marshal `bool`、enum 或 string。
- client 使用 `SafeHandle`。Microsoft 说明 P/Invoke 会在调用期间维护 SafeHandle 引用并由匹配 release 管理 native lifetime，适合本 opaque handle。[SafeHandle](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-runtime-interopservices-safehandle)
- callback 为 `static` + `UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])` function pointer。per-request state 以 `GCHandle` 传给 `user_data`；`start != OK` 由调用线程 free，`start == OK` 由唯一 callback `take/free`。
- `TaskCompletionSource<CaptureResult>` 使用 `RunContinuationsAsynchronously`，防止用户 continuation 在 native callback stack 内执行。
- callback 在 `try` 中检查 kind/长度并 `Marshal.Copy` 到新 `byte[]`，在 `finally` 调 `completion_free` 并释放 GCHandle；callback 最外层捕获全部 managed exception，以 faulted Task 收口。
- token 不可取消时传 `out_request_id == NULL`；token 可取消时传 ID 并注册 cancel intent。registration 与 request state 一起在终态释放。
- `Dispose` 是幂等且可能阻塞，调用 native destroy 等待 callbacks；不从 callback 或用户 continuation 中持锁调用。

### 5.3 C# 示例

```csharp
using var client = new CaptureClient();
using var cts = new CancellationTokenSource();

try
{
    CaptureResult result = await client.CaptureAsync(
        new CaptureOptions { DisableClipboard = false }, cts.Token);
    await File.WriteAllBytesAsync("selection.png", result.Png.ToArray());
}
catch (CaptureException error) when (error.ErrorCode == CaptureErrorCode.Busy)
{
    // 已有全局 Capture Session；SDK 不排队，稍后由业务发起新请求。
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // native 终态已经确认取消；不是仅仅发出了 cancel intent。
}
```

## 6. Swift Package 与 XCFramework

### 6.1 分发结构

仓库根 `Package.swift` 是远程 Swift package 的 manifest，因为 SwiftPM 远程包要求 Git 仓库根存在 manifest 并以 SemVer tag 发布。[SwiftPM releasing and publishing](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/releasingpublishingapackage/)

```text
Package.swift
sdk/swift/
├── Sources/SnaploomCapture/
│   ├── CaptureClient.swift
│   ├── CaptureResult.swift
│   └── CaptureError.swift
├── Tests/SnaploomCaptureTests/
└── README.md
```

manifest 固定 `.macOS(.v14)`，源码 target `SnaploomCapture` 依赖远程 binary target `CSnaploomCapture`：

```swift
.binaryTarget(
    name: "CSnaploomCapture",
    url: "https://github.com/moch1ce/Snaploom/releases/download/v1.2.3/CSnaploomCapture-1.2.3.xcframework.zip",
    checksum: "<swift-package-compute-checksum>"
)
```

Apple 要求远程 binary target 使用 HTTPS ZIP、在 archive 根包含 XCFramework，并由 manifest checksum 校验；XCFramework 可包含动态库与 headers。[Distributing binary frameworks as Swift packages](https://developer.apple.com/documentation/xcode/distributing-binary-frameworks-as-swift-packages) [Creating a multiplatform binary framework bundle](https://developer.apple.com/documentation/xcode/creating-a-multi-platform-binary-framework-bundle)

```text
CSnaploomCapture.xcframework/
├── Info.plist
└── macos-arm64/
    ├── libsnaploom_capture.dylib
    └── Headers/
        ├── snaploom_capture.h
        └── module.modulemap   # module CSnaploomCapture
```

首版 XCFramework 只有 macOS arm64 slice，不放空 simulator/iOS/Intel slice。Release 构建以 `xcodebuild -create-xcframework -library ... -headers ...` 生成，检查 install name、arm64、macOS 14 deployment target、module import 与消费方签名/嵌入。

### 6.2 Swift API 与 callback bridge

```swift
public final class CaptureClient: @unchecked Sendable {
    public init(options: CaptureClientOptions = .init()) throws
    public var runtimeVersion: CaptureSDKVersion { get }
    public func capture(
        options: CaptureOptions = .init()
    ) async throws -> CaptureResult
    public func close() throws
}

public struct CaptureResult: Sendable {
    public let pngData: Data
    public let pixelWidth: UInt32
    public let pixelHeight: UInt32
    public let clipboardWritten: Bool
}
```

实现使用 `withTaskCancellationHandler` + `withCheckedThrowingContinuation`：

- private `RequestBox` 以锁保护 continuation、request ID、cancel intent 和 terminal flag。
- `Unmanaged.passRetained(box).toOpaque()` 成为 `user_data`。`start != OK` 立即 `release()`；成功后 callback 用 `takeRetainedValue()` 恰好一次。
- Swift async Task 始终可能收到取消，因此传 `&requestID`；这不把 ID 暴露给调用者。取消 handler 不能 resume continuation，只记录/发送 native cancel 并等待 callback。
- callback 以 `defer { completion_free(completion) }` 保证释放；`Completed` 用 `Data(bytes:count:)` 复制 PNG 后才 resume。Apple 明确该 initializer 复制传入 bytes，适合把 native allocation 收口在 callback。[Foundation Data](https://developer.apple.com/documentation/foundation/data)
- callback 不假设 main actor。continuation resume 让等待 task 回到自身 executor；wrapper 不替第三方宿主切换 UI actor。
- `close()` 幂等并在 callback executor 外执行；client 内部 lock 不跨 `client_destroy` 持有，避免 callback 回来时死锁。

### 6.3 Swift 示例

```swift
let client = try CaptureClient()
defer { try? client.close() }

do {
    let result = try await client.capture()
    try result.pngData.write(to: outputURL, options: .atomic)
} catch is CancellationError {
    // Task cancellation 已经由 native terminal callback 确认。
} catch CaptureError.busy {
    // 全局 Capture Session 正在使用；不自动排队或重试。
}
```

## 7. 版本与兼容策略

### 7.1 版本轴

| 版本 | wrapper 行为 |
| --- | --- |
| Release/package semver | C archives、NuGet、Swift package/XCFramework 与 Host 使用相同 `X.Y.Z`；wrapper public API breaking change 升 major |
| C ABI major | package 初始化调用 `version_v1` 并要求 `abi_major == 1`；只使用自身已知 `struct_size` |
| Native SDK semver | 与 package major 不同则拒绝；同 major 的 minor/patch 不同允许运行并通过 `RuntimeVersion` 暴露诊断 |
| IPC major/minor | 完全留在 C SDK 内协商，wrapper 不解析、不暴露 wire capability |

NuGet 与 SwiftPM 都以 SemVer 描述 package compatibility；本项目禁止移动/覆盖已发布 tag 或替换同 URL binary。[NuGet package versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning) [Swift PackageDescription](https://docs.swift.org/package-manager/PackageDescription/PackageDescription.html)

### 7.2 ABI 1.x 演进

- wrapper 所有 input struct 先 zero-init，再填 `struct_size`；不填写未来 reserved/flags。
- output 先提供 wrapper 已知 size；读取字段前检查 native 返回的 `struct_size` 覆盖该字段。
- 新 native error/kind/flag 保留 raw value；旧 wrapper 映射 unknown，不把未知终态当 Completed。
- official package 默认携带同版本 native binary，但测试允许手工替换为同 ABI major 的旧/新 1.x binary，以验证 `old wrapper ↔ new library` 与 `new wrapper ↔ old library`。
- `_v2` 出现时必须以新 package major 公开；v1/v2 可在 native library 过渡期并存，但 v1 wrapper 不动态猜测 v2。

## 8. Release 资产与消费文档

同一 `v<version>` GitHub Release 至少新增：

```text
snaploom-capture-sdk-c-<version>-windows-x64.zip
snaploom-capture-sdk-c-<version>-windows-x64.zip.sha256
snaploom-capture-sdk-c-<version>-macos-arm64.tar.gz
snaploom-capture-sdk-c-<version>-macos-arm64.tar.gz.sha256
Snaploom.Capture.<version>.nupkg
Snaploom.Capture.<version>.nupkg.sha256
Snaploom.Capture.<version>.snupkg
CSnaploomCapture-<version>.xcframework.zip
CSnaploomCapture-<version>.xcframework.zip.sha256
```

它们与 App、Capture Host、对应源码、总 SBOM/NOTICE 资产同版本原子发布，但每个 SDK 包内部仍有 Apache-2.0 LICENSE、适用 NOTICE、第三方许可证与该包 SBOM。SDK package scan 必须证明不含 GPL Host/Tauri/capture-core/platform Adapter symbol 或二进制。

开发者文档固定包含：五分钟 quickstart、Host 独立安装/发现、默认剪贴板与 opt-out、Busy、用户取消、调用者取消、错误恢复、线程/callback、close 阻塞、内存释放、版本检查、日志隐私、支持矩阵、校验和、许可证与 Host 再分发说明。文档不得承诺“闭源组合零 GPL 风险”；再分发 Host 的开发者必须按 GPL 履行独立义务，并以 #33 对实际包复核为稳定 SDK 发布门禁。

## 9. 测试与发布门禁

### 9.1 共享 ABI 测试

- MSVC x64 C17/C++17、Clang arm64 C17/C++17 编译 public headers，最高 warning 为 error。
- Rust mirror 与 C/C++/C#/Swift importer 对照 `sizeof`、alignment、`offsetof`、cdecl callback；固定 golden layout。
- export allowlist 精确 7 symbols；Windows DLL/import lib、macOS dylib/XCFramework architecture/install name/header/module map 检查。
- null config/options、`out_request_id == NULL`、短/长 struct、unknown flags、UTF-8、reserved、所有稳定数字。
- callback exactly-once、start 非 OK 零 callback、cancel/complete race、destroy wait、callback reentrancy、Host crash、partial PNG、completion free/zero instrumentation。

### 9.2 wrapper conformance

每种语言用 fake C shim 跑确定性 unit tests，再用真实 C SDK + fake Host 和真实 Host 跑 integration：

| 范围 | 必测项 |
| --- | --- |
| C | no-ID 常用路径、显式 cancel、每个终态/free、create/destroy、多线程 |
| C++ | move-only、future 一次取得、operation 析构不取消、异常不越 ABI、client 析构等待、ASan |
| C# | SafeHandle lifetime、GC compaction/stress、GCHandle 一次释放、Task continuation 不在 callback stack、token-before-ID race、Dispose race、trim、NativeAOT |
| Swift | continuation 恰好一次、Task cancel-before-ID、Data copy 后 native 清零、close/callback race、TSan、Swift strict concurrency、module import |

所有 wrapper 都覆盖 Completed/Canceled/每个 stable Failed、unknown code、Busy、默认/禁用剪贴板、128 MiB 前后边界、client destroy、1000 次 start/Busy/cancel、100 次 5K result，并证明 handle/fd/thread/allocation 回基线。

### 9.3 包消费矩阵

- 从最终 archive 新建空 CMake C/C++ consumer，以 relative extracted prefix 完成 configure/build/run；不得引用仓库源码路径。
- 从最终 `.nupkg` 建本地 feed；Windows x64 与 macOS arm64 分别执行 .NET 8 与当前 .NET LTS/current 的 build/run/publish，检查 RID asset、trim 与 NativeAOT。unsupported RID 必须给稳定诊断。
- 从最终 Git tag + Release ZIP 解析 `Package.swift`，验证 checksum、下载、`swift build/test` 与 Xcode macOS app consumer；断网 cache 与 checksum mismatch 路径也要明确失败。
- 解包逐文件审计 LICENSE/NOTICE/SBOM、源码链接、header/XML/DocC 文档、示例可编译、无 Host、无 GPL object、无绝对构建路径、无私钥/签名 secret。
- 同一 tag 的所有 package/version/native `version_v1`、SHA256、Release metadata 必须一致；任何单项失败只允许保留不可见草稿，不公开部分 SDK。

## 10. 明确失败与降级

| 失败 | wrapper 唯一行为 |
| --- | --- |
| native library 缺失/错误 architecture | 平台/加载错误；不从 PATH、网络或临时目录寻找替代物 |
| ABI major 不同 | 明确 incompatible；不猜 struct、不绑定其他 symbol |
| Host 未安装 | 透传 `HOST_NOT_FOUND`；文档链接独立安装，不下载 Host |
| 已有 Capture Session | 一次 `BUSY` 失败；不排队、不自动重试 |
| 调用者取消 | 发送 cancel intent并等待 native terminal；不提前完成 future/Task/continuation |
| Host crash/transport error | 当前调用失败且不 replay；调用者可发起全新请求 |
| PNG 非法/过大/copy 失败 | free/清零 native completion，语言结果失败；不返回 partial buffer |
| completion/code 为未来未知值 | 保留 raw value并失败；不把未知值当成功 |
| callback 内语言异常 | 在 thunk 内转换为语言异步失败并正常返回 C；绝不 unwind |
| package/Release 版本或 checksum 不一致 | 发布门禁失败；不覆盖既有 tag/asset |

任何后续增加官方 Node/Java/Python binding、静态 SDK、zero-copy、caller-provided allocator/dispatcher、额外 native symbol 或把 Host 嵌入 SDK package 的提案，都必须新开 ticket 并重新审查 ABI、所有权、许可和发布边界，不能作为 wrapper 内部优化静默加入。

## 11. 实施顺序

1. 生成/冻结 C header、platform shared libraries、layout/export tests 与两个 C archives。
2. 实现 header-only C++ wrapper 和 C/C++ package consumer tests。
3. 实现 `Snaploom.Capture` managed API、LibraryImport mirror、SafeHandle/callback bridge、双 RID NuGet 与 GC/AOT tests。
4. 实现 Swift source wrapper、C module map、arm64 XCFramework、remote binary target/checksum 与 Swift concurrency tests。
5. 接真实 Capture Host 跑同一 Completed/Cancel/Busy/crash/result matrix，验证三语言返回同一 PNG bytes。
6. 接 #31 的原子 Release、checksum/SBOM/NOTICE/package scan；把实际产物交 #33 做发布前法律复核。

## 12. 一手资料

- .NET native interop：[best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)、[P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)、[SafeHandle](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-runtime-interopservices-safehandle)
- NuGet native distribution：[Native files in .NET packages](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages)、[RID catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)、[package versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning)
- Swift/XCFramework：[Distributing binary frameworks as Swift packages](https://developer.apple.com/documentation/xcode/distributing-binary-frameworks-as-swift-packages)、[Creating an XCFramework](https://developer.apple.com/documentation/xcode/creating-a-multi-platform-binary-framework-bundle)、[CheckedContinuation](https://developer.apple.com/documentation/swift/checkedcontinuation)、[Foundation Data](https://developer.apple.com/documentation/foundation/data)
- SwiftPM：[PackageDescription](https://docs.swift.org/package-manager/PackageDescription/PackageDescription.html)、[Releasing and publishing a package](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/releasingpublishingapackage/)
- C/C++ build consumption：[CMake Importing and Exporting Guide](https://cmake.org/cmake/help/latest/guide/importing-exporting/index.html)
- 共同版本规则：[Semantic Versioning 2.0.0](https://semver.org/)
