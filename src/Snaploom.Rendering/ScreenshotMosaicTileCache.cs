using SkiaSharp;
using Snaploom.Core;
using System.Security.Cryptography;

namespace Snaploom.Rendering;

public readonly record struct MosaicTileKey(int Column, int Row);

public sealed record MosaicTileRaster(
    MosaicTileKey Key,
    int X,
    int Y,
    int Width,
    int Height,
    int Stride,
    byte[] Pixels);

public sealed record MosaicTileUpdate(
    IReadOnlyList<MosaicTileRaster> ChangedTiles,
    int RenderedTileCount,
    long ProcessedPixelCount);

public sealed class ScreenshotMosaicTileCache : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _sourceStride;
    private readonly byte[] _sourcePixels;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly int _tileSize;
    private readonly Dictionary<MosaicTileKey, MosaicTileRaster> _tiles = [];
    private IReadOnlyList<MosaicSnapshot> _previous = [];
    private bool _disposed;

    public ScreenshotMosaicTileCache(
        int width,
        int height,
        int sourceStride,
        ReadOnlySpan<byte> sourcePixels,
        double scaleX,
        double scaleY,
        int tileSize = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceStride, checked(width * 4));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        var requiredLength = checked(((height - 1) * sourceStride) + (width * 4));
        if (sourcePixels.Length < requiredLength)
        {
            throw new ArgumentException("The source pixel buffer is too small.", nameof(sourcePixels));
        }

        _width = width;
        _height = height;
        _sourceStride = checked(width * 4);
        _sourcePixels = new byte[checked(_sourceStride * height)];
        for (var row = 0; row < height; row++)
        {
            sourcePixels.Slice(row * sourceStride, _sourceStride)
                .CopyTo(_sourcePixels.AsSpan(row * _sourceStride, _sourceStride));
        }

        _scaleX = scaleX;
        _scaleY = scaleY;
        _tileSize = tileSize;
    }

    public IReadOnlyCollection<MosaicTileRaster> Tiles => _tiles.Values;

    public MosaicTileUpdate Update(IEnumerable<ScreenshotMosaicAnnotation> annotations)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(annotations);
        var current = annotations
            .Select(annotation => new MosaicSnapshot(
                annotation.Points.ToArray(),
                annotation.Style))
            .ToArray();
        var dirtyTiles = FindDirtyTiles(_previous, current);
        if (dirtyTiles.Count == 0)
        {
            _previous = current;
            return new MosaicTileUpdate([], 0, 0);
        }

        var changedTiles = new List<MosaicTileRaster>(dirtyTiles.Count);
        long processedPixels = 0;
        foreach (var key in dirtyTiles.OrderBy(key => key.Row).ThenBy(key => key.Column))
        {
            var tile = RenderTile(key, current);
            _tiles[key] = tile;
            changedTiles.Add(tile);
            processedPixels += (long)tile.Width * tile.Height;
        }

        _previous = current;
        return new MosaicTileUpdate(changedTiles, changedTiles.Count, processedPixels);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_sourcePixels);
        foreach (var tile in _tiles.Values)
        {
            CryptographicOperations.ZeroMemory(tile.Pixels);
        }

        _tiles.Clear();
        _previous = [];
    }

    private HashSet<MosaicTileKey> FindDirtyTiles(
        IReadOnlyList<MosaicSnapshot> previous,
        IReadOnlyList<MosaicSnapshot> current)
    {
        var dirtyBounds = new List<LogicalBounds>();
        var count = Math.Max(previous.Count, current.Count);
        for (var index = 0; index < count; index++)
        {
            var oldSnapshot = index < previous.Count ? previous[index] : null;
            var newSnapshot = index < current.Count ? current[index] : null;
            if (oldSnapshot is not null &&
                newSnapshot is not null &&
                oldSnapshot.Style == newSnapshot.Style &&
                IsPrefix(oldSnapshot.Points, newSnapshot.Points))
            {
                if (oldSnapshot.Points.Count != newSnapshot.Points.Count)
                {
                    AddBounds(
                        dirtyBounds,
                        newSnapshot,
                        Math.Max(0, oldSnapshot.Points.Count - 1));
                }

                continue;
            }

            if (oldSnapshot is not null)
            {
                AddBounds(dirtyBounds, oldSnapshot, startIndex: 0);
            }

            if (newSnapshot is not null)
            {
                AddBounds(dirtyBounds, newSnapshot, startIndex: 0);
            }
        }

        var keys = new HashSet<MosaicTileKey>();
        foreach (var bounds in dirtyBounds)
        {
            var left = Math.Clamp((int)Math.Floor(bounds.Left * _scaleX), 0, _width - 1);
            var top = Math.Clamp((int)Math.Floor(bounds.Top * _scaleY), 0, _height - 1);
            var right = Math.Clamp((int)Math.Ceiling(bounds.Right * _scaleX), 0, _width - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * _scaleY), 0, _height - 1);
            for (var row = top / _tileSize; row <= bottom / _tileSize; row++)
            {
                for (var column = left / _tileSize; column <= right / _tileSize; column++)
                {
                    keys.Add(new MosaicTileKey(column, row));
                }
            }
        }

        return keys;
    }

    private MosaicTileRaster RenderTile(
        MosaicTileKey key,
        IReadOnlyList<MosaicSnapshot> annotations)
    {
        var tileX = key.Column * _tileSize;
        var tileY = key.Row * _tileSize;
        var tileWidth = Math.Min(_tileSize, _width - tileX);
        var tileHeight = Math.Min(_tileSize, _height - tileY);
        var tileStride = checked(tileWidth * 4);
        var pixels = new byte[checked(tileStride * tileHeight)];
        var maskInfo = new SKImageInfo(
            tileWidth,
            tileHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var mask = new SKBitmap(maskInfo);
        using var canvas = new SKCanvas(mask);
        canvas.Translate(-tileX, -tileY);

        foreach (var annotation in annotations)
        {
            mask.Erase(SKColors.Transparent);
            DrawMask(canvas, annotation);
            ApplyPixelation(
                pixels,
                tileStride,
                tileX,
                tileY,
                tileWidth,
                tileHeight,
                mask,
                annotation.Style);
        }

        return new MosaicTileRaster(
            key,
            tileX,
            tileY,
            tileWidth,
            tileHeight,
            tileStride,
            pixels);
    }

    private void DrawMask(SKCanvas canvas, MosaicSnapshot annotation)
    {
        if (annotation.Points.Count == 0)
        {
            return;
        }

        var scale = (_scaleX + _scaleY) / 2;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(annotation.Style.BrushSize * scale),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        if (annotation.Points.Count == 1)
        {
            var point = annotation.Points[0];
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle(
                (float)(point.X * _scaleX),
                (float)(point.Y * _scaleY),
                paint.StrokeWidth / 2,
                paint);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(ToPhysicalPoint(annotation.Points[0]));
        for (var index = 1; index < annotation.Points.Count; index++)
        {
            path.LineTo(ToPhysicalPoint(annotation.Points[index]));
        }

        canvas.DrawPath(path, paint);
    }

    private void ApplyPixelation(
        byte[] destination,
        int destinationStride,
        int tileX,
        int tileY,
        int tileWidth,
        int tileHeight,
        SKBitmap mask,
        ScreenshotMosaicStyle style)
    {
        var maskPixels = mask.GetPixelSpan();
        var blockSize = Math.Max(
            1,
            (int)Math.Round(style.PixelSize * ((_scaleX + _scaleY) / 2)));
        var averages = new Dictionary<(int X, int Y), BgraColor>();
        for (var localY = 0; localY < tileHeight; localY++)
        {
            for (var localX = 0; localX < tileWidth; localX++)
            {
                var maskAlpha = maskPixels[
                    (localY * mask.RowBytes) + (localX * 4) + 3];
                if (maskAlpha == 0)
                {
                    continue;
                }

                var globalX = tileX + localX;
                var globalY = tileY + localY;
                var blockX = (globalX / blockSize) * blockSize;
                var blockY = (globalY / blockSize) * blockSize;
                if (!averages.TryGetValue((blockX, blockY), out var average))
                {
                    average = GetBlockAverage(blockX, blockY, blockSize);
                    averages.Add((blockX, blockY), average);
                }

                var alpha = (byte)((average.Alpha * maskAlpha + 127) / 255);
                var destinationOffset =
                    (localY * destinationStride) + (localX * 4);
                destination[destinationOffset] = Premultiply(average.Blue, alpha);
                destination[destinationOffset + 1] = Premultiply(average.Green, alpha);
                destination[destinationOffset + 2] = Premultiply(average.Red, alpha);
                destination[destinationOffset + 3] = alpha;
            }
        }
    }

    private BgraColor GetBlockAverage(int blockX, int blockY, int blockSize)
    {
        var right = Math.Min(_width, blockX + blockSize);
        var bottom = Math.Min(_height, blockY + blockSize);
        long blue = 0;
        long green = 0;
        long red = 0;
        long alpha = 0;
        var count = 0;
        for (var y = blockY; y < bottom; y++)
        {
            for (var x = blockX; x < right; x++)
            {
                var offset = (y * _sourceStride) + (x * 4);
                blue += _sourcePixels[offset];
                green += _sourcePixels[offset + 1];
                red += _sourcePixels[offset + 2];
                alpha += _sourcePixels[offset + 3];
                count++;
            }
        }

        return new BgraColor(
            (byte)(blue / count),
            (byte)(green / count),
            (byte)(red / count),
            (byte)(alpha / count));
    }

    private SKPoint ToPhysicalPoint(LogicalPoint point) =>
        new((float)(point.X * _scaleX), (float)(point.Y * _scaleY));

    private static bool IsPrefix(
        IReadOnlyList<LogicalPoint> prefix,
        IReadOnlyList<LogicalPoint> points)
    {
        if (prefix.Count > points.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (prefix[index] != points[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void AddBounds(
        ICollection<LogicalBounds> bounds,
        MosaicSnapshot snapshot,
        int startIndex)
    {
        if (snapshot.Points.Count == 0)
        {
            return;
        }

        var radius = (snapshot.Style.BrushSize / 2d) + snapshot.Style.PixelSize;
        var firstSegment = Math.Clamp(startIndex, 0, snapshot.Points.Count - 1);
        if (snapshot.Points.Count == 1 || firstSegment == snapshot.Points.Count - 1)
        {
            AddPointBounds(bounds, snapshot.Points[firstSegment], radius);
            return;
        }

        for (var index = firstSegment; index < snapshot.Points.Count - 1; index++)
        {
            var start = snapshot.Points[index];
            var end = snapshot.Points[index + 1];
            bounds.Add(new LogicalBounds(
                Math.Min(start.X, end.X) - radius,
                Math.Min(start.Y, end.Y) - radius,
                Math.Max(start.X, end.X) + radius,
                Math.Max(start.Y, end.Y) + radius));
        }
    }

    private static void AddPointBounds(
        ICollection<LogicalBounds> bounds,
        LogicalPoint point,
        double radius) =>
        bounds.Add(new LogicalBounds(
            point.X - radius,
            point.Y - radius,
            point.X + radius,
            point.Y + radius));

    private static byte Premultiply(byte color, byte alpha) =>
        (byte)((color * alpha + 127) / 255);

    private sealed record MosaicSnapshot(
        IReadOnlyList<LogicalPoint> Points,
        ScreenshotMosaicStyle Style);

    private readonly record struct LogicalBounds(
        double Left,
        double Top,
        double Right,
        double Bottom);

    private readonly record struct BgraColor(
        byte Blue,
        byte Green,
        byte Red,
        byte Alpha);
}

public static class ScreenshotMosaicRenderer
{
    public static void ApplyToBitmap(
        SKBitmap bitmap,
        double scaleX,
        double scaleY,
        IEnumerable<ScreenshotMosaicAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(annotations);
        using var cache = new ScreenshotMosaicTileCache(
            bitmap.Width,
            bitmap.Height,
            bitmap.RowBytes,
            bitmap.GetPixelSpan(),
            scaleX,
            scaleY);
        var update = cache.Update(annotations);
        var destination = bitmap.GetPixelSpan();
        foreach (var tile in update.ChangedTiles)
        {
            CompositeTile(destination, bitmap.RowBytes, tile);
        }
    }

    private static void CompositeTile(
        Span<byte> destination,
        int destinationStride,
        MosaicTileRaster tile)
    {
        for (var localY = 0; localY < tile.Height; localY++)
        {
            for (var localX = 0; localX < tile.Width; localX++)
            {
                var sourceOffset = (localY * tile.Stride) + (localX * 4);
                var alpha = tile.Pixels[sourceOffset + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var destinationOffset =
                    ((tile.Y + localY) * destinationStride) +
                    ((tile.X + localX) * 4);
                var inverseAlpha = 255 - alpha;
                destination[destinationOffset] = Blend(
                    tile.Pixels[sourceOffset],
                    destination[destinationOffset],
                    inverseAlpha);
                destination[destinationOffset + 1] = Blend(
                    tile.Pixels[sourceOffset + 1],
                    destination[destinationOffset + 1],
                    inverseAlpha);
                destination[destinationOffset + 2] = Blend(
                    tile.Pixels[sourceOffset + 2],
                    destination[destinationOffset + 2],
                    inverseAlpha);
                destination[destinationOffset + 3] = (byte)Math.Min(
                    255,
                    alpha + ((destination[destinationOffset + 3] * inverseAlpha + 127) / 255));
            }
        }
    }

    private static byte Blend(byte source, byte destination, int inverseAlpha) =>
        (byte)Math.Min(255, source + ((destination * inverseAlpha + 127) / 255));
}
