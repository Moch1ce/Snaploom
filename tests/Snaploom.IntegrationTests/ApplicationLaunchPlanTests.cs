using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class ApplicationLaunchPlanTests
{
    [Fact]
    public void DefaultPlanStartsInTrayWithoutAMainWindow()
    {
        var plan = ApplicationLaunchPlan.Default;

        Assert.False(plan.ShowMainWindow);
        Assert.True(plan.ShowTrayIcon);
        Assert.Collection(
            plan.TrayItems,
            item =>
            {
                Assert.Equal(TrayAction.StartScreenshot, item.Action);
                Assert.False(item.IsEnabled);
            },
            item =>
            {
                Assert.Equal(TrayAction.Exit, item.Action);
                Assert.True(item.IsEnabled);
            });
    }
}
