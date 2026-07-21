import CSnaploomCapture
import Foundation

/// A Swift concurrency wrapper for the separately installed Snaploom Capture Host.
public final class CaptureClient: @unchecked Sendable {
  private let state: ClientState

  /// The ABI and semantic version reported by the loaded native SDK.
  public let runtimeVersion: CaptureSDKVersion

  public convenience init(options: CaptureClientOptions = .init()) throws {
    try self.init(native: SystemNativeCaptureAPI(), options: options)
  }

  internal init(
    native: any NativeCaptureAPI,
    options: CaptureClientOptions = .init()
  ) throws {
    runtimeVersion = try Self.queryRuntimeVersion(native: native)
    let client = try Self.createClient(native: native, options: options)
    state = ClientState(native: native, client: client)
  }

  deinit {
    try? state.close()
  }

  /// Starts one capture and resumes only after the native terminal callback.
  public func capture(options: CaptureOptions = .init()) async throws -> CaptureResult {
    let box = RequestBox(native: state.native) { [state] requestID in
      state.requestCancel(requestID: requestID)
    }

    return try await withTaskCancellationHandler {
      try await withCheckedThrowingContinuation {
        (continuation: CheckedContinuation<CaptureResult, any Error>) in
        box.install(continuation: continuation)
        let retained = Unmanaged.passRetained(box)
        let userData = retained.toOpaque()
        var nativeOptions = snaploom_capture_options_v1()
        nativeOptions.struct_size = UInt32(
          MemoryLayout<snaploom_capture_options_v1>.size
        )
        nativeOptions.flags =
          options.disableClipboard
          ? NativeConstants.disableClipboard
          : 0
        nativeOptions.interaction_timeout_ms =
          options.interactionTimeoutMilliseconds ?? 0
        var requestID: UInt64 = 0
        let status = state.start(
          options: &nativeOptions,
          callback: captureCallback,
          userData: userData,
          requestID: &requestID
        )
        if status == NativeConstants.statusOK {
          box.publish(requestID: requestID)
        } else {
          retained.release()
          box.failStart(error: Self.error(for: status))
        }
      }
    } onCancel: {
      box.requestCancel()
    }
  }

  /// Closes the client. This may block while accepted native callbacks finish.
  public func close() throws {
    try state.close()
  }

  private static func queryRuntimeVersion(
    native: any NativeCaptureAPI
  ) throws -> CaptureSDKVersion {
    var version = snaploom_capture_version_info_v1()
    version.struct_size = UInt32(
      MemoryLayout<snaploom_capture_version_info_v1>.size
    )
    let status = native.version(&version)
    guard status == NativeConstants.statusOK else {
      throw error(for: status)
    }
    guard
      version.struct_size >= MemoryLayout<snaploom_capture_version_info_v1>.size,
      version.abi_major == NativeConstants.abiMajor,
      let data = version.sdk_semver.data,
      version.sdk_semver.length > 0,
      version.sdk_semver.length <= 256,
      let count = Int(exactly: version.sdk_semver.length)
    else {
      throw CaptureError.incompatibleNative(reason: "ABI version information is invalid")
    }
    let semver = String(decoding: UnsafeBufferPointer(start: data, count: count), as: UTF8.self)
    guard
      let majorText = semver.split(separator: ".", maxSplits: 1).first,
      let major = UInt32(majorText),
      major == NativeConstants.packageSemverMajor
    else {
      throw CaptureError.incompatibleNative(reason: "native package major is incompatible")
    }
    return CaptureSDKVersion(abiMajor: version.abi_major, nativeSemver: semver)
  }

  private static func createClient(
    native: any NativeCaptureAPI,
    options: CaptureClientOptions
  ) throws -> OpaquePointer {
    if let path = options.hostExecutableOverride,
      !(path as NSString).isAbsolutePath
    {
      throw CaptureError.invalidArgument(status: NativeConstants.statusInvalidArgument)
    }
    var config = snaploom_capture_client_config_v1()
    config.struct_size = UInt32(
      MemoryLayout<snaploom_capture_client_config_v1>.size
    )
    config.launch_timeout_ms = options.launchTimeoutMilliseconds ?? 0
    config.handshake_timeout_ms = options.handshakeTimeoutMilliseconds ?? 0
    let hostBytes = Array((options.hostExecutableOverride ?? "").utf8)
    var client: OpaquePointer?
    let status = hostBytes.withUnsafeBufferPointer { bytes in
      config.host_executable_override.data = bytes.isEmpty ? nil : bytes.baseAddress
      config.host_executable_override.length = UInt64(bytes.count)
      return native.create(&config, &client)
    }
    guard status == NativeConstants.statusOK else {
      throw error(for: status)
    }
    guard let client else {
      throw CaptureError.incompatibleNative(reason: "native create returned a null client")
    }
    return client
  }

  internal static func error(for status: UInt32) -> CaptureError {
    switch status {
    case NativeConstants.statusInvalidArgument,
      NativeConstants.statusInvalidStructSize:
      .invalidArgument(status: status)
    case NativeConstants.statusClientClosed:
      .clientClosed
    case NativeConstants.statusOutOfMemory:
      .outOfMemory
    default:
      .nativeStatus(rawValue: status)
    }
  }
}

private let captureCallback: NativeCaptureCallback = { _, completion, userData in
  guard let userData else {
    return
  }
  let box = Unmanaged<RequestBox>.fromOpaque(userData).takeRetainedValue()
  box.complete(completion: completion)
}

private final class ClientState: @unchecked Sendable {
  private let lock = NSLock()
  fileprivate let native: any NativeCaptureAPI
  private var client: OpaquePointer?

  fileprivate init(native: any NativeCaptureAPI, client: OpaquePointer) {
    self.native = native
    self.client = client
  }

  fileprivate func start(
    options: UnsafePointer<snaploom_capture_options_v1>,
    callback: NativeCaptureCallback,
    userData: UnsafeMutableRawPointer,
    requestID: UnsafeMutablePointer<UInt64>
  ) -> UInt32 {
    lock.lock()
    defer { lock.unlock() }
    guard let client else {
      return NativeConstants.statusClientClosed
    }
    return native.start(client, options, callback, userData, requestID)
  }

  fileprivate func requestCancel(requestID: UInt64) {
    lock.lock()
    defer { lock.unlock() }
    guard let client else {
      return
    }
    _ = native.cancel(client, requestID)
  }

  fileprivate func close() throws {
    let closing: OpaquePointer?
    lock.lock()
    closing = client
    client = nil
    lock.unlock()
    guard let closing else {
      return
    }
    let status = native.destroy(closing)
    guard status == NativeConstants.statusOK else {
      lock.lock()
      if client == nil {
        client = closing
      }
      lock.unlock()
      throw CaptureClient.error(for: status)
    }
  }
}

private final class RequestBox: @unchecked Sendable {
  private let lock = NSLock()
  private let native: any NativeCaptureAPI
  private let cancel: @Sendable (UInt64) -> Void
  private var continuation: CheckedContinuation<CaptureResult, any Error>?
  private var requestID: UInt64?
  private var cancelRequested = false
  private var terminal = false

  fileprivate init(
    native: any NativeCaptureAPI,
    cancel: @escaping @Sendable (UInt64) -> Void
  ) {
    self.native = native
    self.cancel = cancel
  }

  fileprivate func install(
    continuation: CheckedContinuation<CaptureResult, any Error>
  ) {
    lock.lock()
    self.continuation = continuation
    lock.unlock()
  }

  fileprivate func publish(requestID: UInt64) {
    let shouldCancel: Bool
    lock.lock()
    if terminal {
      lock.unlock()
      return
    }
    self.requestID = requestID
    shouldCancel = cancelRequested
    lock.unlock()
    if shouldCancel {
      cancel(requestID)
    }
  }

  fileprivate func requestCancel() {
    let published: UInt64?
    lock.lock()
    cancelRequested = true
    published = terminal ? nil : requestID
    lock.unlock()
    if let published {
      cancel(published)
    }
  }

  fileprivate func failStart(error: CaptureError) {
    let waiting = finish()
    waiting.continuation?.resume(throwing: error)
  }

  fileprivate func complete(
    completion: UnsafeMutablePointer<snaploom_capture_completion_v1>?
  ) {
    guard let completion else {
      let waiting = finish()
      waiting.continuation?.resume(
        throwing: CaptureError.invalidCompletion(reason: "native completion is null")
      )
      return
    }
    defer { native.freeCompletion(completion) }

    let waiting = finish()
    guard let continuation = waiting.continuation else {
      return
    }
    do {
      continuation.resume(
        returning: try decode(
          completion: completion.pointee,
          callerCanceled: waiting.cancelRequested
        )
      )
    } catch {
      continuation.resume(throwing: error)
    }
  }

  private func finish() -> (
    continuation: CheckedContinuation<CaptureResult, any Error>?,
    cancelRequested: Bool
  ) {
    lock.lock()
    defer { lock.unlock() }
    guard !terminal else {
      return (nil, cancelRequested)
    }
    terminal = true
    let waiting = continuation
    continuation = nil
    return (waiting, cancelRequested)
  }

  private func decode(
    completion: snaploom_capture_completion_v1,
    callerCanceled: Bool
  ) throws -> CaptureResult {
    guard completion.struct_size >= MemoryLayout<snaploom_capture_completion_v1>.size else {
      throw CaptureError.invalidCompletion(reason: "native completion layout is too short")
    }
    switch completion.kind {
    case NativeConstants.completionCompleted:
      guard
        completion.png_size > 0,
        completion.png_size <= NativeConstants.maximumPNGSize,
        let png = completion.png_data,
        let count = Int(exactly: completion.png_size),
        completion.pixel_width > 0,
        completion.pixel_height > 0
      else {
        throw CaptureError.invalidCompletion(reason: "completed PNG metadata is invalid")
      }
      return CaptureResult(
        pngData: Data(bytes: png, count: count),
        pixelWidth: completion.pixel_width,
        pixelHeight: completion.pixel_height,
        clipboardWritten: completion.flags & NativeConstants.clipboardWritten != 0
      )
    case NativeConstants.completionCanceled:
      if callerCanceled {
        throw CancellationError()
      }
      throw CaptureError.canceledByUser
    case NativeConstants.completionFailed:
      throw CaptureError.failure(
        CaptureFailure(
          code: CaptureErrorCode(rawValue: completion.error_code),
          isRetryable: completion.flags & NativeConstants.retryable != 0
        )
      )
    default:
      throw CaptureError.unknownCompletion(rawValue: completion.kind)
    }
  }
}
