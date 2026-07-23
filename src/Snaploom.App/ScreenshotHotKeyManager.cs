using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public enum ScreenshotHotKeyChangeResult
{
    Success,
    Conflict,
}

public sealed class ScreenshotHotKeyManager : IDisposable
{
    private readonly IGlobalScreenshotHotKeyService _hotKeyService;
    private readonly ISystemResumeService _resumeService;
    private readonly Action _callback;
    private bool _monitoring;
    private bool _disposed;

    public ScreenshotHotKeyManager(
        IGlobalScreenshotHotKeyService hotKeyService,
        ISystemResumeService resumeService,
        ScreenshotHotKey initialHotKey,
        Action callback)
    {
        ArgumentNullException.ThrowIfNull(hotKeyService);
        ArgumentNullException.ThrowIfNull(resumeService);
        ArgumentNullException.ThrowIfNull(callback);
        _hotKeyService = hotKeyService;
        _resumeService = resumeService;
        _callback = callback;
        CurrentHotKey = initialHotKey;
    }

    public ScreenshotHotKey CurrentHotKey { get; private set; }

    public bool IsRegistered { get; private set; }

    public event EventHandler? RegistrationFailed;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_monitoring)
        {
            _resumeService.StartMonitoring(HandleResumed);
            _monitoring = true;
        }

        _hotKeyService.UnregisterScreenshotHotKey();
        IsRegistered = _hotKeyService.TryRegisterScreenshotHotKey(CurrentHotKey, _callback);
        return IsRegistered;
    }

    public ScreenshotHotKeyChangeResult TryChange(ScreenshotHotKey hotKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (hotKey == CurrentHotKey && IsRegistered)
        {
            return ScreenshotHotKeyChangeResult.Success;
        }

        var previous = CurrentHotKey;
        _hotKeyService.UnregisterScreenshotHotKey();
        if (_hotKeyService.TryRegisterScreenshotHotKey(hotKey, _callback))
        {
            CurrentHotKey = hotKey;
            IsRegistered = true;
            return ScreenshotHotKeyChangeResult.Success;
        }

        IsRegistered = _hotKeyService.TryRegisterScreenshotHotKey(previous, _callback);
        return ScreenshotHotKeyChangeResult.Conflict;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotKeyService.UnregisterScreenshotHotKey();
        IsRegistered = false;
        if (_monitoring)
        {
            _resumeService.StopMonitoring();
            _monitoring = false;
        }
    }

    private void HandleResumed()
    {
        if (_disposed)
        {
            return;
        }

        _hotKeyService.UnregisterScreenshotHotKey();
        IsRegistered = _hotKeyService.TryRegisterScreenshotHotKey(CurrentHotKey, _callback);
        if (!IsRegistered)
        {
            RegistrationFailed?.Invoke(this, EventArgs.Empty);
        }
    }
}
