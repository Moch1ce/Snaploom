using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class CapturedFrameTests
{
    [Fact]
    public void SamplesBgraPixelAndFormatsRgbHex()
    {
        var pixels = new byte[24];
        pixels[16] = 0x3E;
        pixels[17] = 0x3A;
        pixels[18] = 0x37;
        pixels[19] = byte.MaxValue;

        using var frame = new CapturedFrame(
            new PhysicalSize(2, 2),
            new LogicalSize(2, 2),
            stride: 12,
            pixels);

        var color = frame.SamplePixel(new PhysicalPoint(1, 1));

        Assert.Equal(new CapturedColor(0x37, 0x3A, 0x3E), color);
        Assert.Equal("#373A3E", color.Hex);
    }

    [Fact]
    public void SamplingOutsideTheFrameIsRejected()
    {
        using var frame = new CapturedFrame(
            new PhysicalSize(2, 2),
            new LogicalSize(2, 2),
            stride: 8,
            new byte[16]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => frame.SamplePixel(new PhysicalPoint(2, 1)));
    }

    [Fact]
    public void MapsLogicalCoordinatesToPhysicalPixelsAtHighDpi()
    {
        using var frame = new CapturedFrame(
            new PhysicalSize(3000, 2000),
            new LogicalSize(1500, 1000),
            stride: 12000,
            new byte[24_000_000]);

        Assert.Equal(
            new PhysicalPoint(247, 501),
            frame.ToPhysicalPoint(new LogicalPoint(123.5, 250.5)));
    }
}
