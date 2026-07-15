using Snaploom.App;
using Snaploom.Platform.Abstractions;

namespace Snaploom.IntegrationTests;

public sealed class TrayMenuViewModelTests
{
    [Fact]
    public void AutoStartIsOffUntilTheUserEnablesIt()
    {
        var autoStart = new FakeAutoStartService();
        var settings = AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(DesktopPlatformKind.Windows));
        var viewModel = new TrayMenuViewModel(
            () => Task.CompletedTask,
            () => { },
            autoStart,
            () => { },
            settings);

        Assert.False(viewModel.IsAutoStartEnabled);

        viewModel.ToggleAutoStartCommand.Execute(parameter: null);

        Assert.True(viewModel.IsAutoStartEnabled);
        Assert.True(autoStart.Enabled);
        Assert.True(settings.Current.AutoStart);
    }

    [Fact]
    public void AutoStartPlatformFailureIsReportedWithoutChangingTheSetting()
    {
        var settings = AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(DesktopPlatformKind.Windows));
        Exception? reported = null;
        var viewModel = new TrayMenuViewModel(
            () => Task.CompletedTask,
            () => { },
            new FailingAutoStartService(),
            () => { },
            settings,
            exception => reported = exception);

        viewModel.ToggleAutoStartCommand.Execute(parameter: null);

        Assert.IsType<InvalidOperationException>(reported);
        Assert.False(viewModel.IsAutoStartEnabled);
        Assert.False(settings.Current.AutoStart);
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool Enabled { get; private set; }

        public bool IsAutoStartEnabled() => Enabled;

        public void SetAutoStartEnabled(bool enabled) => Enabled = enabled;
    }

    private sealed class FailingAutoStartService : IAutoStartService
    {
        public bool IsAutoStartEnabled() => false;

        public void SetAutoStartEnabled(bool enabled) =>
            throw new InvalidOperationException("Sensitive platform detail.");
    }
}
