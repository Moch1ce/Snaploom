using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed class ScreenCapturePermissionWindow : Window
{
    private readonly IScreenCapturePermissionService _permissionService;
    private readonly TextBlock _statusText;

    public ScreenCapturePermissionWindow(IScreenCapturePermissionService permissionService)
    {
        ArgumentNullException.ThrowIfNull(permissionService);
        _permissionService = permissionService;

        Title = AppUiText.PermissionTitle;
        Width = 480;
        Height = 235;
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AppWindowChrome.Configure(this);

        _statusText = new TextBlock
        {
            Text = AppUiText.PermissionDescription,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14,
        };

        var requestButton = new Button
        {
            Content = AppUiText.ContinueAuthorization,
            MinWidth = 96,
        };
        AppUiTheme.StylePrimaryButton(requestButton);
        requestButton.Click += HandleRequestPermission;

        var settingsButton = new Button
        {
            Content = AppUiText.OpenSystemSettings,
            MinWidth = 110,
        };
        AppUiTheme.StyleSecondaryButton(settingsButton);
        settingsButton.Click += (_, _) =>
        {
            _permissionService.OpenPermissionSettings();
            _statusText.Text = AppUiText.PermissionRestartHint;
        };

        var cancelButton = new Button
        {
            Content = AppUiText.Later,
            MinWidth = 76,
        };
        AppUiTheme.StyleSecondaryButton(cancelButton);
        cancelButton.Click += (_, _) => Close();

        Content = AppWindowChrome.Wrap(
            this,
            new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 22,
                Children =
                {
                    _statusText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancelButton, settingsButton, requestButton },
                    },
                },
            });
    }

    private void HandleRequestPermission(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_permissionService.RequestPermission() ||
            _permissionService.GetPermissionStatus() == ScreenCapturePermissionStatus.Granted)
        {
            Close();
            return;
        }

        _statusText.Text = AppUiText.PermissionNotGranted;
    }
}
