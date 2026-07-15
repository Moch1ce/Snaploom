namespace Snaploom.IntegrationTests;

public sealed class PixelInspectorZoomTests
{
    [Fact]
    public void MagnificationMatchesTheReferenceInterface()
    {
        Assert.InRange(Snaploom.App.ScreenshotPixelInspector.Magnification, 2, 3);
    }
}
