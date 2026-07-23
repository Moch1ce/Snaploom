using Snaploom.App;
using Snaploom.Platform.Abstractions;

namespace Snaploom.IntegrationTests;

public sealed class HotKeyLifecycleTests
{
    [Theory]
    [InlineData(DesktopPlatformKind.Windows, ScreenshotHotKeyModifiers.Alt | ScreenshotHotKeyModifiers.Shift)]
    [InlineData(DesktopPlatformKind.MacOS, ScreenshotHotKeyModifiers.Command | ScreenshotHotKeyModifiers.Shift)]
    public void PlatformDefaultsUseTheExpectedModifiers(
        DesktopPlatformKind platform,
        ScreenshotHotKeyModifiers expectedModifiers)
    {
        var hotKey = ScreenshotHotKeyDefaults.For(platform);

        Assert.Equal(ScreenshotHotKeyKey.A, hotKey.Key);
        Assert.Equal(expectedModifiers, hotKey.Modifiers);
        Assert.Equal(
            platform == DesktopPlatformKind.Windows ? "Alt+Shift+A" : "Command+Shift+A",
            hotKey.ToString());
    }

    [Fact]
    public void ChangingToAConflictingShortcutRestoresThePreviousRegistration()
    {
        var service = new FakeHotKeyService();
        var resume = new FakeResumeService();
        var original = new ScreenshotHotKey(
            ScreenshotHotKeyModifiers.Alt | ScreenshotHotKeyModifiers.Shift,
            ScreenshotHotKeyKey.A);
        var conflicting = new ScreenshotHotKey(
            ScreenshotHotKeyModifiers.Control | ScreenshotHotKeyModifiers.Shift,
            ScreenshotHotKeyKey.B);
        service.ConflictingHotKey = conflicting;
        using var manager = new ScreenshotHotKeyManager(service, resume, original, () => { });

        Assert.True(manager.Start());
        var result = manager.TryChange(conflicting);

        Assert.Equal(ScreenshotHotKeyChangeResult.Conflict, result);
        Assert.Equal(original, manager.CurrentHotKey);
        Assert.Equal([original, conflicting, original], service.RegistrationAttempts);
        Assert.True(manager.IsRegistered);
    }

    [Fact]
    public void ResumeReRegistersTheCurrentShortcut()
    {
        var service = new FakeHotKeyService();
        var resume = new FakeResumeService();
        var hotKey = ScreenshotHotKeyDefaults.For(DesktopPlatformKind.Windows);
        using var manager = new ScreenshotHotKeyManager(service, resume, hotKey, () => { });
        Assert.True(manager.Start());

        resume.RaiseResumed();

        Assert.Equal([hotKey, hotKey], service.RegistrationAttempts);
        Assert.Equal(2, service.UnregisterCount);
        Assert.True(manager.IsRegistered);
    }

    [Fact]
    public void DisposeStopsResumeMonitoringAndUnregistersTheShortcut()
    {
        var service = new FakeHotKeyService();
        var resume = new FakeResumeService();
        var manager = new ScreenshotHotKeyManager(
            service,
            resume,
            ScreenshotHotKeyDefaults.For(DesktopPlatformKind.MacOS),
            () => { });
        manager.Start();

        manager.Dispose();

        Assert.Equal(2, service.UnregisterCount);
        Assert.Equal(1, resume.StopCount);
    }

    [Fact]
    public void FailedRegistrationAfterWakeRaisesABackgroundFailure()
    {
        var service = new FakeHotKeyService();
        var resume = new FakeResumeService();
        var hotKey = ScreenshotHotKeyDefaults.For(DesktopPlatformKind.Windows);
        using var manager = new ScreenshotHotKeyManager(service, resume, hotKey, () => { });
        Assert.True(manager.Start());
        var failureCount = 0;
        manager.RegistrationFailed += (_, _) => failureCount++;
        service.ConflictingHotKey = hotKey;

        resume.RaiseResumed();

        Assert.Equal(1, failureCount);
        Assert.False(manager.IsRegistered);
    }

    private sealed class FakeHotKeyService : IGlobalScreenshotHotKeyService
    {
        public List<ScreenshotHotKey> RegistrationAttempts { get; } = [];

        public ScreenshotHotKey? ConflictingHotKey { get; set; }

        public int UnregisterCount { get; private set; }

        public bool TryRegisterScreenshotHotKey(ScreenshotHotKey hotKey, Action callback)
        {
            RegistrationAttempts.Add(hotKey);
            return hotKey != ConflictingHotKey;
        }

        public void UnregisterScreenshotHotKey() => UnregisterCount++;
    }

    private sealed class FakeResumeService : ISystemResumeService
    {
        private Action? _callback;

        public int StopCount { get; private set; }

        public void StartMonitoring(Action callback) => _callback = callback;

        public void StopMonitoring()
        {
            StopCount++;
            _callback = null;
        }

        public void RaiseResumed() => _callback?.Invoke();
    }
}
