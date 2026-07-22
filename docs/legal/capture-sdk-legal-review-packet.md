# Capture SDK 发布前法律复核事实包

状态：**工程事实与复核表已准备；首次稳定发布前必须由仓库所有者核对最终实物并明确接受未经过外部法律复核的风险。**

对应 Issue：[#33](https://github.com/moch1ce/Snaploom/issues/33)

> 本文是工程事实包和风险核对表，不是法律意见，也不得描述为“已经法务批准”。外部律师复核不再是发布门禁；仓库所有者必须填写第 9 节的风险接受记录。

## 1. 要复核的具体事实

Snaploom 计划在同一 GitHub Release 中发布彼此独立的两类作品：

1. `GPL-3.0-or-later` 的 Snaploom Desktop 与 Capture Host；Host 是可独立启动的本地交互式截图程序。
2. `Apache-2.0` 的 Capture SDK，包括公开协议、7-symbol C ABI、C/C++ header/wrapper、C# NuGet、Swift Package/XCFramework 与示例。

闭源宿主只在自己的进程内动态调用 Apache C SDK。SDK 通过版本化的本地 IPC 寻找并启动独立 GPL Host，提交“开始/取消交互式截图”请求，最终只接收 PNG bytes、物理像素尺寸、稳定终态和错误码。

这个结构的法律风险不由“两个进程”这一形式自动消除。GNU FAQ 将通信的形式与实质、耦合程度以及是否实际形成一个程序作为判断因素。复核必须针对下述实际产物和调用方式，不能只看架构图。

权威工程输入：

- [分层授权 ADR](../adr/0001-open-source-license-boundaries.md)
- [独立 Capture Host ADR](../adr/0003-isolate-capture-host.md)
- [许可证与依赖边界审计](../research/tauri-open-source-license-audit.md)
- [C ABI 与本地 IPC 决策](../research/capture-sdk-c-abi-ipc.md)
- [官方 SDK 封装与分发决策](../research/capture-sdk-wrappers-distribution.md)

## 2. 已固定的边界

### 2.1 代码与依赖方向

```text
Closed host -> Apache wrapper -> Apache C ABI/client/protocol
                                      |
                               versioned local IPC
                                      v
GPL Desktop/Host -> Apache protocol <- GPL capture/session/platform/UI
```

- Apache workspace 不得依赖、复制、生成自或链接 GPL product workspace、Tauri Host、Canvas UI 或 platform Adapter。
- GPL App/Host 可以单向依赖 Apache protocol/client。
- SDK 不暴露 Host 对象图、Rust trait、Tauri command、平台句柄、callback address 或可变共享内存。
- SDK 不实现截图、标注、Canvas、窗口目录、剪贴板或保存逻辑。

### 2.2 进程与协议

- Host 可不经闭源宿主或 SDK 独立启动。
- Windows 使用同登录会话 named pipe；macOS 使用同用户私有 UDS。不提供 TCP、网络服务或远程端点。
- 公开 proto3 schema 只包含版本/能力协商、start/cancel、Busy、终态、PNG chunks、尺寸和稳定错误。
- PNG 是最终服务结果，不是 Host 内部可变领域状态。没有 shared-memory object graph、plugin ABI 或函数级 RPC。
- Host 崩溃或连接中断只结束当前请求，SDK 不自动重放。

### 2.3 安装、发现和更新

- SDK 不捆绑、链接、自动下载或静默安装 Host。
- 官方 Host 由独立 GPL installer/DMG 安装，并用 Windows `HKCU` 安装登记或 macOS bundle identifier/Launch Services 发现。
- 开发者可显式传入 Host 绝对路径。SDK 不从 `PATH`、当前目录、临时目录或网络猜测 binary。
- App、Host 和 SDK 使用同一 release semver，但为独立产物。跨 minor 兼容由公开 IPC/ABI 协商提供，不把 Host 伪装成 SDK 的“资源文件”。

## 3. 实际发布物清单

仓库所有者必须在最终候选构建上核对此表；如另行咨询律师，应使用同一不可变候选。文件名可由发布设计票调整，但产物边界不变。

| 产物 | 授权 | 必须包含 | 必须不包含 |
| --- | --- | --- | --- |
| Desktop installer / DMG | GPL-3.0-or-later | Desktop、独立 Host、GPL 正文、对应源码指引、适用 NOTICE/SBOM | 闭源宿主或限制用户修改版的 DRM |
| Capture Host standalone asset | GPL-3.0-or-later | Host binary、GPL 正文、精确对应源码指引、SBOM | Apache SDK 许可声称覆盖 Host |
| C SDK archives | Apache-2.0 | header、DLL/lib 或 dylib、示例、LICENSE/NOTICE/SBOM | Host、Tauri、GPL capture/platform/UI object 或 symbol |
| C# NuGet | Apache-2.0 | managed assembly、win-x64/osx-arm64 native SDK、LICENSE/NOTICE/SBOM | Host installer/binary、下载器或 GPL 对象 |
| Swift Package/XCFramework | Apache-2.0 | Swift wrapper、arm64 C SDK、header/module map、LICENSE/NOTICE/SBOM | Host `.app`、Tauri、capture-core/platform code |
| Corresponding Source | GPL-3.0-or-later 范围 | 精确 tag 源码、协议/生成器输入、构建/安装/打包脚本、配置 | 签名私钥；与 binary 不对应的滚动 `main` |

## 4. 分发场景复核问题

请仓库所有者对每项给出“接受 / 需修改 / 不接受”和理由；如另行咨询律师，可将意见附到同一记录：

1. 闭源宿主仅链接 Apache SDK，用户另行安装 GPL Host，宿主从本地 IPC 只获得最终 PNG。
2. 闭源厂商在自己的 installer 中并排携带未修改 Host，并同时提供 Host 的 GPL 文本、精确对应源码与明确独立作品说明。
3. 闭源厂商修改并再分发 Host，对修改后 Host 履行 GPL 义务，但不将闭源宿主声称为 GPL。
4. 官方 GitHub Release 在一个页面上同时提供 GPL App/Host 和 Apache SDK，但使用不同下载文件、包内许可证、SBOM 和源码指引。
5. SDK 从平台安装登记发现 Host，或由宿主显式传入绝对路径；SDK 不下载 Host。
6. 官方文档将 Host 描述为独立的 GPL 程序，而不是“SDK runtime”、“内置组件”或被 Apache 重新授权的库。
7. 安装与平台信任机制允许用户运行自编译/修改的 Host；当前官方资产也不把发布者证书作为协议层的排他授权。

## 5. 对应源码与通知核对

最终候选构建必须附下列实物，而不是计划文档：

- 不可变 `vX.Y.Z` tag 与可重建的 corresponding-source archive。
- Cargo/pnpm/Swift/.NET 的精确锁定文件或恢复输入。
- `.proto`、C header、Windows bindings 生成输入与生成脚本。
- Tauri/Rust/TypeScript 构建、Windows unsigned installer、macOS stable-unsigned/ad hoc DMG 和验证脚本。
- GPL-3.0-or-later 和 Apache-2.0 完整文本、文件级 SPDX/REUSE 映射、准确 NOTICE 与第三方声明。
- 按实际产物分开的 SBOM，以及锁文件、SBOM、NOTICE 和解包内容的交叉检查。
- 用于构建和运行修改版的说明。证书私钥不需要提供，但不得因二进制被修改而在产品协议中禁止其正常本地使用。

## 6. 实物检查命令与证据

发布候选生成后，复核包必须附带以下命令的原始输出或 CI artifact：

```sh
# 稳定 C ABI 与 SDK 包边界
nm -gU libsnaploom_capture.dylib
dumpbin /exports snaploom_capture.dll
unzip -l Snaploom.Capture.*.nupkg
unzip -l CSnaploomCapture-*.xcframework.zip

# 平台二进制依赖与签名
otool -L <macOS-binary>
codesign --verify --deep --strict --verbose=2 <macOS-app-or-framework>
spctl --assess --type execute --verbose=4 <macOS-app>
dumpbin /dependents <windows-binary>

# 开源合规与软件物料
reuse lint
cargo deny check licenses
cyclonedx validate --input-file <sbom.cdx.json>
```

自动化还必须证明：

- SDK archive/NuGet/XCFramework 内不存在 Host binary、GPL product path、Tauri/capture-core/platform symbol 或 Host 自动下载逻辑。
- GPL binary 与 corresponding-source archive 的 tag/version/commit 一致。
- 每个产物的 LICENSE/NOTICE/SBOM 只描述其实际内容，不用“全仓通用声明”掩盖产物差异。
- Release 在所有产物、源码、checksums 与 SBOM 验证通过前保持 draft，不公开部分成功的 SDK。

## 7. 对外文案与禁用声称

对外文案使用 [Capture SDK 集成与再分发风险说明](./capture-sdk-integration-risk-disclosure.md)。对外页面、README、NuGet 和 Swift Package 不得出现以下绝对化声称：

- “IPC 会自动隔离 GPL”。
- “任何闭源应用都可以零风险内置 Host”。
- “Apache SDK 把 Capture Host 重新授权为 Apache-2.0”。
- “使用官方签名版 Host 是协议层的必要条件”。
- “项目方承担所有第三方再分发的合规责任”。

## 8. 工程复核状态

| 检查 | 当前状态 | 解除条件 |
| --- | --- | --- |
| C ABI/IPC 事实 | 已决策 | 实现后对照 binary/schema 复核 |
| SDK wrapper/package 事实 | 已决策 | 实现后解包复核 |
| 安装/发现/更新事实 | 自动化已实现，待最终实物 | 运行稳定 RC workflow 并核对最终 installer/DMG |
| Corresponding Source/NOTICE/SBOM | 自动化已实现，待最终实物 | 同一 RC workflow 原子汇总、重下载复验通过 |
| 闭源宿主集成走查 | 自动化已实现，待最终实物 | 稳定 RC 对最终 C/C++/C#/Swift 包逐项执行 package-only consumer |
| 所有者风险接受 | **阻塞** | 第 9 节完整填写并绑定最终 draft/checksum |
| 稳定 SDK publish | **禁止** | 上述全部通过；没有 bypass input |

稳定 RC 的生成、GitHub draft 重下载和复核包字段见
[稳定 RC draft 流程](../distribution/legal-rc-process.md)。该自动化不会填写本节，也没有 stable
publish 权限。所有者风险接受记录完成后，只能由
[受保护的稳定 Release 公开入口](../distribution/stable-release-process.md) 重新验证同一 draft 并执行一次
不可变转换。

## 9. 所有者发布批准记录

以下字段必须由仓库所有者本人填写，不允许 CI 或机器人代填：

```text
所有者姓名：
GitHub 用户名：
批准日期：
风险接受说明：
确认未经过外部法律复核：true
复核的 tag/commit：
复核的 Release candidate URL：
复核的产物 checksums：

结论：接受 / 有条件接受 / 不接受
必须修改项：
可公开的集成风险表述：
保留意见：
决策记录的 SHA-256 与受控存档位置：
签署：
```

如另有律师意见且含特权或个人信息，仓库只保留所有者认可的公开摘要、不可变文档哈希、日期、结论与受控存档位置；不把特权文件上传到 public repository。

## 10. 一手依据

- [GNU GPLv3 正文：aggregate 与 Corresponding Source](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU GPL FAQ：GPL 程序与专有系统](https://www.gnu.org/licenses/gpl-faq.html#GPLInProprietarySystem)
- [Apache License 2.0 正文：再分发与 NOTICE](https://www.apache.org/licenses/LICENSE-2.0)
- [Apache 对 GPLv3 兼容性的说明](https://www.apache.org/licenses/GPL-compatibility)
