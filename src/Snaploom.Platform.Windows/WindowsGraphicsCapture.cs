#if WINDOWS
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static Vortice.Direct3D11.D3D11;

namespace Snaploom.Platform.Windows;

internal static class WindowsGraphicsCapture
{
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    ];

    internal static async Task<CapturedScreen> CaptureCurrentDisplayAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported())
        {
            return WindowsDisplayCapture.CaptureCurrentDisplay(cancellationToken);
        }

        var target = WindowsDisplayCapture.GetCurrentDisplayTarget();
        try
        {
            return await CaptureWithGraphicsCaptureAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception graphicsCaptureException)
        {
            try
            {
                return WindowsDisplayCapture.CaptureCurrentDisplay(cancellationToken);
            }
            catch (Exception fallbackException)
            {
                throw new ScreenCaptureException(
                    $"Windows Graphics Capture failed ({graphicsCaptureException.GetType().Name}) and the Win32 compatibility capture also failed: {fallbackException.Message}");
            }
        }
    }

    private static async Task<CapturedScreen> CaptureWithGraphicsCaptureAsync(
        WindowsCaptureTarget target,
        CancellationToken cancellationToken)
    {
        D3D11CreateDevice(
            adapter: null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels,
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();
        using (device)
        using (context)
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
        using (var direct3DDevice = CreateDirect3D11DeviceFromDXGIDevice<IDirect3DDevice>(dxgiDevice))
        {
            var item = CreateItemForMonitor(target.Monitor);
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                numberOfBuffers: 1,
                item.Size);
            using var session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            var frameSource = new TaskCompletionSource<Direct3D11CaptureFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void HandleFrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                try
                {
                    var frame = sender.TryGetNextFrame();
                    if (frame is not null && !frameSource.TrySetResult(frame))
                    {
                        frame.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    frameSource.TrySetException(exception);
                }
            }

            framePool.FrameArrived += HandleFrameArrived;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
                using var registration = linkedCancellation.Token.Register(
                    () => frameSource.TrySetCanceled(linkedCancellation.Token));
                session.StartCapture();

                Direct3D11CaptureFrame frame;
                try
                {
                    frame = await frameSource.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new ScreenCaptureException("Windows Graphics Capture timed out while waiting for a display frame.");
                }

                using (frame)
                {
                    var pixels = ReadFramePixels(device, context, frame);
                    var width = frame.ContentSize.Width;
                    var height = frame.ContentSize.Height;
                    var logicalSize = width == target.Width && height == target.Height
                        ? target.LogicalSize
                        : new LogicalSize(
                            target.LogicalSize.Width * width / target.Width,
                            target.LogicalSize.Height * height / target.Height);
                    var capturedFrame = new CapturedFrame(
                        new PhysicalSize(width, height),
                        logicalSize,
                        checked(width * 4),
                        pixels);
                    return WindowsDisplayCapture.CreateCapturedScreen(capturedFrame, target);
                }
            }
            finally
            {
                framePool.FrameArrived -= HandleFrameArrived;
            }
        }
    }

    private static byte[] ReadFramePixels(
        ID3D11Device device,
        ID3D11DeviceContext context,
        Direct3D11CaptureFrame frame)
    {
        using var dxgiSurface = GetDXGISurface(frame.Surface);
        using var sourceTexture = dxgiSurface.QueryInterface<ID3D11Texture2D>();
        var sourceDescription = sourceTexture.Description;
        var stagingDescription = sourceDescription;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;

        using var stagingTexture = device.CreateTexture2D(stagingDescription);
        context.CopyResource(stagingTexture, sourceTexture);
        context.Map(
            stagingTexture,
            subresource: 0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None,
            out var mapped).CheckError();
        try
        {
            var width = frame.ContentSize.Width;
            var height = frame.ContentSize.Height;
            var bytesPerRow = checked(width * 4);
            var pixels = new byte[checked(bytesPerRow * height)];
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    mapped.DataPointer + checked((int)(row * mapped.RowPitch)),
                    pixels,
                    row * bytesPerRow,
                    bytesPerRow);
            }

            return pixels;
        }
        finally
        {
            context.Unmap(stagingTexture, 0);
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(nint monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(monitor, GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            _ = Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);

        nint CreateForMonitor(nint monitor, in Guid iid);
    }
}
#endif
