using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.MacOS;

public sealed class MacOSDesktopPlatform : IDesktopPlatform
{
    public DesktopPlatformKind Kind => DesktopPlatformKind.MacOS;

    public string ApplicationId => ProductIdentity.MacOSBundleId;
}
