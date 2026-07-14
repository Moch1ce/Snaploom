using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Snaploom.App;

public sealed partial class TrayMenuViewModel : ObservableObject
{
    private readonly Action _requestShutdown;

    public TrayMenuViewModel(Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(requestShutdown);
        _requestShutdown = requestShutdown;
    }

    [RelayCommand]
    private void Exit() => _requestShutdown();
}
