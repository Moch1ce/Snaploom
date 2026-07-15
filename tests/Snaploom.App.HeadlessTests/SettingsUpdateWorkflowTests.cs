using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App.HeadlessTests;

public sealed class SettingsUpdateWorkflowTests
{
    [AvaloniaFact]
    public void UpdateServiceRunsOnlyAfterClickAndReleaseOpensOnlyAfterSecondClick()
    {
        var logDirectory = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-update-ui-{Guid.NewGuid():N}");
        var hotKeyService = new FakeHotKeyService();
        using var hotKeyManager = new ScreenshotHotKeyManager(
            hotKeyService,
            new FakeResumeService(),
            ScreenshotHotKeyDefaults.For(DesktopPlatformKind.Windows),
            () => { });
        Assert.True(hotKeyManager.Start());
        var settings = AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(DesktopPlatformKind.Windows));
        var trayViewModel = new TrayMenuViewModel(
            () => Task.CompletedTask,
            () => { },
            new FakeAutoStartService(),
            () => { },
            settings);
        var updateService = new FakeUpdateCheckService();
        var uriLauncher = new FakeUriLauncher();
        var window = new ShortcutSettingsWindow(
            hotKeyManager,
            settings,
            trayViewModel,
            () => { },
            new PrivacyLog(logDirectory),
            folderLauncher: null,
            updateService,
            uriLauncher);
        try
        {
            window.Show();
            Assert.Equal(0, updateService.CallCount);

            FindButton(window, AppUiText.CheckUpdates).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, updateService.CallCount);
            Assert.Null(uriLauncher.OpenedUri);

            FindButton(window, AppUiText.OpenRelease).RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(updateService.Result.ReleasePage, uriLauncher.OpenedUri);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }

    private static Button FindButton(Window window, string content) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Content is string value &&
                string.Equals(value, content, StringComparison.Ordinal));

    private sealed class FakeUpdateCheckService : IUpdateCheckService
    {
        public UpdateCheckResult Result { get; } = new(
            UpdateCheckStatus.UpdateAvailable,
            new Version(2, 0, 0, 0),
            new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
            "Release notes",
            new Uri("https://github.com/liuchuana/Snaploom/releases/tag/v2.0.0"));

        public int CallCount { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeUriLauncher : IExternalUriLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public void OpenUri(Uri uri) => OpenedUri = uri;
    }

    private sealed class FakeHotKeyService : IGlobalScreenshotHotKeyService
    {
        public bool TryRegisterScreenshotHotKey(ScreenshotHotKey hotKey, Action callback) => true;

        public void UnregisterScreenshotHotKey()
        {
        }
    }

    private sealed class FakeResumeService : ISystemResumeService
    {
        public void StartMonitoring(Action callback)
        {
        }

        public void StopMonitoring()
        {
        }
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool IsAutoStartEnabled() => false;

        public void SetAutoStartEnabled(bool enabled)
        {
        }
    }
}
