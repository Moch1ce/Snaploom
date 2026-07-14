using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.Windows;

public sealed partial class WindowsDesktopPlatform : IDesktopPlatform, IPlatformProcessInitializer
{
    public DesktopPlatformKind Kind => DesktopPlatformKind.Windows;

    public string ApplicationId => ProductIdentity.WindowsAppId;

    public void InitializeProcess()
    {
        var result = SetCurrentProcessExplicitAppUserModelId(ApplicationId);
        Marshal.ThrowExceptionForHR(result);
    }

    [LibraryImport(
        "shell32.dll",
        EntryPoint = "SetCurrentProcessExplicitAppUserModelID",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelId(string applicationId);
}
