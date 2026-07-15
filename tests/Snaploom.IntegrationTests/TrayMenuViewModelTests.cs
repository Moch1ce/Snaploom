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

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool Enabled { get; private set; }

        public bool IsAutoStartEnabled() => Enabled;

        public void SetAutoStartEnabled(bool enabled) => Enabled = enabled;
    }
}
