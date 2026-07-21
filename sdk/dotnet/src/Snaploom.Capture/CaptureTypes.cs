namespace Snaploom.Capture;

/// <summary>Options used when creating a capture client.</summary>
public sealed class CaptureClientOptions
{
    /// <summary>An optional absolute path to a separately installed Capture Host.</summary>
    public string? HostExecutableOverride { get; init; }

    /// <summary>An optional Host launch timeout. Native defaults apply when omitted.</summary>
    public TimeSpan? LaunchTimeout { get; init; }

    /// <summary>An optional Host handshake timeout. Native defaults apply when omitted.</summary>
    public TimeSpan? HandshakeTimeout { get; init; }
}

/// <summary>Options for one interactive capture.</summary>
public sealed class CaptureOptions
{
    /// <summary>Disables the default clipboard write when true.</summary>
    public bool DisableClipboard { get; init; }

    /// <summary>An optional interaction timeout. No deadline is imposed when omitted.</summary>
    public TimeSpan? InteractionTimeout { get; init; }
}

/// <summary>The loaded native SDK version.</summary>
/// <param name="AbiMajor">The C ABI major version.</param>
/// <param name="NativeSemver">The native SDK semantic version.</param>
public sealed record CaptureSdkVersion(uint AbiMajor, string NativeSemver);

/// <summary>An owned capture result. PNG bytes no longer reference native memory.</summary>
/// <param name="Png">The encoded PNG bytes.</param>
/// <param name="PixelWidth">The physical pixel width.</param>
/// <param name="PixelHeight">The physical pixel height.</param>
/// <param name="ClipboardWritten">Whether the same PNG bytes were written to the clipboard.</param>
public sealed record CaptureResult(
    ReadOnlyMemory<byte> Png,
    uint PixelWidth,
    uint PixelHeight,
    bool ClipboardWritten);

/// <summary>Stable errors reported asynchronously by the Capture SDK.</summary>
public enum CaptureErrorCode : uint
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>Another global capture session is active.</summary>
    Busy = 1,
    /// <summary>The separately installed Capture Host was not found.</summary>
    HostNotFound = 2,
    /// <summary>The Capture Host could not be started.</summary>
    HostStartFailed = 3,
    /// <summary>Starting the Capture Host timed out.</summary>
    HostStartTimeout = 4,
    /// <summary>Peer authentication failed.</summary>
    AuthenticationFailed = 5,
    /// <summary>The Host and SDK protocol versions are incompatible.</summary>
    ProtocolIncompatible = 6,
    /// <summary>The Host sent an invalid protocol message.</summary>
    ProtocolError = 7,
    /// <summary>The Capture Host exited during the request.</summary>
    HostCrashed = 8,
    /// <summary>The local IPC transport failed.</summary>
    TransportFailed = 9,
    /// <summary>The authenticated handshake timed out.</summary>
    HandshakeTimeout = 10,
    /// <summary>The overall request timed out.</summary>
    RequestTimeout = 11,
    /// <summary>Capture is unavailable on the current platform.</summary>
    PlatformUnavailable = 20,
    /// <summary>The required screen recording permission was not granted.</summary>
    PermissionNotGranted = 21,
    /// <summary>A previously granted permission was revoked.</summary>
    PermissionRevoked = 22,
    /// <summary>The requested display is no longer available.</summary>
    DisplayUnavailable = 23,
    /// <summary>The platform capture service is unavailable.</summary>
    CaptureUnavailable = 24,
    /// <summary>Platform frame capture timed out.</summary>
    CaptureTimeout = 25,
    /// <summary>Captured pixels could not be converted.</summary>
    PixelConversionFailed = 26,
    /// <summary>The returned image violated result invariants.</summary>
    InvalidResult = 30,
    /// <summary>The returned image exceeded the 128 MiB contract limit.</summary>
    ResultTooLarge = 31,
    /// <summary>Writing the final PNG to the clipboard failed.</summary>
    ClipboardWriteFailed = 32,
    /// <summary>The native SDK could not allocate memory.</summary>
    OutOfMemory = 40,
    /// <summary>The client was already closed.</summary>
    ClientClosed = 41,
    /// <summary>An internal native error occurred.</summary>
    Internal = 255,
}

/// <summary>Base exception for Capture SDK protocol and native status failures.</summary>
public class CaptureSdkException : Exception
{
    internal CaptureSdkException(string message, uint? nativeStatus = null, Exception? innerException = null)
        : base(message, innerException)
    {
        NativeStatus = nativeStatus;
    }

    /// <summary>The raw synchronous native status, when the failure came from a C call.</summary>
    public uint? NativeStatus { get; }
}

/// <summary>An asynchronous capture failure with a stable raw error value.</summary>
public sealed class CaptureException : CaptureSdkException
{
    internal CaptureException(CaptureErrorCode errorCode, bool isRetryable, string diagnosticName)
        : base($"Snaploom capture failed: {diagnosticName} ({(uint)errorCode}).")
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    /// <summary>The stable error value. Unknown future values remain representable.</summary>
    public CaptureErrorCode ErrorCode { get; }

    /// <summary>Whether a later, newly initiated request may succeed.</summary>
    public bool IsRetryable { get; }
}

/// <summary>The user canceled from the Capture Host UI rather than through a caller token.</summary>
public sealed class CaptureCanceledException : CaptureSdkException
{
    internal CaptureCanceledException()
        : base("The capture was canceled in the Snaploom Capture Host.")
    {
    }
}

/// <summary>A synchronous argument or ABI layout status reported by native code.</summary>
public sealed class CaptureArgumentException : ArgumentException
{
    internal CaptureArgumentException(uint nativeStatus)
        : base($"The Snaploom Capture SDK rejected an argument (native status {nativeStatus}).")
    {
        NativeStatus = nativeStatus;
    }

    /// <summary>The raw synchronous native status.</summary>
    public uint NativeStatus { get; }
}
