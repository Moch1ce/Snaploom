import CSnaploomCapture
import Foundation
import XCTest

@testable import SnaploomCapture

final class CaptureClientTests: XCTestCase {
  func testAuthoritativeABILayoutAndVersion() throws {
    XCTAssertEqual(MemoryLayout<snaploom_utf8_view_v1>.size, 16)
    XCTAssertEqual(MemoryLayout<snaploom_capture_version_info_v1>.size, 56)
    XCTAssertEqual(MemoryLayout<snaploom_capture_client_config_v1>.size, 64)
    XCTAssertEqual(MemoryLayout<snaploom_capture_options_v1>.size, 48)
    XCTAssertEqual(MemoryLayout<snaploom_capture_completion_v1>.size, 80)

    let native = FakeNativeCaptureAPI()
    let client = try CaptureClient(native: native)
    XCTAssertEqual(client.runtimeVersion, CaptureSDKVersion(abiMajor: 1, nativeSemver: "0.1.0"))
    XCTAssertTrue(native.createdWithCanonicalEmptyHostView)
    try client.close()
    try client.close()
    XCTAssertEqual(native.destroyCount, 1)
  }

  func testCompletionCopiesDataAndFreesNativeStorageOnce() async throws {
    let native = FakeNativeCaptureAPI()
    let client = try CaptureClient(native: native)
    let capture = Task { try await client.capture() }
    await native.waitUntilStarted()

    native.complete(
      kind: NativeConstants.completionCompleted,
      png: [137, 80, 78, 71],
      width: 1,
      height: 1,
      flags: NativeConstants.clipboardWritten
    )
    let result = try await capture.value
    XCTAssertEqual(result.pngData, Data([137, 80, 78, 71]))
    XCTAssertEqual(result.pixelWidth, 1)
    XCTAssertEqual(result.pixelHeight, 1)
    XCTAssertTrue(result.clipboardWritten)
    XCTAssertEqual(native.freeCount, 1)
    XCTAssertEqual(native.bytesObservedAfterZero, [0, 0, 0, 0])
    try client.close()
  }

  func testCancellationIntentBeforeRequestIDWaitsForNativeTerminal() async throws {
    let native = FakeNativeCaptureAPI(blockStartBeforePublishingID: true)
    let client = try CaptureClient(native: native)
    let capture = Task { try await client.capture() }
    await native.waitUntilStartEntered()

    capture.cancel()
    native.releaseStart()
    await native.waitUntilCancel(requestID: 42)
    XCTAssertFalse(capture.isCancelled && native.freeCount > 0)

    native.complete(kind: NativeConstants.completionCanceled)
    do {
      _ = try await capture.value
      XCTFail("caller cancellation must throw")
    } catch is CancellationError {
      // Native terminal confirmed the caller's cancellation intent.
    }
    XCTAssertEqual(native.cancelledRequestIDs, [42])
    XCTAssertEqual(native.freeCount, 1)
    try client.close()
  }

  func testUserCancellationIsDistinctFromTaskCancellation() async throws {
    let native = FakeNativeCaptureAPI()
    let client = try CaptureClient(native: native)
    let capture = Task { try await client.capture() }
    await native.waitUntilStarted()
    native.complete(kind: NativeConstants.completionCanceled)

    do {
      _ = try await capture.value
      XCTFail("Host cancellation must throw")
    } catch let error as CaptureError {
      XCTAssertEqual(error, .canceledByUser)
    }
    try client.close()
  }

  func testUnknownFailureValueAndRetryableFlagArePreserved() async throws {
    let native = FakeNativeCaptureAPI()
    let client = try CaptureClient(native: native)
    let capture = Task { try await client.capture() }
    await native.waitUntilStarted()
    native.complete(
      kind: NativeConstants.completionFailed,
      errorCode: 4_242,
      flags: NativeConstants.retryable
    )

    do {
      _ = try await capture.value
      XCTFail("failure completion must throw")
    } catch let CaptureError.failure(failure) {
      XCTAssertEqual(failure.code.rawValue, 4_242)
      XCTAssertEqual(failure.code.diagnosticName, "UNKNOWN")
      XCTAssertTrue(failure.isRetryable)
    }
    try client.close()
  }

  func testCloseDoesNotHoldClientLockAcrossDestroyCallbackWait() async throws {
    let native = FakeNativeCaptureAPI(destroyWaitsForCompletion: true)
    let client = try CaptureClient(native: native)
    let capture = Task { try await client.capture() }
    await native.waitUntilStarted()

    let close = Task.detached { try client.close() }
    await native.waitUntilDestroyEntered()
    native.complete(
      kind: NativeConstants.completionCompleted,
      png: [1, 2, 3],
      width: 1,
      height: 1
    )
    _ = try await capture.value
    try await close.value
    XCTAssertEqual(native.destroyCount, 1)
    XCTAssertEqual(native.freeCount, 1)
  }

  func testStartFailureDoesNotPromiseCallback() async throws {
    let native = FakeNativeCaptureAPI(startStatus: NativeConstants.statusInvalidArgument)
    let client = try CaptureClient(native: native)
    do {
      _ = try await client.capture()
      XCTFail("start failure must throw synchronously through the continuation")
    } catch let error as CaptureError {
      XCTAssertEqual(error, .invalidArgument(status: NativeConstants.statusInvalidArgument))
    }
    XCTAssertEqual(native.freeCount, 0)
    try client.close()
  }
}

private final class FakeNativeCaptureAPI: NativeCaptureAPI, @unchecked Sendable {
  private let lock = NSLock()
  private let versionBytes: UnsafeMutablePointer<UInt8>
  private let startGate = DispatchSemaphore(value: 0)
  private let destroyGate = DispatchSemaphore(value: 0)
  private let blockStartBeforePublishingID: Bool
  private let destroyWaitsForCompletion: Bool
  private let startStatus: UInt32
  private var startEntered = false
  private var started = false
  private var destroyEntered = false
  private var callback: NativeCaptureCallback?
  private var userData: UnsafeMutableRawPointer?
  private var pngAllocations: [UInt: (UnsafeMutablePointer<UInt8>, Int)] = [:]
  private(set) var cancelledRequestIDs: [UInt64] = []
  private(set) var freeCount = 0
  private(set) var destroyCount = 0
  private(set) var bytesObservedAfterZero: [UInt8] = []
  private(set) var createdWithCanonicalEmptyHostView = false

  init(
    blockStartBeforePublishingID: Bool = false,
    destroyWaitsForCompletion: Bool = false,
    startStatus: UInt32 = NativeConstants.statusOK
  ) {
    self.blockStartBeforePublishingID = blockStartBeforePublishingID
    self.destroyWaitsForCompletion = destroyWaitsForCompletion
    self.startStatus = startStatus
    let bytes = Array("0.1.0".utf8)
    versionBytes = .allocate(capacity: bytes.count)
    versionBytes.initialize(from: bytes, count: bytes.count)
  }

  deinit {
    versionBytes.deallocate()
  }

  func version(_ output: UnsafeMutablePointer<snaploom_capture_version_info_v1>) -> UInt32 {
    output.pointee.struct_size = UInt32(MemoryLayout<snaploom_capture_version_info_v1>.size)
    output.pointee.abi_major = 1
    output.pointee.sdk_semver.data = UnsafePointer(versionBytes)
    output.pointee.sdk_semver.length = 5
    return NativeConstants.statusOK
  }

  func create(
    _ config: UnsafePointer<snaploom_capture_client_config_v1>?,
    _ output: UnsafeMutablePointer<OpaquePointer?>
  ) -> UInt32 {
    if let config {
      createdWithCanonicalEmptyHostView =
        config.pointee.host_executable_override.data == nil
        && config.pointee.host_executable_override.length == 0
    }
    output.pointee = OpaquePointer(bitPattern: 1)
    return NativeConstants.statusOK
  }

  func start(
    _ client: OpaquePointer,
    _ options: UnsafePointer<snaploom_capture_options_v1>?,
    _ callback: NativeCaptureCallback,
    _ userData: UnsafeMutableRawPointer,
    _ requestID: UnsafeMutablePointer<UInt64>
  ) -> UInt32 {
    _ = client
    _ = options
    lock.lock()
    startEntered = true
    self.callback = callback
    self.userData = userData
    lock.unlock()
    if blockStartBeforePublishingID {
      startGate.wait()
    }
    guard startStatus == NativeConstants.statusOK else {
      return startStatus
    }
    requestID.pointee = 42
    lock.lock()
    started = true
    lock.unlock()
    return NativeConstants.statusOK
  }

  func cancel(_ client: OpaquePointer, _ requestID: UInt64) -> UInt32 {
    _ = client
    lock.lock()
    cancelledRequestIDs.append(requestID)
    lock.unlock()
    return NativeConstants.statusOK
  }

  func destroy(_ client: OpaquePointer) -> UInt32 {
    _ = client
    lock.lock()
    destroyCount += 1
    destroyEntered = true
    lock.unlock()
    if destroyWaitsForCompletion {
      destroyGate.wait()
    }
    return NativeConstants.statusOK
  }

  func freeCompletion(_ completion: UnsafeMutablePointer<snaploom_capture_completion_v1>) {
    let png = completion.pointee.png_data
    let key = UInt(bitPattern: completion)
    lock.lock()
    let allocation = pngAllocations.removeValue(forKey: key)
    freeCount += 1
    lock.unlock()
    if let allocation {
      allocation.0.initialize(repeating: 0, count: allocation.1)
      lock.lock()
      bytesObservedAfterZero = Array(
        UnsafeBufferPointer(start: allocation.0, count: allocation.1)
      )
      lock.unlock()
      allocation.0.deallocate()
    } else {
      XCTAssertNil(png)
    }
    completion.deinitialize(count: 1)
    completion.deallocate()
    destroyGate.signal()
  }

  func releaseStart() {
    startGate.signal()
  }

  func complete(
    kind: UInt32,
    png: [UInt8] = [],
    width: UInt32 = 0,
    height: UInt32 = 0,
    errorCode: UInt32 = 0,
    flags: UInt32 = 0
  ) {
    lock.lock()
    let callback = self.callback
    let userData = self.userData
    self.callback = nil
    self.userData = nil
    lock.unlock()
    guard let callback, let userData else {
      XCTFail("completion requires one accepted request")
      return
    }
    let completion = UnsafeMutablePointer<snaploom_capture_completion_v1>.allocate(capacity: 1)
    completion.initialize(to: snaploom_capture_completion_v1())
    completion.pointee.struct_size = UInt32(MemoryLayout<snaploom_capture_completion_v1>.size)
    completion.pointee.kind = kind
    completion.pointee.error_code = errorCode
    completion.pointee.flags = flags
    completion.pointee.request_id = 42
    completion.pointee.pixel_width = width
    completion.pointee.pixel_height = height
    if !png.isEmpty {
      let bytes = UnsafeMutablePointer<UInt8>.allocate(capacity: png.count)
      bytes.initialize(from: png, count: png.count)
      completion.pointee.png_data = UnsafePointer(bytes)
      completion.pointee.png_size = UInt64(png.count)
      lock.lock()
      pngAllocations[UInt(bitPattern: completion)] = (bytes, png.count)
      lock.unlock()
    }
    callback(OpaquePointer(bitPattern: 1), completion, userData)
  }

  func waitUntilStartEntered() async {
    await waitUntil { self.withLock { self.startEntered } }
  }

  func waitUntilStarted() async {
    await waitUntil { self.withLock { self.started } }
  }

  func waitUntilCancel(requestID: UInt64) async {
    await waitUntil { self.withLock { self.cancelledRequestIDs.contains(requestID) } }
  }

  func waitUntilDestroyEntered() async {
    await waitUntil { self.withLock { self.destroyEntered } }
  }

  private func withLock<T>(_ body: () -> T) -> T {
    lock.lock()
    defer { lock.unlock() }
    return body()
  }

  private func waitUntil(_ predicate: @escaping @Sendable () -> Bool) async {
    for _ in 0..<2_000 {
      if predicate() {
        return
      }
      try? await Task.sleep(for: .milliseconds(1))
    }
    XCTFail("timed out waiting for fake native state")
  }
}
