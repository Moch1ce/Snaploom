using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotMoveCursorTests
{
    [AvaloniaFact]
    public void MoveCursorRendersFourArrowTipsAroundACenteredHotSpot()
    {
        using var bitmap = ScreenshotMoveCursor.RenderBitmap();
        var pixels = CopyPixels(bitmap);

        Assert.Equal(
            new PixelPoint(
                ScreenshotMoveCursor.BitmapSize / 2,
                ScreenshotMoveCursor.BitmapSize / 2),
            ScreenshotMoveCursor.HotSpot);
        AssertVisible(pixels, ScreenshotMoveCursor.HotSpot.X, 3);
        AssertVisible(pixels, ScreenshotMoveCursor.HotSpot.X, 25);
        AssertVisible(pixels, 3, ScreenshotMoveCursor.HotSpot.Y);
        AssertVisible(pixels, 25, ScreenshotMoveCursor.HotSpot.Y);
        AssertTransparent(pixels, 0, 0);
        AssertTransparent(pixels, ScreenshotMoveCursor.BitmapSize - 1, 0);
        AssertTransparent(pixels, 0, ScreenshotMoveCursor.BitmapSize - 1);
        AssertTransparent(
            pixels,
            ScreenshotMoveCursor.BitmapSize - 1,
            ScreenshotMoveCursor.BitmapSize - 1);
    }

    private static byte[] CopyPixels(Bitmap bitmap)
    {
        var stride = ScreenshotMoveCursor.BitmapSize * 4;
        var pixels = new byte[stride * ScreenshotMoveCursor.BitmapSize];
        var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, ScreenshotMoveCursor.BitmapSize, ScreenshotMoveCursor.BitmapSize),
                pinnedPixels.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            pinnedPixels.Free();
        }

        return pixels;
    }

    private static void AssertVisible(byte[] pixels, int x, int y) =>
        Assert.NotEqual(0, pixels[((y * ScreenshotMoveCursor.BitmapSize) + x) * 4 + 3]);

    private static void AssertTransparent(byte[] pixels, int x, int y) =>
        Assert.Equal(0, pixels[((y * ScreenshotMoveCursor.BitmapSize) + x) * 4 + 3]);
}
