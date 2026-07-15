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

    [Fact]
    public void WindowsIconContainsAllRequiredPngSizes()
    {
        var icon = TrayIconRenderer.RenderWindowsIco();

        Assert.Equal((ushort)0, BitConverter.ToUInt16(icon, 0));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(icon, 2));
        Assert.Equal((ushort)5, BitConverter.ToUInt16(icon, 4));
        Assert.Equal([16, 32, 48, 64, 0], ReadEntrySizes(icon));
        for (var index = 0; index < 5; index++)
        {
            var entryOffset = 6 + (index * 16);
            var imageLength = BitConverter.ToInt32(icon, entryOffset + 8);
            var imageOffset = BitConverter.ToInt32(icon, entryOffset + 12);
            Assert.True(imageLength > 100);
            Assert.Equal(PngSignature, icon[imageOffset..(imageOffset + PngSignature.Length)]);
        }
    }

    private static int[] ReadEntrySizes(byte[] icon)
    {
        var sizes = new int[5];
        for (var index = 0; index < sizes.Length; index++)
        {
            sizes[index] = icon[6 + (index * 16)];
        }

        return sizes;
    }
}
