using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class TrayMenuViewModelTests
{
    [Fact]
    public void ExitCommandRequestsApplicationShutdown()
    {
        var shutdownRequested = false;
        var viewModel = new TrayMenuViewModel(() => shutdownRequested = true);

        viewModel.ExitCommand.Execute(parameter: null);

        Assert.True(shutdownRequested);
    }
}
