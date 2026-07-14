using System.Security.Cryptography;

namespace Snaploom.Core;

public enum CapturedPixelFormat
{
    Bgra8888PremultipliedSrgb,
}

public sealed class CapturedFrame : IDisposable
{
    private byte[]? _pixels;

    public CapturedFrame(
        PhysicalSize physicalSize,
        LogicalSize logicalSize,
        int stride,
        byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var minimumStride = checked(physicalSize.Width * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, minimumStride);

        if (pixels.Length != checked(stride * physicalSize.Height))
        {
            throw new ArgumentException("The pixel buffer length does not match the frame dimensions.", nameof(pixels));
        }

        PhysicalSize = physicalSize;
        LogicalSize = logicalSize;
        Stride = stride;
        _pixels = pixels;
    }

    public PhysicalSize PhysicalSize { get; }

    public LogicalSize LogicalSize { get; }

    public int Stride { get; }

    public CapturedPixelFormat PixelFormat { get; } = CapturedPixelFormat.Bgra8888PremultipliedSrgb;

    public double ScaleX => PhysicalSize.Width / LogicalSize.Width;

    public double ScaleY => PhysicalSize.Height / LogicalSize.Height;

    public double DpiX => 96 * ScaleX;

    public double DpiY => 96 * ScaleY;

    public ReadOnlyMemory<byte> Pixels =>
        _pixels ?? throw new ObjectDisposedException(nameof(CapturedFrame));

    public void Dispose()
    {
        var pixels = Interlocked.Exchange(ref _pixels, null);
        if (pixels is not null)
        {
            CryptographicOperations.ZeroMemory(pixels);
        }
    }
}
