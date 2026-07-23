using System.Security.Cryptography;

namespace Snaploom.Core;

public enum CapturedPixelFormat
{
    Bgra8888PremultipliedSrgb,
}

public readonly record struct CapturedColor(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
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

    public PhysicalPoint ToPhysicalPoint(LogicalPoint point) =>
        new(
            checked((int)Math.Round(point.X * ScaleX)),
            checked((int)Math.Round(point.Y * ScaleY)));

    public ReadOnlyMemory<byte> Pixels =>
        _pixels ?? throw new ObjectDisposedException(nameof(CapturedFrame));

    public CapturedColor SamplePixel(PhysicalPoint point)
    {
        if (point.X < 0 || point.X >= PhysicalSize.Width ||
            point.Y < 0 || point.Y >= PhysicalSize.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                point,
                "The sample point must be inside the captured frame.");
        }

        var pixels = Pixels.Span;
        var offset = checked((point.Y * Stride) + (point.X * 4));
        var alpha = pixels[offset + 3];
        return new CapturedColor(
            Unpremultiply(pixels[offset + 2], alpha),
            Unpremultiply(pixels[offset + 1], alpha),
            Unpremultiply(pixels[offset], alpha));
    }

    public void Dispose()
    {
        var pixels = Interlocked.Exchange(ref _pixels, null);
        if (pixels is not null)
        {
            CryptographicOperations.ZeroMemory(pixels);
        }
    }

    private static byte Unpremultiply(byte component, byte alpha)
    {
        if (alpha == byte.MaxValue)
        {
            return component;
        }

        if (alpha == 0)
        {
            return 0;
        }

        return (byte)Math.Min(
            byte.MaxValue,
            ((component * byte.MaxValue) + (alpha / 2)) / alpha);
    }
}
