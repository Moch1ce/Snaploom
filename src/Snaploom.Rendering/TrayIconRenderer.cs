using SkiaSharp;

namespace Snaploom.Rendering;

public static class TrayIconRenderer
{
    private static readonly int[] WindowsIconSizes = [16, 32, 48, 64, 256];
    private static readonly (string FileName, int PixelSize)[] MacOSIconAssets =
    [
        ("icon_16x16.png", 16),
        ("icon_16x16@2x.png", 32),
        ("icon_32x32.png", 32),
        ("icon_32x32@2x.png", 64),
        ("icon_128x128.png", 128),
        ("icon_128x128@2x.png", 256),
        ("icon_256x256.png", 256),
        ("icon_256x256@2x.png", 512),
        ("icon_512x512.png", 512),
        ("icon_512x512@2x.png", 1024),
    ];

    public static byte[] RenderPng(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);

        var imageInfo = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var inset = size * 0.0625f;
        using var backgroundPaint = new SKPaint
        {
            Color = new SKColor(25, 31, 48),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(
            new SKRect(inset, inset, size - inset, size - inset),
            size * 0.21875f,
            size * 0.21875f,
            backgroundPaint);

        using var framePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = Math.Max(1.5f, size * 0.09375f),
        };

        var outer = size * 0.25f;
        var inner = size * 0.4375f;
        var farOuter = size - outer;
        var farInner = size - inner;

        canvas.DrawLine(outer, inner, outer, outer, framePaint);
        canvas.DrawLine(outer, outer, inner, outer, framePaint);
        canvas.DrawLine(farInner, outer, farOuter, outer, framePaint);
        canvas.DrawLine(farOuter, outer, farOuter, inner, framePaint);
        canvas.DrawLine(outer, farInner, outer, farOuter, framePaint);
        canvas.DrawLine(outer, farOuter, inner, farOuter, framePaint);
        canvas.DrawLine(farInner, farOuter, farOuter, farOuter, framePaint);
        canvas.DrawLine(farOuter, farOuter, farOuter, farInner, framePaint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    public static byte[] RenderWindowsIco()
    {
        var images = new byte[WindowsIconSizes.Length][];
        for (var index = 0; index < images.Length; index++)
        {
            images[index] = RenderPng(WindowsIconSizes[index]);
        }

        const int headerLength = 6;
        const int entryLength = 16;
        var imageOffset = headerLength + (entryLength * images.Length);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        for (var index = 0; index < images.Length; index++)
        {
            var size = WindowsIconSizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[index].Length);
            writer.Write(imageOffset);
            imageOffset += images[index].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        return stream.ToArray();
    }

    public static IReadOnlyDictionary<string, byte[]> RenderMacOSIconset()
    {
        var renderedSizes = new Dictionary<int, byte[]>();
        var iconset = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var (fileName, pixelSize) in MacOSIconAssets)
        {
            if (!renderedSizes.TryGetValue(pixelSize, out var png))
            {
                png = RenderPng(pixelSize);
                renderedSizes.Add(pixelSize, png);
            }

            iconset.Add(fileName, png);
        }

        return iconset;
    }
}
