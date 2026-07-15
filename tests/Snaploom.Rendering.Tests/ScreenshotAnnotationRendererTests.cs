using System.Security.Cryptography;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.Rendering.Tests;

public sealed class ScreenshotAnnotationRendererTests
{
    [Theory]
    [InlineData(
        96,
        72,
        1,
        "8A151511BA3B3B920B2AA2866FCAB306CC31E3BE066EF92D25",
        "93C986DD34BB460A2D511D778F28D12ECFB3E3199BC8D6287B")]
    [InlineData(
        192,
        144,
        2,
        "516240C06F44D66C8B82DA07440446D8051AD7A0EF8DB2CC5A",
        "A46FB73519C609C69D2C7CFC0820223B3E957F7026041EEFB5")]
    public void RectangleAndArrowMatchTheGoldenRasterAtDifferentDpi(
        int width,
        int height,
        double scale,
        string macOSGoldenFingerprint,
        string windowsGoldenFingerprint)
    {
        IScreenshotAnnotation[] annotations =
        [
            new ScreenshotRectangleAnnotation(
                new LogicalPoint(5, 5),
                new LogicalPoint(40, 30),
                new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Red, 2)),
            new ScreenshotArrowAnnotation(
                new LogicalPoint(10, 55),
                new LogicalPoint(70, 15),
                new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 4)),
        ];

        var raster = ScreenshotAnnotationRenderer.RenderBgra(
            width,
            height,
            scale,
            scale,
            annotations);

        Assert.Equal(width * 4, raster.Stride);
        var expectedFingerprint = OperatingSystem.IsWindows()
            ? windowsGoldenFingerprint
            : macOSGoldenFingerprint;
        var actualHash = Convert.ToHexString(SHA256.HashData(raster.Pixels));
        Assert.StartsWith(expectedFingerprint, actualHash, StringComparison.Ordinal);
    }

    [Fact]
    public void RectangleInteriorRemainsTransparent()
    {
        var annotation = new ScreenshotRectangleAnnotation(
            new LogicalPoint(4, 4),
            new LogicalPoint(28, 24),
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Green, 2));

        var raster = ScreenshotAnnotationRenderer.RenderBgra(
            32,
            28,
            1,
            1,
            [annotation]);

        Assert.Equal(0, GetAlpha(raster, 16, 14));
        Assert.True(GetAlpha(raster, 4, 14) > 0);
    }

    private static byte GetAlpha(AnnotationRaster raster, int x, int y) =>
        raster.Pixels[(y * raster.Stride) + (x * 4) + 3];
}
