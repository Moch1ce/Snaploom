using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotActivationGateTests
{
    [Fact]
    public void RepeatedActivationIsIgnoredUntilTheSessionEnds()
    {
        var gate = new ScreenshotActivationGate();

        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());

        gate.End();

        Assert.True(gate.TryBegin());
    }
}
