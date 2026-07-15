using SkiaSharp;
using Snaploom.Core;

namespace Snaploom.Rendering;

public static class SelectionPngEncoder
{
    public static byte[] Encode(CapturedFrame frame, PhysicalRect selection)
        => Encode(frame, selection, Array.Empty<IScreenshotAnnotation>());

    public static byte[] Encode(
        CapturedFrame frame,
        PhysicalRect selection,
        IEnumerable<IScreenshotAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(annotations);
        var annotationList = annotations.ToArray();

        if (selection.Width <= 0 || selection.Height <= 0 ||
            selection.X < 0 || selection.Y < 0 ||
            selection.X + selection.Width > frame.PhysicalSize.Width ||
            selection.Y + selection.Height > frame.PhysicalSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        using var colorSpace = SKColorSpace.CreateSrgb();
        var imageInfo = new SKImageInfo(
            selection.Width,
            selection.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace);
        using var bitmap = new SKBitmap(imageInfo);

        var source = frame.Pixels.Span;
        var destination = bitmap.GetPixelSpan();
        var copiedBytesPerRow = checked(selection.Width * 4);

        for (var row = 0; row < selection.Height; row++)
        {
            var sourceOffset = checked(((selection.Y + row) * frame.Stride) + (selection.X * 4));
            var destinationOffset = checked(row * bitmap.RowBytes);
            source.Slice(sourceOffset, copiedBytesPerRow)
                .CopyTo(destination.Slice(destinationOffset, copiedBytesPerRow));
        }

        var mosaics = annotationList.OfType<ScreenshotMosaicAnnotation>().ToArray();
        if (mosaics.Length > 0)
        {
            ScreenshotMosaicRenderer.ApplyToBitmap(
                bitmap,
                frame.ScaleX,
                frame.ScaleY,
                mosaics);
        }

        using (var canvas = new SKCanvas(bitmap))
        {
            ScreenshotAnnotationRenderer.Draw(
                canvas,
                frame.ScaleX,
                frame.ScaleY,
                annotationList.Where(annotation =>
                    annotation is not ScreenshotMosaicAnnotation));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }
}
