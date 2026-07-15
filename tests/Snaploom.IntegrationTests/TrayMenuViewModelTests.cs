using Snaploom.App;
using Snaploom.Platform.Abstractions;

namespace Snaploom.IntegrationTests;

public sealed class TrayMenuViewModelTests
{
    [Fact]
    public void AutoStartIsOffUntilTheUserEnablesIt()
    {
        var autoStart = new FakeAutoStartService();
        var viewModel = new TrayMenuViewModel(
            () => Task.CompletedTask,
            () => { },
            autoStart,
            () => { });

        Assert.False(viewModel.IsAutoStartEnabled);

        viewModel.ToggleAutoStartCommand.Execute(parameter: null);

        Assert.True(viewModel.IsAutoStartEnabled);
        Assert.True(autoStart.Enabled);
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool Enabled { get; private set; }

        public bool IsAutoStartEnabled() => Enabled;

        public void SetAutoStartEnabled(bool enabled) => Enabled = enabled;
    }
}
