using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Snaploom.App;

internal sealed class ScreenshotToolbar : Border
{
    private readonly ScreenshotToolbarButton _saveButton;
    private readonly ScreenshotToolbarButton _confirmButton;

    internal ScreenshotToolbar()
    {
        Height = ScreenshotUiTheme.ToolbarHeight;
        Background = ScreenshotUiTheme.FloatingSurfaceBrush;
        BorderBrush = ScreenshotUiTheme.FloatingBorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(ScreenshotUiTheme.FloatingCornerRadius);
        BoxShadow = ScreenshotUiTheme.FloatingShadow;
        Padding = new Thickness(ScreenshotUiTheme.ToolbarHorizontalPadding, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        IsVisible = false;

        var rectangleButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Rectangle,
            ScreenshotUiText.RectangleUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);
        var arrowButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Arrow,
            ScreenshotUiText.ArrowUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);
        var textButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Text,
            ScreenshotUiText.TextUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);
        var mosaicButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Mosaic,
            ScreenshotUiText.MosaicUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);
        var undoButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Undo,
            ScreenshotUiText.UndoUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);

        _saveButton = CreateButton(
            ScreenshotToolbarIconKind.Save,
            ScreenshotUiText.SavePng,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _saveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);

        var cancelButton = CreateButton(
            ScreenshotToolbarIconKind.Cancel,
            ScreenshotUiText.ExitScreenshot,
            ScreenshotUiTheme.DangerBrush);
        cancelButton.Invoked += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        _confirmButton = CreateButton(
            ScreenshotToolbarIconKind.Confirm,
            ScreenshotUiText.CompleteAndCopy,
            ScreenshotUiTheme.AccentBrush,
            isEnabled: false);
        _confirmButton.Invoked += (_, _) => ConfirmRequested?.Invoke(this, EventArgs.Empty);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(rectangleButton);
        content.Children.Add(arrowButton);
        content.Children.Add(textButton);
        content.Children.Add(mosaicButton);
        content.Children.Add(CreateSeparator());
        content.Children.Add(undoButton);
        content.Children.Add(_saveButton);
        content.Children.Add(CreateSeparator());
        content.Children.Add(cancelButton);
        content.Children.Add(_confirmButton);
        Child = content;
    }

    internal event EventHandler? SaveRequested;

    internal event EventHandler? ConfirmRequested;

    internal event EventHandler? CancelRequested;

    internal void SetSelectionActionsEnabled(bool isEnabled)
    {
        _saveButton.SetEnabled(isEnabled);
        _confirmButton.SetEnabled(isEnabled);
    }

    private static ScreenshotToolbarButton CreateButton(
        ScreenshotToolbarIconKind icon,
        string tooltip,
        IBrush iconBrush,
        bool isEnabled = true)
        => new(icon, tooltip, iconBrush, isEnabled);

    private static ScreenshotToolbarButton CreateUnavailableButton(
        ScreenshotToolbarIconKind icon,
        string tooltip,
        IBrush iconBrush)
    {
        var button = CreateButton(icon, tooltip, iconBrush);
        button.IsHitTestVisible = false;
        button.Opacity = 0.38;
        return button;
    }

    private static Border CreateSeparator() =>
        new()
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(ScreenshotUiTheme.ToolbarSeparatorMargin, 0),
            Background = ScreenshotUiTheme.SeparatorBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
}

internal sealed class ScreenshotToolbarButton : Border
{
    private readonly Border _selectedBackground;
    private bool _isSelected;
    private bool _isPressed;

    internal ScreenshotToolbarButton(
        ScreenshotToolbarIconKind icon,
        string tooltip,
        IBrush iconBrush,
        bool isEnabled)
    {
        Width = ScreenshotUiTheme.ToolbarButtonSize;
        Height = ScreenshotUiTheme.ToolbarButtonSize;
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;

        _selectedBackground = new Border
        {
            Width = ScreenshotUiTheme.SelectedToolBackgroundSize,
            Height = ScreenshotUiTheme.SelectedToolBackgroundSize,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new ScreenshotToolbarIcon(icon, iconBrush),
        };
        Child = _selectedBackground;
        ToolTip.SetTip(this, tooltip);
        SetEnabled(isEnabled);
    }

    internal event EventHandler? Invoked;

    internal bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            UpdateVisualState();
        }
    }

    internal void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        Opacity = isEnabled ? 1 : 0.38;
        UpdateVisualState();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdateVisualState();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPressed = false;
        UpdateVisualState();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isPressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        e.Pointer.Capture(control: null);
        e.Handled = true;
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            Invoked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateVisualState()
    {
        _selectedBackground.Background = _isSelected
            ? ScreenshotUiTheme.SelectedToolBrush
            : IsEnabled && IsPointerOver
                ? ScreenshotUiTheme.HoveredToolBrush
                : Brushes.Transparent;
    }
}
