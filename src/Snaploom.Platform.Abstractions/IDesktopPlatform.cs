namespace Snaploom.Platform.Abstractions;

public enum DesktopPlatformKind
{
    Windows,
    MacOS,
}

public interface IDesktopPlatform
{
    DesktopPlatformKind Kind { get; }

    string ApplicationId { get; }
}

public interface IPlatformProcessInitializer
{
    void InitializeProcess();
}
