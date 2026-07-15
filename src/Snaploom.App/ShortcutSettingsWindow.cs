using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed class ShortcutSettingsWindow : Window
{
    private readonly ScreenshotHotKeyManager _hotKeyManager;
    private readonly TextBlock _shortcutText;
    private readonly TextBlock _statusText;
    private ScreenshotHotKey _candidate;

    public ShortcutSettingsWindow(ScreenshotHotKeyManager hotKeyManager)
    {
        ArgumentNullException.ThrowIfNull(hotKeyManager);
        _hotKeyManager = hotKeyManager;
        _candidate = hotKeyManager.CurrentHotKey;

        Title = "Snaploom 快捷键设置";
        Width = 440;
        Height = 250;
        CanResize = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _shortcutText = new TextBlock
        {
            Text = _candidate.ToString(),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _statusText = new TextBlock
        {
            Text = hotKeyManager.IsRegistered
                ? "请按下包含至少一个修饰键的新快捷键。"
                : "当前快捷键已被其他应用占用，请设置新的快捷键。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        var saveButton = new Button
        {
            Content = "应用",
            MinWidth = 80,
        };
        saveButton.Click += HandleSave;
        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 80,
        };
        closeButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = "截图快捷键",
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new Border
                {
                    Padding = new Thickness(14),
                    Child = _shortcutText,
                },
                _statusText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { closeButton, saveButton },
                },
            },
        };

        KeyDown += HandleKeyDown;
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Enum.TryParse<ScreenshotHotKeyKey>(e.Key.ToString(), out var key))
        {
            return;
        }

        var modifiers = ScreenshotHotKeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= ScreenshotHotKeyModifiers.Alt;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= ScreenshotHotKeyModifiers.Control;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= ScreenshotHotKeyModifiers.Shift;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= ScreenshotHotKeyModifiers.Command;
        }

        if (modifiers == ScreenshotHotKeyModifiers.None)
        {
            _statusText.Text = "快捷键必须包含 Alt、Control、Shift 或 Command。";
            return;
        }

        _candidate = new ScreenshotHotKey(modifiers, key);
        _shortcutText.Text = _candidate.ToString();
        _statusText.Text = "点击“应用”保存新快捷键。";
        e.Handled = true;
    }

    private void HandleSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = _hotKeyManager.TryChange(_candidate);
        _statusText.Text = result == ScreenshotHotKeyChangeResult.Success
            ? "快捷键已生效。"
            : "该快捷键已被其他应用占用，原快捷键已恢复。";
    }
}
