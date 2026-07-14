using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Snaploom.App;

public sealed class MessageWindow : Window
{
    public MessageWindow(string title, string message)
    {
        Title = title;
        Width = 420;
        Height = 190;
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var closeButton = new Button
        {
            Content = "知道了",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 88,
        };
        closeButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                },
                closeButton,
            },
        };
    }
}
