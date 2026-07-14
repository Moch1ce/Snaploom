using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Snaploom.App;

public sealed partial class TrayMenuViewModel : ObservableObject
{
    private readonly Func<Task> _startScreenshot;
    private readonly Action _requestShutdown;

    public TrayMenuViewModel(Func<Task> startScreenshot, Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(startScreenshot);
        ArgumentNullException.ThrowIfNull(requestShutdown);
        _startScreenshot = startScreenshot;
        _requestShutdown = requestShutdown;
    }

    [RelayCommand]
    private Task StartScreenshotAsync() => _startScreenshot();

    [RelayCommand]
    private void Exit() => _requestShutdown();
}
