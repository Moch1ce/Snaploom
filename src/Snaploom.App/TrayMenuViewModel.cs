using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Security;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed partial class TrayMenuViewModel : ObservableObject
{
    private readonly Func<Task> _startScreenshot;
    private readonly Action _openShortcutSettings;
    private readonly IAutoStartService? _autoStartService;
    private readonly Action _requestShutdown;

    public TrayMenuViewModel(
        Func<Task> startScreenshot,
        Action openShortcutSettings,
        IAutoStartService? autoStartService,
        Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(startScreenshot);
        ArgumentNullException.ThrowIfNull(openShortcutSettings);
        ArgumentNullException.ThrowIfNull(requestShutdown);
        _startScreenshot = startScreenshot;
        _openShortcutSettings = openShortcutSettings;
        _autoStartService = autoStartService;
        _requestShutdown = requestShutdown;
        IsAutoStartEnabled = ReadAutoStartState(autoStartService);
    }

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    [RelayCommand]
    private Task StartScreenshotAsync() => _startScreenshot();

    [RelayCommand]
    private void OpenShortcutSettings() => _openShortcutSettings();

    [RelayCommand]
    private void ToggleAutoStart()
    {
        if (_autoStartService is null)
        {
            return;
        }

        var enabled = !IsAutoStartEnabled;
        try
        {
            _autoStartService.SetAutoStartEnabled(enabled);
            IsAutoStartEnabled = enabled;
        }
        catch (InvalidOperationException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }
    }

    [RelayCommand]
    private void Exit() => _requestShutdown();

    private static bool ReadAutoStartState(IAutoStartService? autoStartService)
    {
        try
        {
            return autoStartService?.IsAutoStartEnabled() == true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }
}
