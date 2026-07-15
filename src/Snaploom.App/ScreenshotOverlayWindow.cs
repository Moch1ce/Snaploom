using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using Snaploom.Rendering;

namespace Snaploom.App;

public sealed class ScreenshotOverlayWindow : Window, IDisposable
{
    private readonly CapturedScreen _capturedScreen;
    private readonly IPngSaveDialogService _saveDialogService;
    private readonly IScreenshotOverlayConfigurator _overlayConfigurator;
    private readonly ScreenshotSelectionCanvas _selectionCanvas;
    private readonly Button _saveButton;
    private readonly TextBlock _statusText;
    private bool _resourcesDisposed;

    public ScreenshotOverlayWindow(
        CapturedScreen capturedScreen,
        IPngSaveDialogService saveDialogService,
        IScreenshotOverlayConfigurator overlayConfigurator)
    {
        ArgumentNullException.ThrowIfNull(capturedScreen);
        ArgumentNullException.ThrowIfNull(saveDialogService);
        ArgumentNullException.ThrowIfNull(overlayConfigurator);
        _capturedScreen = capturedScreen;
        _saveDialogService = saveDialogService;
        _overlayConfigurator = overlayConfigurator;

        Title = "Snaploom 截图";
        Width = capturedScreen.Frame.LogicalSize.Width;
        Height = capturedScreen.Frame.LogicalSize.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        SizeToContent = SizeToContent.Manual;
        Background = Brushes.Black;

        _selectionCanvas = new ScreenshotSelectionCanvas(capturedScreen.Frame);
        _selectionCanvas.SelectionChanged += HandleSelectionChanged;

        _statusText = new TextBlock
        {
            Text = "拖动鼠标选择截图区域，按 Esc 退出",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _saveButton = new Button
        {
            Content = "保存 PNG",
            IsEnabled = false,
            MinWidth = 92,
        };
        _saveButton.Click += HandleSave;

        var cancelButton = new Button
        {
            Content = "退出",
            MinWidth = 68,
        };
        cancelButton.Click += (_, _) => Close();

        var toolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 9),
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { _statusText, cancelButton, _saveButton },
            },
        };

        var root = new Grid();
        root.Children.Add(_selectionCanvas);
        root.Children.Add(toolbar);
        Content = root;

        Opened += HandleOpened;
        KeyDown += HandleKeyDown;
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeResources();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (IsVisible)
        {
            Close();
        }
        else
        {
            DisposeResources();
        }
    }

    private void HandleOpened(object? sender, EventArgs e)
    {
        var cursor = _capturedScreen.CursorPosition;
        var screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
        }

        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is not null)
        {
            _overlayConfigurator.ConfigureScreenshotOverlay(platformHandle.Handle);
        }

        Activate();
        _selectionCanvas.Focus();
    }

    private void HandleSelectionChanged(object? sender, EventArgs e)
    {
        if (_selectionCanvas.Session.Selection is { } selection &&
            _selectionCanvas.Session.State == ScreenshotSessionState.Selected)
        {
            _saveButton.IsEnabled = true;
            _statusText.Text = $"{selection.Width} × {selection.Height} 像素";
            return;
        }

        _saveButton.IsEnabled = false;
        _statusText.Text = _selectionCanvas.Session.State == ScreenshotSessionState.Selecting
            ? "松开鼠标完成选区"
            : $"选区至少需要 {ScreenshotSession.MinimumSelectionSize} × {ScreenshotSession.MinimumSelectionSize} 像素";
    }

    private async void HandleSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectionCanvas.Session.Selection is not { } selection ||
            _selectionCanvas.Session.State != ScreenshotSessionState.Selected)
        {
            return;
        }

        _selectionCanvas.Session.BeginSave();
        _saveButton.IsEnabled = false;
        _statusText.Text = "选择保存位置…";

        try
        {
            var suggestedName = $"Snaploom_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var path = _saveDialogService.ShowSaveDialog(suggestedName);
            if (path is null)
            {
                _selectionCanvas.Session.CancelSave();
                _saveButton.IsEnabled = true;
                _statusText.Text = $"{selection.Width} × {selection.Height} 像素";
                return;
            }

            var png = SelectionPngEncoder.Encode(_capturedScreen.Frame, selection);
            await File.WriteAllBytesAsync(path, png);
            Close();
        }
        catch (Exception exception)
        {
            if (_selectionCanvas.Session.State == ScreenshotSessionState.Saving)
            {
                _selectionCanvas.Session.CancelSave();
            }

            _saveButton.IsEnabled = true;
            _statusText.Text = $"保存失败：{exception.Message}";
        }
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

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _selectionCanvas.SelectionChanged -= HandleSelectionChanged;
        _selectionCanvas.Dispose();
        _capturedScreen.Dispose();
    }
}
