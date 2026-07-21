# Capture SDK C ABI 与本地 IPC 决策

状态：Issue [#28](https://github.com/moch1ce/Snaploom/issues/28) 的实施输入

适用范围：Windows 10 22H2 / Windows 11 x64、macOS 14+ arm64、Capture SDK 1.x 与 Capture Host 1.x

上位约束：[功能等价合同](./snaploom-feature-equivalence-contract.md)、ADR 0001～0004、[Windows Adapter 决策](./windows-rust-platform-adapter.md)、[macOS Adapter 决策](./macos-rust-platform-adapter.md)

## 1. 最终结论

Capture SDK 首版固定采用以下方案，不再并行保留备用协议：

1. Capture SDK 是 Apache-2.0 的进程内客户端库，只暴露 7 个 `extern "C"` 函数、opaque client handle、固定宽度整数、带 `struct_size` 的 append-only 结构和一次性异步完成回调。Rust/Tauri、平台句柄、IPC 类型和 Host 内部对象都不越过 C ABI。
2. GPL Capture Host 是每个用户登录会话、每个 IPC major 唯一的独立进程。Snaploom App 与所有 SDK client 都连接这个 Host；Host 内唯一 `CaptureSessionGate` 决定全局 `Busy`，SDK 不在各自进程内建立互相看不见的门控。
3. Windows 使用带严格 DACL、`PIPE_REJECT_REMOTE_CLIENTS` 和 `FILE_FLAG_FIRST_PIPE_INSTANCE` 的 duplex byte-mode named pipe；macOS 使用私有 `0700` 目录中的 `0600` `AF_UNIX/SOCK_STREAM` socket。两端都验证 OS peer identity，只接受同一登录会话/有效用户。
4. wire schema 使用公开的 proto3 Protocol Buffers。每帧有固定 12-byte little-endian header；控制帧有界，PNG 以连续的 bounded chunks 传输，不使用共享内存或临时文件。
5. C ABI semver、ABI major、IPC major/minor 是三个独立版本轴。ABI 1.x 只能 append；IPC 相同 major 内用 minor 区间协商和 capability bits，breaking wire change 必须升 major 并使用新 endpoint。
6. `capture_start` 只报告本地参数/排队是否成功；只要它返回 `OK`，SDK 就必须恰好回调一次 `Completed`、`Canceled` 或 `Failed`。`Busy`、Host 缺失、协议不兼容、Host crash 和超时都走这一次终态回调。
7. `Completed` 返回 Host 最终合成的同一份 PNG bytes 与物理像素尺寸。SDK 模式默认先把同一 bytes 写入系统剪贴板；禁用剪贴板必须由调用者显式设置 flag。
8. active Capture Session 不自动重放。Host crash、transport 中断、鉴权失败或超时都结束当前请求；SDK 只允许下一次新请求重新发现/启动 Host。
9. Capture Host 与 SDK 仍是不同二进制和不同 Release 资产。SDK 不捆绑、链接或自动下载 GPL Host；显式 Host 路径与平台安装登记只是发现位置，不改变许可证边界。

## 2. 架构与授权边界

最终依赖方向固定为：

```text
Snaploom App ─┐
              ├─ public protocol ─ local IPC ─> GPL Capture Host ─> GPL capture/platform/UI
Closed host ─> Apache C ABI SDK ───┘
```

- `sdk/c-abi`、`sdk/protocol`、C/C++/C#/Swift wrappers 与示例采用 Apache-2.0。
- App、Capture Host、Capture Session、截图/标注/导出和平台 Adapter 采用 GPL-3.0-or-later。
- SDK 只能了解进程启动、endpoint、协议版本、request ID、终态、PNG 与尺寸；不得共享 Host 的对象图、Canvas 状态、平台句柄、Rust trait、函数回调地址或可变共享内存。
- Host 必须能独立启动并服务 App 或 SDK。App 也必须经同一协议进入 Host，不能为“自家入口”旁路全局 gate。

这与 ADR 0003 的独立进程边界和许可证审计的单向依赖一致。官方 SDK 包不得包含 Host；如果第三方再分发单独的 Host 资产，其 GPL 义务不被 SDK 的 Apache-2.0 许可证覆盖，详见[许可证审计](./tauri-open-source-license-audit.md)。

## 3. 小而深的 C ABI

### 3.1 导出面

首版仅导出以下符号；后缀 `_v1` 是 ABI major，不是发布 semver：

```c
snaploom_capture_version_v1
snaploom_capture_client_create_v1
snaploom_capture_start_v1
snaploom_capture_cancel_v1
snaploom_capture_client_destroy_v1
snaploom_capture_completion_free_v1
snaploom_capture_error_name_v1
```

`error_name` 只返回稳定、非本地化、无用户数据的常量名称，便于日志和诊断；它不返回系统 message、路径或堆栈。语言 wrapper 只绑定这 7 个符号，不直接绑定 IPC。

Rust 官方 FFI 文档只保证 `#[repr(C)]` 类型按 C 表示传递，也明确指出 Rust `String`/`Vec` 不是 C 字符串/数组，Rust panic 不应穿过非 unwind C ABI。[Rustonomicon FFI](https://doc.rust-lang.org/nomicon/ffi.html) 因此实施规则为：

- 所有入口为 `extern "C"`，Windows 明确 `__cdecl`；不用 Rust ABI、C++ ABI 或平台默认可能变化的回调约定。
- public header 只使用 `uint32_t`、`uint64_t`、原始指针和 opaque incomplete struct；不用 C `bool`、裸 `enum`、bitfield、`long`、`size_t`、Rust slice/string、泛型或 inline allocator。
- Rust mirror 全部 `#[repr(C)]`，CI 同时验证 MSVC/Clang 的 size、alignment 与 `offsetof`。
- 每个导出入口拦截 Rust unwind 并映射为稳定 `INTERNAL`；任何 panic 或 foreign exception 都不得跨 ABI。C++ wrapper 也必须在 callback thunk 内捕获异常。
- 调用者 callback 不得用 C++ exception、Rust panic 或 `longjmp` 穿过 C callback 边界；官方 wrappers 必须在自己的 thunk 内收口这些语言级控制流。违反此调用约定不是可恢复的 SDK error。
- 动态库隐藏默认 visibility，符号 allowlist 只能出现上述 7 个 C symbols 和平台必需 runtime symbols。

#### 3.1.1 三类 Interface 方案对比

在冻结 ABI 前，分别以“导出面最小”“未来扩展最强”和“常用调用最短”为目标设计了三类替代方案。评估结果如下：

| 候选 | 表面 Interface | 隐含代价 | 决策 |
| --- | --- | --- | --- |
| 3-symbol `open/execute/close` | 用 tagged command 合并 start/cancel，callback 期间借用 PNG | 每个 wrapper 都要重复 request-ID 分发、错误名映射和大 PNG 复制；条件式所有权增多 | 否决 |
| 单 bootstrap symbol + interface table | vtable、operation handle、extension chain 和 event stream | ABI/interface/extension/protocol/capability 版本轴、引用计数与未知消息攻击面扩大，但当前没有第二类 SDK use case | 否决 |
| 7-symbol client | 5 个核心生命周期/数据函数 + 2 个可选诊断函数 | 常用路径只是 `create → start → callback/free → destroy`，cancel 按需使用 | **采用** |

符号数不等于 Interface 事实数。`create/destroy`、`start/cancel`、`completion/free` 分别对应真实的 client、request 和 cross-allocator ownership Seam；强行合并只会把实现知识推给 C++、C# 和 Swift wrapper。通用 event stream、caller dispatcher、custom transport 和扩展链也只有一个假设中的 Adapter，不在 v1 暴露。

为让安全默认的常用路径不产生无意义样板，`start` 的 `out_request_id` 可为 `NULL`；调用者仅在需要主动取消时接收 request ID。语言 wrapper 可继续对外只提供 `capture async`、`cancel` 和 `dispose`，但必须私有绑定这 7 个底层符号，不重做 IPC 或结果生命周期。

### 3.2 头文件形状

下列片段锁定行为与字段类型；实施 ticket 可以补注释和宏，但不得改变字段含义或所有权：

```c
typedef uint32_t snaploom_status_v1;
typedef uint32_t snaploom_completion_kind_v1;
typedef uint32_t snaploom_error_v1;
typedef uint64_t snaploom_request_id_v1;

typedef struct snaploom_capture_client_v1 snaploom_capture_client_v1;

typedef struct snaploom_utf8_view_v1 {
  const uint8_t *data;
  uint64_t length;
} snaploom_utf8_view_v1;

typedef struct snaploom_capture_version_info_v1 {
  uint32_t struct_size;
  uint32_t abi_major;
  snaploom_utf8_view_v1 sdk_semver;
  uint64_t reserved[4];
} snaploom_capture_version_info_v1;

typedef struct snaploom_capture_client_config_v1 {
  uint32_t struct_size;
  uint32_t flags;
  snaploom_utf8_view_v1 host_executable_override;
  uint32_t launch_timeout_ms;
  uint32_t handshake_timeout_ms;
  uint64_t reserved[4];
} snaploom_capture_client_config_v1;

typedef struct snaploom_capture_options_v1 {
  uint32_t struct_size;
  uint32_t flags;              /* DISABLE_CLIPBOARD is opt-out */
  uint64_t interaction_timeout_ms; /* 0 means no user-interaction deadline */
  uint64_t reserved[4];
} snaploom_capture_options_v1;

#define SNAPLOOM_CAPTURE_CLIENT_CONFIG_V1_INIT \
  { sizeof(snaploom_capture_client_config_v1), 0 }
#define SNAPLOOM_CAPTURE_OPTIONS_V1_INIT \
  { sizeof(snaploom_capture_options_v1), 0 }

typedef struct snaploom_capture_completion_v1 {
  uint32_t struct_size;
  uint32_t kind;               /* COMPLETED / CANCELED / FAILED */
  uint32_t error_code;         /* NONE unless FAILED */
  uint32_t flags;              /* e.g. RETRYABLE */
  uint64_t request_id;
  const uint8_t *png_data;     /* non-null only for COMPLETED */
  uint64_t png_size;
  uint32_t pixel_width;
  uint32_t pixel_height;
  uint64_t reserved[4];
} snaploom_capture_completion_v1;

typedef void (SNAPLOOM_CALL *snaploom_capture_callback_v1)(
    snaploom_capture_client_v1 *client,
    snaploom_capture_completion_v1 *completion,
    void *user_data);

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_version_v1(snaploom_capture_version_info_v1 *out_version);

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_client_create_v1(
    const snaploom_capture_client_config_v1 *config,
    snaploom_capture_client_v1 **out_client);

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_start_v1(
    snaploom_capture_client_v1 *client,
    const snaploom_capture_options_v1 *options,
    snaploom_capture_callback_v1 callback,
    void *user_data,
    snaploom_request_id_v1 *out_request_id); /* optional; NULL disables caller cancel */

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_cancel_v1(
    snaploom_capture_client_v1 *client,
    snaploom_request_id_v1 request_id);

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_client_destroy_v1(snaploom_capture_client_v1 *client);

SNAPLOOM_API void SNAPLOOM_CALL
snaploom_capture_completion_free_v1(
    snaploom_capture_completion_v1 *completion);

SNAPLOOM_API const char *SNAPLOOM_CALL
snaploom_capture_error_name_v1(snaploom_error_v1 error_code);
```

函数行为：

| 函数 | 同步结果 | 异步/所有权语义 |
| --- | --- | --- |
| `version` | 写出 ABI major 与 SDK semver | 不连接或启动 Host |
| `client_create` | 校验 config，建立私有 I/O 与 callback executor | 默认 lazy connect；成功后调用者拥有 client |
| `start` | 校验参数、分配非零进程内 request ID 并入队；`out_request_id` 可为 `NULL` | 返回 `OK` 后恰好一次 callback；非 `OK` 时若提供 output 则写 0，且绝不 callback |
| `cancel` | 对已知未终态请求设置幂等 cancel intent | `OK` 不保证最终一定取消；Host 已提交结果时 `Completed` 可以赢得竞态 |
| `client_destroy` | 标记 closed、拒绝新 start、取消未完成请求 | 等待所有已承诺 callback 返回；返回后不再 callback，不关闭其他 client 共用的 Host |
| `completion_free` | `NULL` 是 no-op | 可在任意线程、client 销毁后调用；只能对每个 completion 调用一次 |
| `error_name` | stable code → static ASCII name | 未知 code 返回 `UNKNOWN`，不分配内存 |

### 3.3 结构扩展和输入校验

- 每个 input/output struct 首字段都是 `struct_size`。调用者先 zero-initialize，再填写它理解的字段。
- `client_create` 的 `config == NULL` 和 `start` 的 `options == NULL` 都表示使用安全默认值；`start` 的 `out_request_id == NULL` 表示调用者不需要主动取消。除此例外，required output pointer、callback 或 client handle 不允许为 `NULL`。
- 库只读取 `min(caller_size, known_size)`；小于 v1 最小尺寸返回 `INVALID_STRUCT_SIZE`。未来追加字段必须在尾部，旧字段 offset 永不改变。
- `flags` 与 `reserved` 中未知/非零 bit 默认拒绝为 `INVALID_ARGUMENT`，除非对应 minor 明确改为可忽略；不能让旧库误解新安全语义。
- `host_executable_override` 是调用期间有效的 UTF-8 byte view；`data == NULL && length == 0` 表示未提供。内部立即复制，Windows 严格转 UTF-16。不得搜索 `PATH`、当前目录或未经校验的环境变量。
- `version_info.sdk_semver` 与 `error_name` 指向 library-owned static bytes，在动态库 unload 前有效，调用者不得释放；semver view 不要求 NUL 结尾，`error_name` 是 NUL-terminated ASCII。
- config 的默认 launch timeout 为 5 秒、handshake timeout 为 2 秒；调用者可在 1～30 秒范围覆盖。`interaction_timeout_ms == 0` 是默认，因为真人编辑没有合理的硬超时。

### 3.4 completion 与内存所有权

callback 得到的 completion 及其 PNG allocation 由 SDK 的 allocator 创建，所有权在进入 callback 时转给调用者：

- `Completed`：`png_data != NULL`、`png_size > 0`、宽高均大于 0；bytes 保持有效直到 `completion_free`。
- `Canceled` / `Failed`：PNG pointer、size、width、height 必须全为 0。
- 调用者可在 callback 返回前后复制或保留 completion，但最终只能用同一 SDK 动态库的 `completion_free` 释放；不能调用 `free`、`delete`、`CoTaskMemFree` 或 Swift allocator。
- `completion_free` 先清零 PNG 与结构中的敏感内容，再释放；partial result、解码失败和 Host crash 路径也必须清零已收字节。
- `user_data` 始终由调用者拥有。成功 `start` 后，它必须存活到 callback 返回；SDK 不读取、复制或释放它。

这种“一个 terminal allocation、一个匹配 free”比跨语言暴露独立 buffer retain/release 更深：C# wrapper 可用 `SafeHandle`，Swift wrapper 可复制为 `Data` 后立刻 free，C++ wrapper 可用单一 RAII deleter。

### 3.5 线程、重入与销毁

- client 的 `start`、`cancel`、`version` 和 `completion_free` 都是 thread-safe。
- callback 在 SDK 私有 callback executor 上调用，绝不在 Host I/O reader、Tauri main thread、调用者线程或平台 UI thread调用。
- 同一 client 的 callbacks 串行执行；不同 clients 不承诺同一线程或全局顺序。
- callback 内允许调用 `start`、`cancel` 和 `completion_free`。callback 内调用同一 client 的 `client_destroy` 返回 `CALLBACK_CONTEXT` 且不销毁，避免 self-join；wrapper 应在其他线程销毁。
- `client_destroy` 与并发 `start` 竞态由内部 closed flag 线性化：若 `start` 先返回 `OK`，仍必须得到终态 callback；否则返回 `CLIENT_CLOSED` 且不 callback。
- `client_destroy` 必须等正在执行的 foreign callback 返回，因此调用者不得在 callback 内执行无界阻塞；SDK 不能安全强杀外部语言栈来伪造销毁完成。

## 4. Capture Host 发现、启动和唯一性

### 4.1 Host 资产与位置

SDK 不联网、不下载 Host，也不把 Host 打进 NuGet、XCFramework、C SDK archive 或 SDK 动态库。

发现顺序固定为：

1. `host_executable_override`：供开发、企业部署或第三方单独再分发 Host 时显式指定绝对路径。
2. 平台安装登记：Windows 用户级 Host installer 写入 `HKCU\Software\Snaploom\CaptureHost\InstallPath`；macOS 通过 bundle identifier `com.snaploom.capture-host` 的 Launch Services 记录定位 `.app`。[Windows Registry hives](https://learn.microsoft.com/en-us/windows/win32/sysinfo/registry-hives) [NSWorkspace application lookup](https://developer.apple.com/documentation/appkit/nsworkspace/urlforapplication%28withbundleidentifier%3A%29)
3. Snaploom App 自用时可显式传递其同一 GPL app asset 内的 adjacent Host；这不是公共 SDK 的搜索路径。

找不到时返回 `HOST_NOT_FOUND`；绝不从工作目录、`PATH`、临时目录或网络猜测。启动前要求绝对、存在、普通可执行文件；拒绝网络路径和 world-writable 可执行文件。官方发布验证签名/公证，但 SDK 不把“只能由 Snaploom 官方签名”写成协议条件，以免阻止 GPL 修改版通过显式 override 运行。

### 4.2 安全启动

- Windows 使用 `CreateProcessW`，非空 `lpApplicationName` 指向已解析的完整路径，不经 shell。Microsoft 明确记录了 `lpApplicationName == NULL` 与含空格路径会选错 executable 的风险。[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
- macOS 使用 `posix_spawn` 的完整 executable path，不用 `system`、shell 或 `posix_spawnp` PATH search。[Apple posix_spawn](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/posix_spawn.2.html)
- 子进程环境以 allowlist 重建，至少移除 `DYLD_*`、`LD_*` 与协议 secret；working directory 设为稳定非用户输入目录。
- launcher 建立一条仅子进程继承的 bootstrap channel。Windows 用 anonymous pipe 并通过 `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` 只继承指定 handle；macOS 用 `socketpair` 与 `posix_spawn_file_actions` 只映射指定 fd。Windows 官方文档说明 handle allowlist 可限制具体 child 继承对象；Apple `socketpair` 提供匿名、已连接的 descriptor pair。[Windows handle inheritance](https://learn.microsoft.com/en-us/windows/win32/procthread/inheritance) [Apple socketpair](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/socketpair.2.html)
- launcher 用 `BCryptGenRandom(BCRYPT_USE_SYSTEM_PREFERRED_RNG)` 或 `SecRandomCopyBytes` 生成 32-byte challenge，只通过 bootstrap channel 发送；Host 建立 endpoint 后回显 challenge、PID、instance ID 和协商前版本。challenge 不进 argv、environment、registry、socket path 或日志。[BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom) [SecRandomCopyBytes](https://developer.apple.com/documentation/security/secrandomcopybytes%28_%3A_%3A_%3A%29)

bootstrap challenge 只证明“刚启动并握有继承 channel 的进程”完成 readiness，不替代 steady-state endpoint 的 OS peer credential 校验。

### 4.3 单例与并发启动

endpoint 名包含 IPC major 和用户登录会话 identity。每个登录会话、每个 protocol major 只能有一个 Host leader：

- 首个 Host 原子创建 exclusive endpoint 并成为 leader。
- 同时启动的 loser 检测到 endpoint 已存在后不创建 UI、不接受业务，连接 leader 健康检查成功即退出。
- SDK launcher 等待 endpoint/readiness 至多 launch timeout；多个 SDK 同时启动时都连接同一个 winner。
- Host 在无 client、无 App keepalive、无 active Capture Session 后保留 30 秒 idle grace 再退出；任何新连接取消退出。Host 退出不删除安装登记。
- Host 正常退出关闭 endpoint；异常退出后的 stale macOS socket 只能在取得 owner-checked lock 并确认连接失败后清理。

## 5. Transport 比较与选择

| 候选 | 适配性 | 安全/所有权代价 | 决策 |
| --- | --- | --- | --- |
| Windows named pipe | 原生 duplex local IPC、DACL、登录会话 SID、async I/O、server/client PID | 必须显式拒绝 remote、不能用宽松默认 ACL、处理多实例 race | **Windows 采用** |
| Unix domain stream socket | macOS 原生可靠双向 byte stream；`getpeereid` 可核验 euid/egid | socket path ownership、mode、长度和 stale inode 必须严格处理 | **macOS 采用** |
| shared memory | 大 payload 可少一次 kernel copy | 还需另建控制 channel、同步、长度/生命周期/崩溃回收/ACL；暴露跨边界可变内存会削弱许可证与安全边界 | 不采用；无实测瓶颈不得引入 |
| temporary file | 实现表面简单、可容纳大 PNG | 路径/ACL/symlink/TOCTOU、清理和 crash 残留会把截图落盘；与“不保留截图历史”冲突 | 不采用，也不作大图 fallback |

PNG 已经是压缩结果；使用有界 chunk stream 可以把 transport working set 限制在约一个 chunk，避免为首版承担 shared-memory handle duplication、mapping ownership 和 crash cleanup。临时文件会让截图在用户未选择保存时落盘，因此不能成为透明降级。

## 6. Windows transport

endpoint 形状为 `\\.\pipe\Snaploom.CaptureHost.p<major>.<logon-sid-hash>`；hash 只用于名称，访问控制使用真实 logon SID。

Host 的第一实例使用：

- `PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE`；
- `PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_REJECT_REMOTE_CLIENTS`；
- 显式 security descriptor，只允许 Host identity、当前 logon SID 与必要的 `SYSTEM` 访问，不使用默认 descriptor；
- client 只申请完成读写所需的具体 rights，不用会额外包含 pipe-instance 权限的宽泛 `GENERIC_WRITE`。

Microsoft 说明默认 named-pipe ACL 还会给 Everyone 和 anonymous read，因此本项目禁止 `NULL` security descriptor；官方也建议用 logon SID 阻止不同 Terminal Services session 访问。[Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) `FILE_FLAG_FIRST_PIPE_INSTANCE` 用来确认 leader 确实创建第一个 pipe object，`PIPE_REJECT_REMOTE_CLIENTS` 明确拒绝远程 client。[CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea)

连接后双方执行：

1. Host 用 `GetNamedPipeClientProcessId` 取得 PID，打开其 process token，要求 `TokenUser` 等于 Host 的用户 SID，并在 `TokenGroups` 中找到与当前会话一致、带 `SE_GROUP_LOGON_ID` 的 logon SID；SDK 用 `GetNamedPipeServerProcessId` 对 server 做同样检查。
2. PID/token 查询失败、process 已替换、SID/session 不匹配都关闭连接并返回 `AUTHENTICATION_FAILED`。
3. 完成 OS identity 校验后才接受 protobuf `Hello`。Windows access token 包含用户 SID 与 logon SID，`OpenProcessToken`/`GetTokenInformation` 是读取这些信息的官方入口。[Access tokens](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-tokens) [GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid) [GetNamedPipeServerProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid)

byte-mode pipe 不假设一次 `WriteFile` 对应一次 `ReadFile`；frame decoder 必须支持 header/payload partial read。内部可使用锁定版本 Tokio named-pipe support 或最小 Windows bindings，但必须保留上述原生 flags/ACL，而不是接受 crate 默认值。[Tokio named pipe](https://docs.rs/tokio/latest/tokio/net/windows/named_pipe/)

## 7. macOS transport

endpoint 固定放在短路径私有目录：`/tmp/slcap-<uid>-<security-session-id>-p<major>/host.sock`，同目录还有 `leader.lock`。双方通过 `SessionGetInfo(callerSecuritySession, …)` 取得当前 macOS login security session ID；Apple 明确建议用该 ID 区分不同登录会话中的 session-specific resources。[Apple login sessions](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPMultipleUsers/Concepts/SystemContexts.html) [SessionGetInfo](https://developer.apple.com/documentation/security/sessiongetinfo%28_%3A_%3A_%3A%29)

- 创建目录时使用 `0700`，随后 `lstat/fstat` 验证 owner 是当前 euid、对象是 directory、不是 symlink，mode 不比 `0700` 宽；任何不匹配都 fail closed，不删除陌生对象。
- socket 使用 `AF_UNIX`、`SOCK_STREAM`、nonblocking，bind 后 `chmod 0600`。Apple 将 `SOCK_STREAM` 定义为可靠、有序的双向 byte stream。[Apple socket(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/socket.2.html)
- leader 对 `leader.lock` 持有 exclusive `flock`；该锁只是 cooperating Host 的启动仲裁，不当成拒绝恶意进程的安全边界。Apple 文档也明确 `flock` 是 advisory。[Apple flock(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/flock.2.html)
- accept 后 Host 与 SDK 都调用 `getpeereid`，要求 peer euid 等于自身 effective uid；失败或不匹配立即关闭。Apple 说明该 credential 来自 connect/listen 时的有效身份，peer 无法直接伪造返回值。[Apple getpeereid](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/getpeereid.3.html)
- stale socket 只有在获得 exclusive lock、`lstat` 证明 owner/mode/type 正确、connect 明确失败后才 unlink；绝不跟随 symlink。
- 首版只承诺非 App-Sandbox 的 GitHub/Developer-ID 桌面宿主。需要 App Sandbox container/app-group 的第三方宿主必须另开协议/entitlement ticket，不把 UDS 失败静默改成 TCP loopback。

`sun_path` 长度较短，因此不用任意 Application Support 路径作为 socket name；实现必须在 bind 前检查编码后的 path 长度。Rust runtime 可用 `UnixStream`，但 peer credential、mode、stale cleanup 仍由 macOS transport Seam 负责。[Tokio UnixStream](https://docs.rs/tokio/latest/tokio/net/struct.UnixStream.html)

## 8. Framing、schema 与协议协商

### 8.1 固定 framing

每个 frame 为：

```text
offset  size  field
0       4     magic = "SLCP"
4       2     framing_version = 1, little-endian
6       2     frame_type, little-endian
8       4     payload_length, little-endian
12      N     payload
```

- control protobuf payload 最大 1,048,576 bytes。
- `ResultChunk` 的 bytes 最大 1,048,000，保证完整 frame 不超过 control limit。
- 一份 PNG 总长最大 134,217,728 bytes（128 MiB）；宽高各最大 32,768，且必须满足非零和实现的 checked arithmetic。
- length、type、reserved value、message order 或 protobuf decode 非法时立即关闭连接；不尝试扫描 magic 重新同步。
- named pipe/UDS 已提供可靠传输，不增加自制加密或 CRC；PNG stream 以总长度、连续 offset、`ResultEnd` 和 PNG 自身结构校验完整性。
- decoder 在分配前先验证长度；每个连接最多一个 active request 和有限 control queue，防止无界内存。

### 8.2 proto3 schema 形状

`.proto` 是 wire contract 的唯一权威输入，放在 Apache `sdk/protocol`，生成代码不得手工分叉。核心消息如下：

```proto
message Hello {
  uint32 protocol_major = 1;
  uint32 min_protocol_minor = 2;
  uint32 max_protocol_minor = 3;
  bytes client_nonce = 4;            // exactly 32 bytes
  string sdk_semver = 5;
  uint64 requested_capabilities = 6;
}

message Welcome {
  uint32 protocol_major = 1;
  uint32 negotiated_protocol_minor = 2;
  bytes echoed_client_nonce = 3;
  bytes connection_id = 4;           // exactly 16 random bytes
  string host_semver = 5;
  uint64 capabilities = 6;
  uint32 max_frame_bytes = 7;
  uint64 max_png_bytes = 8;
}

message StartCapture {
  bytes request_id = 1;              // exactly 16 random bytes on wire
  ClipboardMode clipboard_mode = 2; // 0 = DEFAULT_ENABLED
  optional uint64 interaction_timeout_ms = 3;
  CaptureOrigin origin = 4;          // APP or SDK
}

message CaptureAccepted { bytes request_id = 1; }
message CaptureRejected { bytes request_id = 1; StableError error = 2; }
message CancelCapture { bytes request_id = 1; }
message CancelAcknowledged { bytes request_id = 1; }

message ResultBegin {
  bytes request_id = 1;
  uint64 png_length = 2;
  uint32 pixel_width = 3;
  uint32 pixel_height = 4;
  ClipboardOutcome clipboard = 5;
}
message ResultChunk { bytes request_id = 1; uint64 offset = 2; bytes data = 3; }
message ResultEnd { bytes request_id = 1; }
message CaptureCanceled { bytes request_id = 1; CancelSource source = 2; }
message CaptureFailed { bytes request_id = 1; StableError error = 2; bool retryable = 3; }
```

`Envelope` 另含 `connection_id`、单调递增 `sequence` 和上述 body 的 `oneof`。Hello/Welcome 之前 connection ID 为空；Welcome 之后不匹配的 ID、重复/倒退 sequence、未知 request ID 或非法 oneof 都是 protocol violation。

`origin` 只选择功能等价合同中的 App 输出语义或 SDK 默认剪贴板语义，不是 authorization claim，也不授予额外能力。公共 C SDK 硬编码 `SDK`，Snaploom App client 硬编码 `APP`，两者均不向各自调用者暴露任意 origin。Host 仍对两者使用同一 Session、UI、renderer、权限和全局 gate。

Protocol Buffers 官方保证在遵守规则时旧代码可以忽略新增字段，并明确禁止复用 field number、改变既有 field type 或新增 required field。[Proto3 language guide](https://protobuf.dev/programming-guides/proto3/) [Proto best practices](https://protobuf.dev/best-practices/dos-donts/) 本项目额外规定：

- 所有 enum 的 0 值是明确的 `UNSPECIFIED` 或安全默认；clipboard 的 0 特意定义为默认写入，不能用模糊 bool 反转语义。
- 新字段只用全新 tag 且必须是 optional/additive；删除 tag/name 和 enum number 必须 `reserved`。
- 不依赖 protobuf serialization 的逐字节稳定性，不把 encoded control message 当签名、hash 或持久化格式。
- schema 与 golden wire fixtures 一起版本控制；生成器升级必须证明旧 fixtures 仍可读。

### 8.3 三个版本轴

| 版本 | 用途 | 兼容规则 |
| --- | --- | --- |
| SDK semver | 对开发者发布的 C API/wrappers/package | SemVer 2.0：兼容新增升 minor，修复升 patch，破坏 public API 升 major |
| C ABI major | 动态库 symbols/struct layout | v1 只 append；breaking symbol/layout/ownership 改为 `_v2`，允许过渡期并存 |
| IPC major/minor | SDK 与 Host wire | major 表示 breaking，minor 只允许 additive schema/capability；endpoint 按 major 隔离 |

SemVer 要求先声明 public API，并用 major/minor/patch 分别表达破坏、兼容新增和兼容修复。[Semantic Versioning 2.0.0](https://semver.org/)

握手选择相同 major 下 `min(client.max, host.max)` 的最高 minor，且结果不得低于双方 min；否则 `PROTOCOL_INCOMPATIBLE`。required capability 缺失同样拒绝，不以“字段能 decode”冒充语义兼容。首版 Host 只需支持 protocol major 1；同一 Release 必须发布匹配 SDK/Host，但 1.x minor 滚动升级仍必须通过双向兼容矩阵。

## 9. Capture Session、Busy、取消和终态

### 9.1 全局 gate

Host 是唯一 gate owner。状态机为：

```text
Idle -> Preparing -> Interactive -> Finalizing -> Terminal cleanup -> Idle
```

- `Idle` 才能原子接受 `StartCapture`；设置 gate 后才发 `CaptureAccepted`。
- 其他 App/SDK 请求立即收到 `CaptureRejected(BUSY)`，不排队、不抢焦点、不闪烁或覆盖现有浮层。
- App client 把 `BUSY` 转成合同规定的静默忽略；SDK client 把它映射成一次 `Failed(BUSY)` callback。
- gate 只在 overlay、capture buffers、renderer/cache 和临时状态释放后回到 `Idle`，防止下一请求看见旧资源。
- 发起 client 断开时 Host 取消它拥有的 active Session；其他 client 断开不影响当前 Session。

### 9.2 恰好一次 callback

SDK 为每个成功入队的 local request 保存 atomic terminal slot：

1. Host 正常终态占用 slot；重复终态被丢弃并记录稳定 protocol event。
2. transport EOF/crash、local deadline、client destroy 也只能 CAS 占用同一 slot并合成一个终态。
3. callback 被投递后才从 request table 移除；`client_destroy` 等待 callback 返回。
4. partial PNG 永不产生 `Completed`。
5. `start` 在返回 `OK` 前预分配 terminal record 和不含 PNG 的 fallback completion；因此后续 OOM 仍能投递一次 `Failed(OUT_OF_MEMORY)`，而不是破坏 callback 承诺。

“恰好一次”是 SDK 对调用者的进程内承诺，不声称分布式 exactly-once side effect：Host 可能已写剪贴板后连接才断开，此时 SDK 仍返回 `HOST_CRASHED/TRANSPORT_FAILED`，而且绝不自动重放截图。

### 9.3 取消竞态

- request 尚未发出：SDK 本地结束为 `Canceled(CALLER)`，不启动 Host。
- 已发出但未 accepted：记录 cancel intent；一旦 accepted 立即发送 cancel，若先 rejected 则 rejection 赢。
- 已 interactive：Host ack cancel intent，关闭 UI、清理资源后发 `CaptureCanceled`。
- 已开始不可逆 finalization：若最终 PNG/clipboard commit 已完成，则 `Completed` 赢；否则 cancel 可赢。
- 重复 cancel 在 request 未终态时返回 `OK`；completion 已从 table 移除后返回 `NOT_FOUND`。

`cancel` 是请求，不是同步确认；语言 wrappers 的 Task/async cancellation 只有收到 terminal callback 后才真正结束。

### 9.4 crash、EOF 与 timeout

| 情况 | 当前请求 | 后续请求 |
| --- | --- | --- |
| Host 在 accepted 前 crash/EOF | `Failed(HOST_CRASHED)`，不猜测是否开始 | 可重新发现/启动新 Host |
| Host 在交互或 result stream 中 crash | 清零 partial bytes，`Failed(HOST_CRASHED)` | 不自动 replay；新调用可重启 |
| framing/protobuf/order 错误 | `Failed(PROTOCOL_ERROR)`，关闭整个 connection | 新 connection 仍需重新握手 |
| launch timeout | `Failed(HOST_START_TIMEOUT)` | 不后台继续无限等待 |
| handshake timeout | `Failed(HANDSHAKE_TIMEOUT)` | 关闭未经确认的 peer |
| opt-in interaction deadline | 发 cancel；2 秒 grace 后仍无终态则断开并 `Failed(REQUEST_TIMEOUT)` | Host 由 disconnect 清理；新请求重新连接 |
| 默认交互 | 没有总 deadline，等待真人完成/取消 | transport/Host process death 仍立即可见 |

Host 启动、握手、frame read 和取消 grace 都有界；真人截图编辑默认无界，避免把思考/IME/保存对话框误报为 Host hang。

## 10. Capture Result、PNG 与默认剪贴板

Host 完成顺序固定为：

1. 提交可提交的文字草稿，用统一 renderer 生成 8-bit sRGB、无私有 metadata 的最终 PNG。
2. 验证物理宽高和 PNG bytes；同一 allocation 成为后续所有输出的事实来源。
3. SDK mode 的 clipboard mode 默认为 enabled。Host 把同一 bytes 写入 Windows 注册 `PNG` format 或 macOS PNG pasteboard type；不重新编码。App mode 仍严格遵循 `OUT-01～03`：完成动作复制，单独保存动作不新增剪贴板 side effect。
4. 剪贴板失败时浮层保持，显示合同规定的可恢复错误，允许用户重试或取消；不得发送 `ResultBegin`。
5. 保存成功、绿色完成、Enter 或双击等终止性成功动作都返回同一 Capture Result；`Command/Ctrl+C` 只是复制并继续编辑，不产生 terminal result。
6. `ResultBegin` 声明总长/物理尺寸，chunks 必须从 offset 0 连续、无重叠、无空洞，`ResultEnd` 后长度必须精确相等。
7. SDK 校验 PNG signature、IHDR 宽高与声明一致、上限和 checked allocation 后才构造 completion。任一失败返回 `INVALID_RESULT` 并清零 buffer。

禁用默认剪贴板用 `SNAPLOOM_CAPTURE_FLAG_DISABLE_CLIPBOARD`；不是另一个“无剪贴板 SDK”。Host 在 Result 中报告 `WRITTEN` 或 `DISABLED`，SDK 将它映射到 completion flags。Capture Result 永远是物理像素尺寸，不返回 BGRA、文件路径、平台 image handle 或保存目标。

## 11. 稳定错误模型

同步 `snaploom_status_v1` 只描述 C 调用本身：

| 数值 | status | 含义 |
| ---: | --- | --- |
| 0 | `OK` | 调用已同步完成或异步请求承诺 callback |
| 1 | `INVALID_ARGUMENT` | pointer、UTF-8、flag、timeout 非法 |
| 2 | `INVALID_STRUCT_SIZE` | 结构小于 v1 最小尺寸 |
| 3 | `CLIENT_CLOSED` | client 已进入 destroy |
| 4 | `CALLBACK_CONTEXT` | 在禁止的 callback 上下文调用 destroy |
| 5 | `NOT_FOUND` | cancel 的 request 已终态/未知 |
| 6 | `OUT_OF_MEMORY` | 无法建立本地 request/completion allocation |
| 255 | `INTERNAL` | ABI wrapper 内部失败；不得附 raw message |

异步 stable errors：

| 数值 | error | 语义/恢复 |
| ---: | --- | --- |
| 0 | `NONE` | 非 Failed 终态的安全默认值 |
| 1 | `BUSY` | 全局已有 Capture Session；不排队，调用者稍后发起新请求 |
| 2～4 | `HOST_NOT_FOUND` / `HOST_START_FAILED` / `HOST_START_TIMEOUT` | 安装/启动问题；不自动下载或换 binary |
| 5 | `AUTHENTICATION_FAILED` | peer identity、endpoint owner/mode 或 bootstrap challenge 不符；fail closed |
| 6～7 | `PROTOCOL_INCOMPATIBLE` / `PROTOCOL_ERROR` | 版本/capability/消息非法；不降级旧协议 |
| 8～9 | `HOST_CRASHED` / `TRANSPORT_FAILED` | 请求结果未知或连接中断；当前请求不 replay |
| 10～11 | `HANDSHAKE_TIMEOUT` / `REQUEST_TIMEOUT` | 有界基础设施/显式 caller deadline |
| 20 | `PLATFORM_UNAVAILABLE` | 对应平台捕获不可用 |
| 21～22 | `PERMISSION_NOT_GRANTED` / `PERMISSION_REVOKED` | macOS 权限引导；不绕过 TCC |
| 23～25 | `DISPLAY_UNAVAILABLE` / `CAPTURE_UNAVAILABLE` / `CAPTURE_TIMEOUT` | Host 平台捕获失败；释放 gate 后可重试 |
| 26 | `PIXEL_CONVERSION_FAILED` | 不返回半帧 |
| 30～31 | `INVALID_RESULT` / `RESULT_TOO_LARGE` | 不返回 partial PNG |
| 32 | `CLIPBOARD_WRITE_FAILED` | Host UI 保留 Session 供重试；若最终取消则 callback 为 Canceled |
| 40 | `OUT_OF_MEMORY` | 使用 start 时预分配的 fallback completion 收口，不破坏一次 callback 承诺 |
| 41 | `CLIENT_CLOSED` | destroy 合成终态；不再接受新请求 |
| 255 | `INTERNAL` | 稳定通用失败，不泄露 exception/error message |

`completion_kind` 同样固定为 0=`UNSPECIFIED`、1=`COMPLETED`、2=`CANCELED`、3=`FAILED`；v1 的 `SNAPLOOM_CAPTURE_FLAG_DISABLE_CLIPBOARD` 为 bit 0，completion 的 `CLIPBOARD_WRITTEN` 与 `RETRYABLE` 分别为 bit 0、bit 1。错误、kind 和 flag 数字一经发布不得改义或复用；删除值必须保留编号。`retryable` 只说明“新请求可能成功”，绝不表示 SDK 会自动重试。

## 12. 安全与隐私边界

### 12.1 请求鉴权

steady-state 鉴权由 OS peer identity 完成：Windows DACL + logon SID + process token，macOS directory/socket mode + `getpeereid`。Hello 的 client nonce、Welcome echo、connection ID 与 sequence 用于绑定握手、发现 stale/crossed connection，不把公开 nonce 当 password。

本设计的威胁边界是“拒绝网络、其他用户和其他登录会话”，不承诺抵抗同一用户权限下的恶意进程、管理员/root、进程注入或已攻陷宿主。任何把 same-user IPC 描述为强 sandbox 都是不准确的。需要抵抗同用户恶意代码时，必须引入可部署的 code-identity/entitlement broker 并重开 ADR；不能偷偷在配置文件保存一个所有同用户进程都可读的 bearer token。

### 12.2 输入和资源限制

- Host 最多同时接受 32 个 authenticated connections；每 connection 最多 32 个尚未收到 Host 终态的 request、64 个 queued control frames、1 MiB frame。Host 仍只接受其中一个全局 Session，其余立即 `BUSY`；这样同一 client 的并发 start 也由唯一 Host gate 判定，而不是由进程内近似状态抢答。
- 只接受 schema 定义的截图、取消和 health messages；SDK 不允许调用者传 shell argv、任意 Host command、保存路径、WebView command 或平台 handle。
- request ID/nonce 使用 OS CSPRNG；request ID 在 wire 上 128-bit，C API 的 `uint64_t` 只是单 client 进程内 opaque 映射。
- 所有长度、offset、宽高和乘法使用 checked arithmetic；超过上限先拒绝再分配。
- Host executable locator、endpoint、PID 和底层 OS code 可以用于 allowlisted 诊断，但日志不得包含完整用户路径。

### 12.3 截图数据

- PNG 只存在于 Host/SDK memory、OS IPC buffers、用户明确选择的保存文件和默认剪贴板；不创建 IPC temp file，不保留历史，不上传。
- SDK/Host 日志只记录 request outcome、稳定 error、匿名尺寸、耗时和版本；禁止 PNG、标注文字、窗口标题、clipboard、完整路径、protobuf dump、NSError/Win32 raw message 和 stack。
- Host 发送完成后清零其临时 result buffer；SDK completion free、partial receive、decode failure、cancel 和 crash 路径清零 client buffer。
- crash dump 可能包含进程内 PNG，发布配置默认不主动生成/上传自有 dump；系统级 crash report 风险必须在隐私文档中披露，不读取或上传 dump。

## 13. 实现模块与依赖约束

建议的深 Module 分割：

| Module | 许可证 | 责任 |
| --- | --- | --- |
| `sdk/protocol` | Apache-2.0 | `.proto`、framing、版本/错误/capability、golden fixtures |
| `sdk/c-abi` | Apache-2.0 | 7-symbol ABI、client state、callback executor、Host locator/launcher、transport adapters |
| `apps/capture-host/ipc` | GPL（依赖 Apache protocol） | endpoint、peer auth、wire state、全局 gate bridge |
| `apps/capture-host/session` | GPL | Capture Session/use cases，唯一终态和资源释放 |
| `sdk/dotnet` / `sdk/swift` / `sdk/cpp` | Apache-2.0 | 只封装 C ABI，不重复 IPC/Host discovery |

可锁定的候选依赖是 `prost`/`prost-build`（protobuf）与私有 I/O runtime 的最小 Tokio feature set；Windows security flags 仍由最小 windows-rs bindings 设置，macOS credential/mode 用系统 API。具体版本由 #30 统一锁定，必须通过 Apache SDK 的更窄许可证 allowlist、SBOM、NOTICE 和二进制大小审计，不因本文链接当前 docs.rs 版本而提前锁版。[prost documentation](https://docs.rs/prost/latest/prost/) [Tokio named pipe](https://docs.rs/tokio/latest/tokio/net/windows/named_pipe/) [Tokio UnixStream](https://docs.rs/tokio/latest/tokio/net/struct.UnixStream.html)

SDK 的 runtime 必须私有：不要求宿主已有 Tokio/Tauri/runtime，不占用宿主 main thread，不设置进程级 panic hook、signal handler、COM apartment 或 logging subscriber。Windows/macOS 平台初始化都在 SDK 自身线程完成并成对释放。

## 14. 验证矩阵

### 14.1 ABI 与 wrappers

- 用 MSVC C/C++、Clang C/C++ 编译 public header；开启最高 warning，把 `sizeof/alignof/offsetof` 与 Rust mirror 对照。
- 导出符号 allowlist；Windows DLL/import lib、macOS dylib/XCFramework architecture 与 calling convention 检查。
- old header + new 1.x library、new header + old 1.x library 双向矩阵，覆盖短/长 `struct_size`、unknown flags、zero defaults、`out_request_id == NULL` 的无取消常用路径。
- C、C++ RAII、C# `SafeHandle`/delegate GC、Swift callback/Data wrapper 各跑 start/cancel/destroy/completion-free；ASan/Valgrind 或平台等价工具检查 leak/UAF/double-free。
- callback reentrancy、并发 start/cancel/destroy、callback 阻塞和 callback 内错误 destroy 的确定性测试。
- FFI fuzz：null、misaligned pointer（安全拒绝可验证部分）、最大 length、非法 UTF-8、panic injection；任何路径不 unwind 过 C。

### 14.2 Wire 与兼容性

- 每种 v1 message 的 golden binary fixture；Rust generated code 重建后读取旧 fixture。
- protocol minor `old SDK ↔ new Host`、`new SDK ↔ old Host`；capability 缺失和 major 不同必须明确拒绝。
- fuzz header/protobuf/oneof/order/sequence/request ID/chunk offset/length；分配上限在 decode 前生效。
- partial read/write 每个 byte boundary、connection close 在每个 frame/result chunk boundary、duplicate terminal、late cancel。
- schema lint 禁止复用/reserved tag、改变 field type、增加 required/default 语义反转；codegen 后要求 clean diff。

### 14.3 Host 发现、鉴权与并发

- Host missing、override 非绝对/非法 UTF-8、world-writable、版本不兼容、启动 crash、readiness challenge 错误。
- 20 个进程同时 launch，只产生一个 leader/一个 endpoint/一个 UI；loser Host 安全退出。
- Windows：default ACL 禁止门禁、remote client reject、不同 logon session/user 拒绝、client/server PID token match、named-pipe first-instance race。
- macOS：目录/socket mode、wrong owner、symlink、stale inode、lock race、不同 uid `getpeereid` 拒绝、路径长度上限。
- 明确的 same-user adversary 测试用于记录威胁边界，不伪造“可阻止同用户 malware”的结果。

### 14.4 Capture Session 语义

- App 与多个 SDK 在同一时刻 start：恰好一个 `Accepted`，SDK losers 为 `Busy`，App losers 静默；不出现排队后的意外浮层。
- cancel 在 local queue、Hello、Start、Accepted、Interactive、Finalizing、每个 ResultChunk、ResultEnd 的竞态。
- Host crash/kill/abort 在上述每个状态；每个成功 `start` 恰好一次 callback、无 partial PNG、下次请求可重启。
- client process crash/disconnect 使其 Session 取消并释放 gate；无关 client 断开不影响 active Session。
- client destroy 等待 callbacks、返回后零 callback；callback 内 destroy 不死锁。
- 默认交互无 deadline，显式 deadline、launch/handshake/cancel grace 使用 fake clock 精确验证。

### 14.5 PNG、剪贴板与性能

- 1080p、4K、5K Retina、随机不可压缩 PNG 和接近 128 MiB 上限；chunk working set 有界，超限在 allocation 前拒绝。
- SDK 返回 bytes、Host clipboard bytes、用户保存 bytes 逐字节相同；IHDR 宽高等于物理像素，8-bit sRGB 且无私有 metadata。
- 默认 clipboard、显式 disable、clipboard retry/failure、用户取消；失败不发送 result、Session 可恢复。
- result stream 中断后 buffer 清零；completion free 后敏感 allocation 清零的 instrumentation test。
- 至少 1000 次 start/Busy/cancel 与 100 次 5K result，检查 handles/fds/threads/allocations 回到基线。
- IPC/SDK 开销单独计量，不得让 Host 的 4K PNG P95 超过功能合同 1 秒门槛；若 stream 成为实测瓶颈，先提交数据和 ADR，再评估 shared memory，不能先保留隐形 fallback。

### 14.6 分发与许可

- SDK archives、NuGet、Swift package/XCFramework 扫描确认不含 GPL Host、Tauri、capture-core 或平台 Adapter symbols。
- Host 与 SDK 资产使用相同 semver/tag，但有独立 LICENSE/NOTICE/SBOM/checksum；Host locator 指向单独安装资产。
- 对应源码包含 `.proto`、生成器输入、C header、build scripts 与完整 Host source；release 门禁验证 schema/ABI 版本与资产一致。
- #33 法律复核必须看实际 installer、Host locator、IPC schema 和第三方再分发说明，而不是只看架构图。

## 15. 明确失败降级

| 失败 | 唯一允许的降级 |
| --- | --- |
| named pipe / UDS 不可用 | 稳定 transport error；不切 TCP、temp file 或 shared memory |
| Host 未安装 | `HOST_NOT_FOUND`；不下载、不从 PATH 猜测 |
| Host/SDK major 不兼容 | `PROTOCOL_INCOMPATIBLE`；并行安装新 major 由未来发布票设计 |
| endpoint owner/peer identity 异常 | `AUTHENTICATION_FAILED` 并 fail closed |
| 已有 Capture Session | `BUSY`；SDK 不排队、不抢占，App 静默忽略 |
| Host crash / connection EOF | 当前请求失败且不 replay；下一新请求可重启 |
| cancel 未及时完成 | 到 opt-in deadline/grace 后断开，失败终态；不杀共享 Host |
| window catalog 部分失败 | 由 Host Adapter 退化为少量/无候选，仍可整屏选择；IPC 不感知平台对象 |
| clipboard 失败 | Host 保留会话供用户重试/取消；不返回“成功但未复制” |
| PNG 超限/非法/中途断开 | 清零 partial buffer并失败；不落 temp file、不返回路径 |
| 调用者 callback 抛异常/longjmp/panic 越过 ABI | 属于调用约定违规，不定义恢复行为；官方 wrappers 必须在语言 thunk 内捕获并正常返回 |

## 16. 实施顺序

1. 在 Apache workspace 建立 `.proto`、稳定 error/capability registry、framing codec、golden fixtures 与 schema lint。
2. 建立手写权威 C header、Rust `#[repr(C)]` mirror、7-symbol export、ABI layout/symbol tests 和 completion allocator/free。
3. 实现 SDK 私有 I/O/callback executors、request table、exactly-once terminal slot、cancel/destroy fake transport tests。
4. 实现 Windows named pipe 与 macOS UDS transport，包括 endpoint leader election、peer auth、partial I/O 和 stale cleanup。
5. 实现 Host locator/secure spawn/bootstrap readiness、并发 launch 与 idle lifecycle。
6. 在 GPL Host 接入 protocol server 和唯一 `CaptureSessionGate`，先用 fake Session 验证 Busy/cancel/crash。
7. 接真实 Capture Session、统一 renderer、默认 clipboard 和 bounded PNG chunk stream；完成 exact-bytes 真机矩阵。
8. 完成 C++/C#/Swift wrappers，只绑定 C ABI；跑跨版本、并发、fuzz、压力、包内容和许可证门禁。
9. 由 #31 固化独立 Host/SDK 资产、installer registration 与原子 Release；由 #33 对实际边界做发布前法律复核。

任何后续实现若要增加共享内存、临时文件、TCP、自动重放、请求排队、额外 ABI symbols 或 Host-in-SDK packaging，必须先用测量/需求更新本决策并新建 ADR，不能作为内部“优化”静默加入。

## 17. 一手资料

- Rust FFI 与 representation：[Rustonomicon FFI](https://doc.rust-lang.org/nomicon/ffi.html)、[Alternative representations](https://doc.rust-lang.org/nomicon/other-reprs.html)
- Windows named pipe：[CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea)、[security and access rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)、[type/read modes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-type-read-and-wait-modes)
- Windows peer/process security：[GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)、[GetNamedPipeServerProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid)、[access tokens](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-tokens)
- Windows process startup：[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)、[handle inheritance](https://learn.microsoft.com/en-us/windows/win32/procthread/inheritance)、[BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom)
- Apple local IPC/process APIs：[login sessions](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPMultipleUsers/Concepts/SystemContexts.html)、[SessionGetInfo](https://developer.apple.com/documentation/security/sessiongetinfo%28_%3A_%3A_%3A%29)、[socket(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/socket.2.html)、[socketpair(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/socketpair.2.html)、[getpeereid(3)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/getpeereid.3.html)、[flock(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/flock.2.html)、[posix_spawn(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/posix_spawn.2.html)、[SecRandomCopyBytes](https://developer.apple.com/documentation/security/secrandomcopybytes%28_%3A_%3A_%3A%29)
- Protocol Buffers：[proto3 language guide](https://protobuf.dev/programming-guides/proto3/)、[best practices](https://protobuf.dev/best-practices/dos-donts/)、[limits](https://protobuf.dev/programming-guides/proto-limits/)
- Versioning：[Semantic Versioning 2.0.0](https://semver.org/)
- Rust IPC/runtime source docs：[Tokio Windows named pipe](https://docs.rs/tokio/latest/tokio/net/windows/named_pipe/)、[Tokio UnixStream](https://docs.rs/tokio/latest/tokio/net/struct.UnixStream.html)、[prost](https://docs.rs/prost/latest/prost/)
