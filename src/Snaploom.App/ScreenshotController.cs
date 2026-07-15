using Avalonia.Threading;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed class ScreenshotController : IDisposable
{
    private readonly IScreenCapturePermissionService _permissionService;
    private readonly IScreenCaptureService _captureService;
    private readonly IPngSaveDialogService _saveDialogService;
    private readonly IScreenshotClipboardService _clipboardService;
    private readonly IScreenshotOverlayConfigurator _overlayConfigurator;
    private readonly AppSettingsService _settings;
    private readonly PrivacyLog _log;
    private readonly ScreenshotActivationGate _activationGate = new();
    private ScreenshotOverlayWindow? _overlay;
    private CaptureFailureOverlayWindow? _failureOverlay;
    private ScreenCapturePermissionWindow? _permissionWindow;
    private bool _disposed;

    private ScreenshotController(
        IScreenCapturePermissionService permissionService,
        IScreenCaptureService captureService,
        IPngSaveDialogService saveDialogService,
        IScreenshotClipboardService clipboardService,
        IScreenshotOverlayConfigurator overlayConfigurator,
        AppSettingsService settings,
        PrivacyLog log)
    {
        _permissionService = permissionService;
        _captureService = captureService;
        _saveDialogService = saveDialogService;
        _clipboardService = clipboardService;
        _overlayConfigurator = overlayConfigurator;
        _settings = settings;
        _log = log;
    }

    public static ScreenshotController? TryCreate(
        IDesktopPlatform platform,
        AppSettingsService settings,
        PrivacyLog log)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);

        return platform is IScreenCapturePermissionService permissionService &&
               platform is IScreenCaptureService captureService &&
               platform is IPngSaveDialogService saveDialogService &&
               platform is IScreenshotClipboardService clipboardService &&
               platform is IScreenshotOverlayConfigurator overlayConfigurator
            ? new ScreenshotController(
                permissionService,
                captureService,
                saveDialogService,
                clipboardService,
                overlayConfigurator,
                settings,
                log)
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
                _log.Error(AppLogEvent.PermissionMissing);
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
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (exception.PermissionDenied)
                    {
                        _activationGate.End();
                        ShowPermissionGuide();
                    }
                    else
                    {
                        _log.Error(AppLogEvent.CaptureFailed, exception);
                        ShowCaptureFailure(AppUiText.CaptureSystemFailure);
                    }
                });
        }
        catch (Exception exception)
        {
            _log.Error(AppLogEvent.UnexpectedCaptureFailure, exception);
            await Dispatcher.UIThread.InvokeAsync(
                () => ShowCaptureFailure(AppUiText.CaptureUnexpectedFailure));
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
        _failureOverlay?.Close();
        _failureOverlay = null;
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

        _overlay = new ScreenshotOverlayWindow(
            capturedScreen,
            _saveDialogService,
            _clipboardService,
            _overlayConfigurator,
            _settings,
            _log);
        _overlay.Closed += (_, _) =>
        {
            _overlay = null;
            _activationGate.End();
        };
        _overlay.Show();
    }

    private void ShowCaptureFailure(string message)
    {
        if (_disposed)
        {
            _activationGate.End();
            return;
        }

        _failureOverlay = new CaptureFailureOverlayWindow(message);
        _failureOverlay.Closed += (_, _) =>
        {
            _failureOverlay = null;
            _activationGate.End();
        };
        _failureOverlay.Show();
    }
}
