using System.Runtime.InteropServices;

namespace Snaploom.Platform.MacOS;

public enum MacOSFrameResizeCursorPosition
{
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft,
}

public readonly record struct MacOSFrameResizeCursorImage(
    byte[] Png,
    int HotSpotX,
    int HotSpotY);

public static class MacOSFrameResizeCursorImageProvider
{
    public static bool TryCreate(
        MacOSFrameResizeCursorPosition position,
        out MacOSFrameResizeCursorImage image)
    {
        image = default;
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var handle = MacOSNative.CreateFrameResizeCursorImage((int)position);
        if (handle == 0)
        {
            return false;
        }

        try
        {
            var length = checked((int)MacOSNative.GetFrameResizeCursorPngLength(handle));
            var data = MacOSNative.GetFrameResizeCursorPngData(handle);
            if (length <= 0 || data == 0)
            {
                return false;
            }

            var png = new byte[length];
            Marshal.Copy(data, png, 0, length);
            image = new MacOSFrameResizeCursorImage(
                png,
                MacOSNative.GetFrameResizeCursorHotSpotX(handle),
                MacOSNative.GetFrameResizeCursorHotSpotY(handle));
            return true;
        }
        finally
        {
            MacOSNative.ReleaseFrameResizeCursorImage(handle);
        }
    }
}
