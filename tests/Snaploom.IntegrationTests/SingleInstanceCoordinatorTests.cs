using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondaryInstanceSignalsCaptureAndDoesNotBecomePrimary()
    {
        var identity = $"Snaploom.Tests.{Guid.NewGuid():N}";
        using var primary = SingleInstanceCoordinator.Acquire(identity);
        var captureRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(() => captureRequested.TrySetResult());

        using var secondary = SingleInstanceCoordinator.Acquire(identity);
        var signaled = await secondary.SignalCaptureAsync(TestContext.Current.CancellationToken);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.True(signaled);
        await captureRequested.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
    }
}
