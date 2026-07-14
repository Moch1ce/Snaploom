using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.Windows;

public sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    public DesktopPlatformKind Kind => DesktopPlatformKind.Windows;

    public string ApplicationId => ProductIdentity.WindowsAppId;
}
