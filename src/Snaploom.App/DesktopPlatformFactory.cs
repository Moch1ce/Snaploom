using Snaploom.Platform.Abstractions;
using Snaploom.Platform.MacOS;
using Snaploom.Platform.Windows;

namespace Snaploom.App;

public static class DesktopPlatformFactory
{
    public static IDesktopPlatform CreateCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDesktopPlatform();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSDesktopPlatform();
        }

        throw new PlatformNotSupportedException("Snaploom v1 supports Windows and macOS only.");
    }
}
