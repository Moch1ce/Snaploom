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
    private readonly IScreenshotClipboardService _clipboardService;
    private readonly IScreenshotOverlayConfigurator _overlayConfigurator;
    private readonly ScreenshotSelectionCanvas _selectionCanvas;
    private readonly TextBlock _sizeText;
    private readonly Border _sizeBadge;
    private readonly ScreenshotToolbar _toolbar;
    private readonly TranslateTransform _sizeBadgeTransform = new();
    private readonly TranslateTransform _toolbarTransform = new();
    private Rect _availableUiBounds;
    private bool _resourcesDisposed;

    public ScreenshotOverlayWindow(
        CapturedScreen capturedScreen,
        IPngSaveDialogService saveDialogService,
        IScreenshotClipboardService clipboardService,
        IScreenshotOverlayConfigurator overlayConfigurator)
    {
        ArgumentNullException.ThrowIfNull(capturedScreen);
        ArgumentNullException.ThrowIfNull(saveDialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(overlayConfigurator);
        _capturedScreen = capturedScreen;
        _saveDialogService = saveDialogService;
        _clipboardService = clipboardService;
        _overlayConfigurator = overlayConfigurator;
        _availableUiBounds = new Rect(
            new Size(
                capturedScreen.Frame.LogicalSize.Width,
                capturedScreen.Frame.LogicalSize.Height));

        Title = ScreenshotUiText.WindowTitle;
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
        _selectionCanvas.SelectionDoubleClicked += HandleConfirm;

        _sizeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sizeBadge = new Border
        {
            Background = ScreenshotUiTheme.SizeBadgeBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransform = _sizeBadgeTransform,
            Child = _sizeText,
        };

        _toolbar = new ScreenshotToolbar
        {
            RenderTransform = _toolbarTransform,
        };
        _toolbar.SaveRequested += HandleSave;
        _toolbar.ConfirmRequested += HandleConfirm;
        _toolbar.CancelRequested += HandleCancel;

        var root = new Grid();
        root.Children.Add(_selectionCanvas);
        root.Children.Add(_sizeBadge);
        root.Children.Add(_toolbar);
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
            var screenBounds = screen.Bounds;
            var workingArea = screen.WorkingArea;
            var logicalPerPhysicalX = _capturedScreen.Frame.LogicalSize.Width / screenBounds.Width;
            var logicalPerPhysicalY = _capturedScreen.Frame.LogicalSize.Height / screenBounds.Height;
            _availableUiBounds = new Rect(
                (workingArea.X - screenBounds.X) * logicalPerPhysicalX,
                (workingArea.Y - screenBounds.Y) * logicalPerPhysicalY,
                workingArea.Width * logicalPerPhysicalX,
                workingArea.Height * logicalPerPhysicalY);
        }

        Activate();
        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is not null)
        {
            _overlayConfigurator.ConfigureScreenshotOverlay(platformHandle.Handle);
        }

        _selectionCanvas.Focus();
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }

    }

    private void HandleSelectionChanged(object? sender, EventArgs e)
    {
        if (_selectionCanvas.Session.Selection is { } selection &&
            _selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            _sizeBadge.IsVisible = true;
            _sizeText.Text = $"{selection.Width} × {selection.Height}";
            var isSelected = _selectionCanvas.Session.State == ScreenshotSessionState.Selected;
            _toolbar.IsVisible = isSelected;
            _toolbar.SetSelectionActionsEnabled(isSelected);
            PositionFloatingUi(logicalSelection);
            return;
        }

        _sizeBadge.IsVisible = false;
        _toolbar.IsVisible = false;
        _toolbar.SetSelectionActionsEnabled(isEnabled: false);
    }

    private void PositionFloatingUi(Rect selection)
    {
        var availableSize = _selectionCanvas.Bounds.Size;
        _sizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var placement = ScreenshotFloatingUiLayout.Place(
            selection,
            _availableUiBounds.Intersect(new Rect(availableSize)),
            _sizeBadge.DesiredSize,
            _toolbar.DesiredSize);
        _sizeBadgeTransform.X = placement.BadgeOrigin.X;
        _sizeBadgeTransform.Y = placement.BadgeOrigin.Y;
        _toolbarTransform.X = placement.ToolbarOrigin.X;
        _toolbarTransform.Y = placement.ToolbarOrigin.Y;
    }

    private async void HandleSave(object? sender, EventArgs e)
    {
        if (_selectionCanvas.Session.Selection is not { } selection ||
            _selectionCanvas.Session.State != ScreenshotSessionState.Selected)
        {
            return;
        }

        _selectionCanvas.Session.BeginSave();
        _toolbar.SetSelectionActionsEnabled(isEnabled: false);
        _sizeText.Text = ScreenshotUiText.ChoosingSaveLocation;
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }

        try
        {
            var suggestedName = $"Snaploom_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var path = _saveDialogService.ShowSaveDialog(suggestedName);
            if (path is null)
            {
                _selectionCanvas.Session.CancelSave();
                RestoreSelectedUi(selection);
                return;
            }

            await File.WriteAllBytesAsync(path, EncodeSelection(selection));
            Close();
        }
        catch (Exception exception)
        {
            if (_selectionCanvas.Session.State == ScreenshotSessionState.Saving)
            {
                _selectionCanvas.Session.CancelSave();
            }

            _toolbar.SetSelectionActionsEnabled(isEnabled: true);
            _sizeText.Text = ScreenshotUiText.SaveFailed;
            ToolTip.SetTip(_sizeBadge, exception.Message);
        }
    }

    private void HandleConfirm(object? sender, EventArgs e)
    {
        if (_selectionCanvas.Session.Selection is not { } selection ||
            _selectionCanvas.Session.State != ScreenshotSessionState.Selected)
        {
            return;
        }

        CopySelection(selection, closeAfterCopy: true);
    }

    private void CopySelection(PhysicalRect selection, bool closeAfterCopy)
    {
        _toolbar.SetSelectionActionsEnabled(isEnabled: false);
        _sizeText.Text = ScreenshotUiText.CopyingImage;
        try
        {
            _clipboardService.CopyPng(EncodeSelection(selection));
            if (closeAfterCopy)
            {
                Close();
            }
            else
            {
                RestoreSelectedUi(selection);
            }
        }
        catch (Exception exception)
        {
            _toolbar.SetSelectionActionsEnabled(isEnabled: true);
            _sizeText.Text = ScreenshotUiText.CopyImageFailed;
            ToolTip.SetTip(_sizeBadge, exception.Message);
        }
    }

    private void RestoreSelectedUi(PhysicalRect selection)
    {
        _toolbar.SetSelectionActionsEnabled(isEnabled: true);
        _sizeText.Text = $"{selection.Width} × {selection.Height}";
        ToolTip.SetTip(_sizeBadge, value: null);
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }
    }

    private void HandleCancel(object? sender, EventArgs e) => Close();

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key == Key.Enter &&
            _selectionCanvas.Session.State == ScreenshotSessionState.Selected &&
            _selectionCanvas.Session.Selection is { } enterSelection)
        {
            e.Handled = true;
            CopySelection(enterSelection, closeAfterCopy: true);
            return;
        }

        var copyModifierPressed = OperatingSystem.IsMacOS()
            ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key != Key.C || !copyModifierPressed)
        {
            return;
        }

        e.Handled = true;
        if (_selectionCanvas.Session.State == ScreenshotSessionState.Selected &&
            _selectionCanvas.Session.Selection is { } copySelection)
        {
            CopySelection(copySelection, closeAfterCopy: true);
            return;
        }

        if (_selectionCanvas.SampledColor is not { } color)
        {
            return;
        }

        try
        {
            _clipboardService.CopyText(color.Hex);
        }
        catch (Exception exception)
        {
            _sizeText.Text = ScreenshotUiText.CopyColorFailed;
            ToolTip.SetTip(_sizeBadge, exception.Message);
        }
    }

    private byte[] EncodeSelection(PhysicalRect selection) =>
        SelectionPngEncoder.Encode(_capturedScreen.Frame, selection);

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _toolbar.SaveRequested -= HandleSave;
        _toolbar.ConfirmRequested -= HandleConfirm;
        _toolbar.CancelRequested -= HandleCancel;
        _selectionCanvas.SelectionChanged -= HandleSelectionChanged;
        _selectionCanvas.SelectionDoubleClicked -= HandleConfirm;
        _selectionCanvas.Dispose();
        _capturedScreen.Dispose();
    }
}
