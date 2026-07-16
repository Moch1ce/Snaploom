using Snaploom.Core;
using Snaploom.Rendering;
using System.Security.Cryptography;

namespace Snaploom.Rendering.Tests;

public sealed class ScreenshotMosaicTileCacheTests
{
    [Fact]
    public void Continuous4KStrokeRebuildsOnlyDamagedTiles()
    {
        const int width = 3840;
        const int height = 2160;
        var source = CreateGradient(width, height);
        var style = new ScreenshotMosaicStyle(16, 8);
        var first = new ScreenshotMosaicAnnotation(
            [new LogicalPoint(20, 20), new LogicalPoint(80, 40)],
            style);
        var extended = new ScreenshotMosaicAnnotation(
            [
                new LogicalPoint(20, 20),
                new LogicalPoint(80, 40),
                new LogicalPoint(180, 50),
            ],
            style);
        using var cache = new ScreenshotMosaicTileCache(
            width,
            height,
            width * 4,
            source,
            scaleX: 1,
            scaleY: 1,
            tileSize: 128);

        var firstUpdate = cache.Update([first]);
        var secondUpdate = cache.Update([extended]);

        Assert.InRange(firstUpdate.RenderedTileCount, 1, 2);
        Assert.InRange(secondUpdate.RenderedTileCount, 1, 4);
        Assert.True(secondUpdate.ProcessedPixelCount < (width * height) / 100);
    }

    [Fact]
    public void MovingALong4KStrokeDoesNotInvalidateItsWholeBoundingRectangle()
    {
        const int width = 3840;
        const int height = 2160;
        var source = CreateGradient(width, height);
        var style = new ScreenshotMosaicStyle(16, 8);
        var points = Enumerable.Range(0, 80)
            .Select(index => new LogicalPoint(300 + (index * 35), 1800 - (index * 12)))
            .ToArray();
        var original = new ScreenshotMosaicAnnotation(points, style);
        var moved = new ScreenshotMosaicAnnotation(
            points.Select(point => new LogicalPoint(point.X + 2, point.Y + 1)).ToArray(),
            style);
        using var cache = new ScreenshotMosaicTileCache(
            width,
            height,
            width * 4,
            source,
            scaleX: 1,
            scaleY: 1,
            tileSize: 128);

        _ = cache.Update([original]);
        var update = cache.Update([moved]);

        Assert.InRange(update.RenderedTileCount, 1, 80);
        Assert.True(update.ProcessedPixelCount < (width * height) / 6);
    }

    [Fact]
    public void TilePixelsAreTransparentOutsideTheStrokeAndPixelatedInsideIt()
    {
        const int width = 128;
        const int height = 128;
        var source = CreateGradient(width, height);
        using var cache = new ScreenshotMosaicTileCache(
            width,
            height,
            width * 4,
            source,
            scaleX: 1,
            scaleY: 1,
            tileSize: 128);
        var annotation = new ScreenshotMosaicAnnotation(
            [new LogicalPoint(32, 64), new LogicalPoint(96, 64)],
            new ScreenshotMosaicStyle(16, 8));

        var update = cache.Update([annotation]);

        var tile = Assert.Single(update.ChangedTiles);
        Assert.Equal(0, GetAlpha(tile, 8, 8));
        Assert.True(GetAlpha(tile, 64, 64) > 0);
        var sourceOffset = ((64 * width) + 64) * 4;
        var tileOffset = (64 * tile.Stride) + (64 * 4);
        Assert.NotEqual(source[sourceOffset], tile.Pixels[tileOffset]);
    }

    [Fact]
    public void MosaicStrokeMatchesTheGoldenTileRaster()
    {
        const int width = 160;
        const int height = 120;
        var source = CreateGradient(width, height);
        using var cache = new ScreenshotMosaicTileCache(
            width,
            height,
            width * 4,
            source,
            scaleX: 1,
            scaleY: 1,
            tileSize: 128);
        var annotation = new ScreenshotMosaicAnnotation(
            [
                new LogicalPoint(12, 18),
                new LogicalPoint(70, 50),
                new LogicalPoint(145, 95),
            ],
            new ScreenshotMosaicStyle(32, 12));

        cache.Update([annotation]);

        var pixels = cache.Tiles
            .OrderBy(tile => tile.Key.Row)
            .ThenBy(tile => tile.Key.Column)
            .SelectMany(tile => tile.Pixels)
            .ToArray();
        var actualHash = Convert.ToHexString(SHA256.HashData(pixels));
        Assert.StartsWith(
            "132D88B4C4BD24B51204FA56170FDEC76512BF883F977A4569",
            actualHash,
            StringComparison.Ordinal);
    }

    private static byte[] CreateGradient(int width, int height)
    {
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

        return pixels;
    }

    private static byte GetAlpha(MosaicTileRaster tile, int x, int y) =>
        tile.Pixels[(y * tile.Stride) + (x * 4) + 3];
}
