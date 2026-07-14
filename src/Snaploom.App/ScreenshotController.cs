using Avalonia.Threading;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed class ScreenshotController : IDisposable
{
    private readonly IScreenCapturePermissionService _permissionService;
    private readonly IScreenCaptureService _captureService;
    private readonly IPngSaveDialogService _saveDialogService;
    private readonly ScreenshotActivationGate _activationGate = new();
    private ScreenshotOverlayWindow? _overlay;
    private ScreenCapturePermissionWindow? _permissionWindow;
    private bool _disposed;

    private ScreenshotController(
        IScreenCapturePermissionService permissionService,
        IScreenCaptureService captureService,
        IPngSaveDialogService saveDialogService)
    {
        _permissionService = permissionService;
        _captureService = captureService;
        _saveDialogService = saveDialogService;
    }

    public static ScreenshotController? TryCreate(IDesktopPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        return platform is IScreenCapturePermissionService permissionService &&
               platform is IScreenCaptureService captureService &&
               platform is IPngSaveDialogService saveDialogService
            ? new ScreenshotController(permissionService, captureService, saveDialogService)
            : null;
    }

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_activationGate.TryBegin())
        {
            return;
        }

        CapturedScreen? capturedScreen = null;
        try
        {
            if (_permissionService.GetPermissionStatus() != ScreenCapturePermissionStatus.Granted)
            {
                ShowPermissionGuide();
                _activationGate.End();
                return;
            }

            capturedScreen = await _captureService.CaptureCurrentDisplayAsync();
            await Dispatcher.UIThread.InvokeAsync(() => ShowOverlay(capturedScreen));
            capturedScreen = null;
        }
        catch (ScreenCaptureException exception)
        {
            _activationGate.End();
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (exception.PermissionDenied)
                    {
                        ShowPermissionGuide();
                    }
                    else
                    {
                        new MessageWindow("截图失败", exception.Message).Show();
                    }
                });
        }
        catch (Exception exception)
        {
            _activationGate.End();
            await Dispatcher.UIThread.InvokeAsync(
                () => new MessageWindow("截图失败", exception.Message).Show());
        }
        finally
        {
            capturedScreen?.Dispose();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _overlay?.Dispose();
        _overlay = null;
        _permissionWindow?.Close();
        _permissionWindow = null;
    }

    private void ShowPermissionGuide()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_permissionWindow is not null)
                {
                    _permissionWindow.Activate();
                    return;
                }

                _permissionWindow = new ScreenCapturePermissionWindow(_permissionService);
                _permissionWindow.Closed += (_, _) => _permissionWindow = null;
                _permissionWindow.Show();
            });
    }

    private void ShowOverlay(CapturedScreen capturedScreen)
    {
        if (_disposed)
        {
            capturedScreen.Dispose();
            _activationGate.End();
            return;
        }

        _overlay = new ScreenshotOverlayWindow(capturedScreen, _saveDialogService);
        _overlay.Closed += (_, _) =>
        {
            _overlay = null;
            _activationGate.End();
        };
        _overlay.Show();
    }
}
