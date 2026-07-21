import CSnaploomCapture

internal typealias NativeCaptureCallback =
  @convention(c) (
    OpaquePointer?,
    UnsafeMutablePointer<snaploom_capture_completion_v1>?,
    UnsafeMutableRawPointer?
  ) -> Void

internal protocol NativeCaptureAPI: Sendable {
  func version(_ output: UnsafeMutablePointer<snaploom_capture_version_info_v1>) -> UInt32
  func create(
    _ config: UnsafePointer<snaploom_capture_client_config_v1>?,
    _ output: UnsafeMutablePointer<OpaquePointer?>
  ) -> UInt32
  func start(
    _ client: OpaquePointer,
    _ options: UnsafePointer<snaploom_capture_options_v1>?,
    _ callback: NativeCaptureCallback,
    _ userData: UnsafeMutableRawPointer,
    _ requestID: UnsafeMutablePointer<UInt64>
  ) -> UInt32
  func cancel(_ client: OpaquePointer, _ requestID: UInt64) -> UInt32
  func destroy(_ client: OpaquePointer) -> UInt32
  func freeCompletion(_ completion: UnsafeMutablePointer<snaploom_capture_completion_v1>)
}

internal struct SystemNativeCaptureAPI: NativeCaptureAPI {
  internal func version(
    _ output: UnsafeMutablePointer<snaploom_capture_version_info_v1>
  ) -> UInt32 {
    snaploom_capture_version_v1(output)
  }

  internal func create(
    _ config: UnsafePointer<snaploom_capture_client_config_v1>?,
    _ output: UnsafeMutablePointer<OpaquePointer?>
  ) -> UInt32 {
    snaploom_capture_client_create_v1(config, output)
  }

  internal func start(
    _ client: OpaquePointer,
    _ options: UnsafePointer<snaploom_capture_options_v1>?,
    _ callback: NativeCaptureCallback,
    _ userData: UnsafeMutableRawPointer,
    _ requestID: UnsafeMutablePointer<UInt64>
  ) -> UInt32 {
    snaploom_capture_start_v1(client, options, callback, userData, requestID)
  }

  internal func cancel(_ client: OpaquePointer, _ requestID: UInt64) -> UInt32 {
    snaploom_capture_cancel_v1(client, requestID)
  }

  internal func destroy(_ client: OpaquePointer) -> UInt32 {
    snaploom_capture_client_destroy_v1(client)
  }

  internal func freeCompletion(
    _ completion: UnsafeMutablePointer<snaploom_capture_completion_v1>
  ) {
    snaploom_capture_completion_free_v1(completion)
  }
}

internal enum NativeConstants {
  static let statusOK: UInt32 = 0
  static let statusInvalidArgument: UInt32 = 1
  static let statusInvalidStructSize: UInt32 = 2
  static let statusClientClosed: UInt32 = 3
  static let statusOutOfMemory: UInt32 = 6
  static let abiMajor: UInt32 = 1
  static let packageSemverMajor: UInt32 = 0
  static let disableClipboard: UInt32 = 1
  static let completionCompleted: UInt32 = 1
  static let completionCanceled: UInt32 = 2
  static let completionFailed: UInt32 = 3
  static let clipboardWritten: UInt32 = 1
  static let retryable: UInt32 = 2
  static let maximumPNGSize: UInt64 = 128 * 1024 * 1024
}
