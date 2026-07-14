using Snaploom.Rendering;

namespace Snaploom.Rendering.Tests;

public sealed class TrayIconRendererTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void RenderedTrayIconIsAPngWithVisibleContent()
    {
        var png = TrayIconRenderer.RenderPng(size: 32);

        Assert.True(png.Length > 100);
        Assert.Equal(PngSignature, png[..PngSignature.Length]);
    }
}
