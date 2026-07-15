using SkiaSharp;

namespace Snaploom.Rendering;

public static class TrayIconRenderer
{
    private static readonly int[] WindowsIconSizes = [16, 32, 48, 64, 256];

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
}
