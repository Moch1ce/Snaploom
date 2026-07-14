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
