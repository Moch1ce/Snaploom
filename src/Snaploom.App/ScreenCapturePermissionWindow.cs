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

        Title = "允许 Snaploom 录制屏幕";
        Width = 480;
        Height = 235;
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _statusText = new TextBlock
        {
            Text = "截图需要 macOS 的“屏幕与系统音频录制”权限。Snaploom 只在你主动截图时读取屏幕。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14,
        };

        var requestButton = new Button
        {
            Content = "继续授权",
            MinWidth = 96,
        };
        requestButton.Click += HandleRequestPermission;

        var settingsButton = new Button
        {
            Content = "打开系统设置",
            MinWidth = 110,
        };
        settingsButton.Click += (_, _) =>
        {
            _permissionService.OpenPermissionSettings();
            _statusText.Text = "授权后若仍无法截图，请退出并重新打开 Snaploom，再按 Command+Shift+A。";
        };

        var cancelButton = new Button
        {
            Content = "稍后",
            MinWidth = 76,
        };
        cancelButton.Click += (_, _) => Close();

        Content = new StackPanel
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
        };
    }

    private void HandleRequestPermission(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_permissionService.RequestPermission() ||
            _permissionService.GetPermissionStatus() == ScreenCapturePermissionStatus.Granted)
        {
            Close();
            return;
        }

        _statusText.Text = "权限尚未开启。请点击“打开系统设置”，允许 Snaploom 录制屏幕后重新启动应用。";
    }
}
