using System.Buffers.Binary;
using System.Text;
using SkiaSharp;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.Rendering.Tests;

public sealed class SelectionPngEncoderTests
{
    [Fact]
    public void EncodedPngUsesThePhysicalSelectionAndPreservesPixels()
    {
        var pixels = new byte[10 * 10 * 4];
        SetPixel(pixels, width: 10, x: 2, y: 3, red: 30, green: 20, blue: 10);
        using var frame = new CapturedFrame(
            new PhysicalSize(10, 10),
            new LogicalSize(5, 5),
            stride: 40,
            pixels);

        Assert.Equal(CapturedPixelFormat.Bgra8888PremultipliedSrgb, frame.PixelFormat);
        Assert.Equal(192, frame.DpiX);
        Assert.Equal(192, frame.DpiY);

        var png = SelectionPngEncoder.Encode(frame, new PhysicalRect(2, 3, 4, 5));

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(4, bitmap.Width);
        Assert.Equal(5, bitmap.Height);
        Assert.Equal(new SKColor(30, 20, 10, 255), bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void EncodedPngIsEightBitSrgbAndContainsNoPrivateMetadata()
    {
        using var frame = new CapturedFrame(
            new PhysicalSize(10, 10),
            new LogicalSize(10, 10),
            stride: 40,
            new byte[10 * 10 * 4]);

        var png = SelectionPngEncoder.Encode(frame, new PhysicalRect(1, 1, 8, 8));
        var chunks = ReadPngChunkTypes(png);

        Assert.Equal(8, png[24]);
        Assert.Contains(chunks, chunk => chunk is "sRGB" or "iCCP");
        Assert.DoesNotContain(chunks, chunk =>
            chunk is "eXIf" or "tEXt" or "zTXt" or "iTXt" or "tIME");
    }

    [Fact]
    public void EncodedPngCompositesAnnotationsRelativeToTheCroppedSelection()
    {
        var pixels = new byte[40 * 40 * 4];
        for (var alphaOffset = 3; alphaOffset < pixels.Length; alphaOffset += 4)
        {
            pixels[alphaOffset] = byte.MaxValue;
        }

        using var frame = new CapturedFrame(
            new PhysicalSize(40, 40),
            new LogicalSize(20, 20),
            stride: 160,
            pixels);
        var annotation = new ScreenshotRectangleAnnotation(
            new LogicalPoint(1, 1),
            new LogicalPoint(8, 8),
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Green, 2));

        var png = SelectionPngEncoder.Encode(
            frame,
            new PhysicalRect(10, 10, 20, 20),
            [annotation]);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(20, bitmap.Width);
        Assert.Equal(20, bitmap.Height);
        Assert.Equal(new SKColor(0, 0, 0, 255), bitmap.GetPixel(10, 10));
        Assert.Equal(new SKColor(7, 201, 119, 255), bitmap.GetPixel(2, 8));
    }

    [Fact]
    public void EncodedPngCompositesLogicalTextAtTheFrameDpi()
    {
        var pixels = new byte[240 * 120 * 4];
        for (var alphaOffset = 3; alphaOffset < pixels.Length; alphaOffset += 4)
        {
            pixels[alphaOffset] = byte.MaxValue;
        }

        using var frame = new CapturedFrame(
            new PhysicalSize(240, 120),
            new LogicalSize(120, 60),
            stride: 960,
            pixels);
        var annotation = new ScreenshotTextAnnotation(
            new LogicalPoint(8, 6),
            "DPI 文字",
            90,
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Yellow, 24));

        var png = SelectionPngEncoder.Encode(
            frame,
            new PhysicalRect(0, 0, 240, 120),
            [annotation]);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Contains(
            Enumerable.Range(12, 70),
            y => Enumerable.Range(16, 180).Any(x => bitmap.GetPixel(x, y).Red > 0));
    }

    [Fact]
    public void EncodedPngPixelatesMosaicAtTheFrameDpiWithoutChangingOutsidePixels()
    {
        const int width = 160;
        const int height = 120;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)((x * 3 + y) % 256);
                pixels[offset + 1] = (byte)((x + y * 5) % 256);
                pixels[offset + 2] = (byte)((x * 7 + y * 2) % 256);
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        using var frame = new CapturedFrame(
            new PhysicalSize(width, height),
            new LogicalSize(width / 2, height / 2),
            width * 4,
            pixels);
        var selection = new PhysicalRect(0, 0, width, height);
        var originalPng = SelectionPngEncoder.Encode(frame, selection);
        var mosaicPng = SelectionPngEncoder.Encode(
            frame,
            selection,
            [
                new ScreenshotMosaicAnnotation(
                    [new LogicalPoint(10, 10), new LogicalPoint(50, 20)],
                    new ScreenshotMosaicStyle(16, 8)),
            ]);

        using var original = SKBitmap.Decode(originalPng);
        using var mosaic = SKBitmap.Decode(mosaicPng);
        Assert.Equal(original.GetPixel(2, 2), mosaic.GetPixel(2, 2));
        Assert.NotEqual(original.GetPixel(40, 24), mosaic.GetPixel(40, 24));
    }

    [Fact]
    public void ExportCompositesOverlappingAnnotationsInCreationOrder()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var alphaOffset = 3; alphaOffset < pixels.Length; alphaOffset += 4)
        {
            pixels[alphaOffset] = byte.MaxValue;
        }

        using var frame = new CapturedFrame(
            new PhysicalSize(size, size),
            new LogicalSize(size, size),
            size * 4,
            pixels);
        var rectangle = new ScreenshotRectangleAnnotation(
            new LogicalPoint(8, 8),
            new LogicalPoint(40, 40),
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Green, 8));
        var mosaic = new ScreenshotMosaicAnnotation(
            [new LogicalPoint(4, 8), new LogicalPoint(44, 8)],
            new ScreenshotMosaicStyle(16, 8));
        var selection = new PhysicalRect(0, 0, size, size);

        using var rectangleOnTop = SKBitmap.Decode(SelectionPngEncoder.Encode(
            frame,
            selection,
            [mosaic, rectangle]));
        using var mosaicOnTop = SKBitmap.Decode(SelectionPngEncoder.Encode(
            frame,
            selection,
            [rectangle, mosaic]));

        Assert.NotEqual(rectangleOnTop.GetPixel(20, 8), mosaicOnTop.GetPixel(20, 8));
        Assert.True(rectangleOnTop.GetPixel(20, 8).Green > mosaicOnTop.GetPixel(20, 8).Green);
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        var offset = ((y * width) + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = byte.MaxValue;
    }

    private static List<string> ReadPngChunkTypes(byte[] png)
    {
        var chunkTypes = new List<string>();
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var dataLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var chunkType = Encoding.ASCII.GetString(png, offset + 4, 4);
            chunkTypes.Add(chunkType);
            offset = checked(offset + 12 + dataLength);
            if (chunkType == "IEND")
            {
                break;
            }
        }

        return chunkTypes;
    }
}
