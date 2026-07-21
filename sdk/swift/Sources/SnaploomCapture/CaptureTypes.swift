import Foundation

/// Options used when creating a capture client.
public struct CaptureClientOptions: Sendable, Equatable {
  /// Optional absolute path to a separately installed Capture Host.
  public var hostExecutableOverride: String?

  /// Optional Host launch timeout in milliseconds. Native defaults apply when omitted.
  public var launchTimeoutMilliseconds: UInt32?

  /// Optional Host handshake timeout in milliseconds. Native defaults apply when omitted.
  public var handshakeTimeoutMilliseconds: UInt32?

  public init(
    hostExecutableOverride: String? = nil,
    launchTimeoutMilliseconds: UInt32? = nil,
    handshakeTimeoutMilliseconds: UInt32? = nil
  ) {
    self.hostExecutableOverride = hostExecutableOverride
    self.launchTimeoutMilliseconds = launchTimeoutMilliseconds
    self.handshakeTimeoutMilliseconds = handshakeTimeoutMilliseconds
  }
}

/// Options for one interactive capture.
public struct CaptureOptions: Sendable, Equatable {
  /// Disables the default clipboard write when true.
  public var disableClipboard: Bool

  /// Optional interaction timeout in milliseconds. Native defaults apply when omitted.
  public var interactionTimeoutMilliseconds: UInt64?

  public init(
    disableClipboard: Bool = false,
    interactionTimeoutMilliseconds: UInt64? = nil
  ) {
    self.disableClipboard = disableClipboard
    self.interactionTimeoutMilliseconds = interactionTimeoutMilliseconds
  }
}

/// The ABI and semantic version reported by the loaded native SDK.
public struct CaptureSDKVersion: Sendable, Equatable {
  public let abiMajor: UInt32
  public let nativeSemver: String

  public init(abiMajor: UInt32, nativeSemver: String) {
    self.abiMajor = abiMajor
    self.nativeSemver = nativeSemver
  }
}

/// An owned capture result. PNG bytes no longer reference native memory.
public struct CaptureResult: Sendable, Equatable {
  public let pngData: Data
  public let pixelWidth: UInt32
  public let pixelHeight: UInt32
  public let clipboardWritten: Bool

  public init(
    pngData: Data,
    pixelWidth: UInt32,
    pixelHeight: UInt32,
    clipboardWritten: Bool
  ) {
    self.pngData = pngData
    self.pixelWidth = pixelWidth
    self.pixelHeight = pixelHeight
    self.clipboardWritten = clipboardWritten
  }
}

/// A forward-compatible stable native error value.
public struct CaptureErrorCode: RawRepresentable, Sendable, Hashable {
  public let rawValue: UInt32

  public init(rawValue: UInt32) {
    self.rawValue = rawValue
  }

  public static let none = Self(rawValue: 0)
  public static let busy = Self(rawValue: 1)
  public static let hostNotFound = Self(rawValue: 2)
  public static let hostStartFailed = Self(rawValue: 3)
  public static let hostStartTimeout = Self(rawValue: 4)
  public static let authenticationFailed = Self(rawValue: 5)
  public static let protocolIncompatible = Self(rawValue: 6)
  public static let protocolError = Self(rawValue: 7)
  public static let hostCrashed = Self(rawValue: 8)
  public static let transportFailed = Self(rawValue: 9)
  public static let handshakeTimeout = Self(rawValue: 10)
  public static let requestTimeout = Self(rawValue: 11)
  public static let platformUnavailable = Self(rawValue: 20)
  public static let permissionNotGranted = Self(rawValue: 21)
  public static let permissionRevoked = Self(rawValue: 22)
  public static let displayUnavailable = Self(rawValue: 23)
  public static let captureUnavailable = Self(rawValue: 24)
  public static let captureTimeout = Self(rawValue: 25)
  public static let pixelConversionFailed = Self(rawValue: 26)
  public static let invalidResult = Self(rawValue: 30)
  public static let resultTooLarge = Self(rawValue: 31)
  public static let clipboardWriteFailed = Self(rawValue: 32)
  public static let outOfMemory = Self(rawValue: 40)
  public static let clientClosed = Self(rawValue: 41)
  public static let `internal` = Self(rawValue: 255)

  /// A stable diagnostic name. Unknown future values remain representable.
  public var diagnosticName: String {
    switch self {
    case .none: "NONE"
    case .busy: "BUSY"
    case .hostNotFound: "HOST_NOT_FOUND"
    case .hostStartFailed: "HOST_START_FAILED"
    case .hostStartTimeout: "HOST_START_TIMEOUT"
    case .authenticationFailed: "AUTHENTICATION_FAILED"
    case .protocolIncompatible: "PROTOCOL_INCOMPATIBLE"
    case .protocolError: "PROTOCOL_ERROR"
    case .hostCrashed: "HOST_CRASHED"
    case .transportFailed: "TRANSPORT_FAILED"
    case .handshakeTimeout: "HANDSHAKE_TIMEOUT"
    case .requestTimeout: "REQUEST_TIMEOUT"
    case .platformUnavailable: "PLATFORM_UNAVAILABLE"
    case .permissionNotGranted: "PERMISSION_NOT_GRANTED"
    case .permissionRevoked: "PERMISSION_REVOKED"
    case .displayUnavailable: "DISPLAY_UNAVAILABLE"
    case .captureUnavailable: "CAPTURE_UNAVAILABLE"
    case .captureTimeout: "CAPTURE_TIMEOUT"
    case .pixelConversionFailed: "PIXEL_CONVERSION_FAILED"
    case .invalidResult: "INVALID_RESULT"
    case .resultTooLarge: "RESULT_TOO_LARGE"
    case .clipboardWriteFailed: "CLIPBOARD_WRITE_FAILED"
    case .outOfMemory: "OUT_OF_MEMORY"
    case .clientClosed: "CLIENT_CLOSED"
    case .internal: "INTERNAL"
    default: "UNKNOWN"
    }
  }
}

/// An asynchronous native failure with its retryability contract preserved.
public struct CaptureFailure: Error, Sendable, Equatable {
  public let code: CaptureErrorCode
  public let isRetryable: Bool

  public init(code: CaptureErrorCode, isRetryable: Bool) {
    self.code = code
    self.isRetryable = isRetryable
  }
}

/// Synchronous SDK, ABI, cancellation, and result-validation failures.
public enum CaptureError: Error, Sendable, Equatable {
  case invalidArgument(status: UInt32)
  case clientClosed
  case outOfMemory
  case nativeStatus(rawValue: UInt32)
  case incompatibleNative(reason: String)
  case canceledByUser
  case failure(CaptureFailure)
  case invalidCompletion(reason: String)
  case unknownCompletion(rawValue: UInt32)
}
