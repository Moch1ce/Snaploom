using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Snaploom.App;

public sealed class CaptureFailureOverlayWindow : Window
{
    public CaptureFailureOverlayWindow(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Title = "Snaploom 截图失败";
        Width = 640;
        Height = 360;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = new SolidColorBrush(Color.FromArgb(235, 15, 15, 15));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var exitButton = new Button
        {
            Content = "退出截图",
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 96,
        };
        exitButton.Click += (_, _) => Close();

        Content = new Grid
        {
            Children =
            {
                new StackPanel
                {
                    Width = 460,
                    Spacing = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "无法截取当前屏幕",
                            Foreground = Brushes.White,
                            FontSize = 22,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = message,
                            Foreground = Brushes.White,
                            FontSize = 14,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        exitButton,
                    },
                },
            },
        };

        Opened += HandleOpened;
        KeyDown += HandleKeyDown;
    }

    private void HandleOpened(object? sender, EventArgs e)
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
        Activate();
        Focus();
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }
}
