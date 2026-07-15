using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Snaploom.Core;

namespace Snaploom.App;

internal sealed class ScreenshotToolbar : Border
{
    private readonly ScreenshotToolbarButton _rectangleButton;
    private readonly ScreenshotToolbarButton _arrowButton;
    private readonly ScreenshotToolbarButton _saveButton;
    private readonly ScreenshotToolbarButton _confirmButton;
    private readonly StackPanel _annotationOptions;
    private readonly Dictionary<ScreenshotAnnotationColor, ScreenshotToolbarButton> _colorButtons = [];
    private readonly Dictionary<int, ScreenshotToolbarButton> _lineWidthButtons = [];

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

        _rectangleButton = CreateButton(
            ScreenshotToolbarIconKind.Rectangle,
            ScreenshotUiText.RectangleTool,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _rectangleButton.Invoked += (_, _) => ToggleTool(ScreenshotAnnotationTool.Rectangle);

        _arrowButton = CreateButton(
            ScreenshotToolbarIconKind.Arrow,
            ScreenshotUiText.ArrowTool,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _arrowButton.Invoked += (_, _) => ToggleTool(ScreenshotAnnotationTool.Arrow);

        var textButton = CreateUnavailableButton(
            ScreenshotToolbarIconKind.Text,
            ScreenshotUiText.TextUnavailable,
            ScreenshotUiTheme.DisabledIconBrush);

        _annotationOptions = CreateAnnotationOptions();
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
        content.Children.Add(_rectangleButton);
        content.Children.Add(_arrowButton);
        content.Children.Add(textButton);
        content.Children.Add(mosaicButton);
        content.Children.Add(_annotationOptions);
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

    internal event EventHandler? ToolChanged;

    internal event EventHandler? AnnotationStyleChanged;

    internal ScreenshotAnnotationTool ActiveTool { get; private set; }

    internal ScreenshotAnnotationStyle AnnotationStyle { get; private set; } =
        ScreenshotAnnotationStyle.Default;

    internal bool AnnotationOptionsVisible => _annotationOptions.IsVisible;

    internal void SetSelectionActionsEnabled(bool isEnabled)
    {
        _rectangleButton.SetEnabled(isEnabled);
        _arrowButton.SetEnabled(isEnabled);
        _saveButton.SetEnabled(isEnabled);
        _confirmButton.SetEnabled(isEnabled);
        if (!isEnabled && ActiveTool != ScreenshotAnnotationTool.Select)
        {
            SelectTool(ScreenshotAnnotationTool.Select);
        }
    }

    internal void SelectTool(ScreenshotAnnotationTool tool)
    {
        if (ActiveTool == tool)
        {
            return;
        }

        ActiveTool = tool;
        _rectangleButton.IsSelected = tool == ScreenshotAnnotationTool.Rectangle;
        _arrowButton.IsSelected = tool == ScreenshotAnnotationTool.Arrow;
        _annotationOptions.IsVisible = tool != ScreenshotAnnotationTool.Select;
        ToolChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectAnnotationStyle(ScreenshotAnnotationStyle style)
    {
        if (AnnotationStyle == style)
        {
            return;
        }

        AnnotationStyle = style;
        foreach (var colorButton in _colorButtons)
        {
            colorButton.Value.IsSelected = colorButton.Key == style.Color;
        }

        foreach (var widthButton in _lineWidthButtons)
        {
            widthButton.Value.IsSelected = widthButton.Key == style.LineWidth;
        }

        AnnotationStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ScreenshotToolbarButton CreateButton(
        ScreenshotToolbarIconKind icon,
        string tooltip,
        IBrush iconBrush,
        bool isEnabled = true)
        => new(icon, tooltip, iconBrush, isEnabled);

    private StackPanel CreateAnnotationOptions()
    {
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        options.Children.Add(CreateSeparator());

        AddColorButton(options, ScreenshotAnnotationColor.Red, ScreenshotUiText.ColorRed);
        AddColorButton(options, ScreenshotAnnotationColor.Yellow, ScreenshotUiText.ColorYellow);
        AddColorButton(options, ScreenshotAnnotationColor.Green, ScreenshotUiText.ColorGreen);
        AddColorButton(options, ScreenshotAnnotationColor.Blue, ScreenshotUiText.ColorBlue);
        AddColorButton(options, ScreenshotAnnotationColor.Black, ScreenshotUiText.ColorBlack);
        AddColorButton(options, ScreenshotAnnotationColor.White, ScreenshotUiText.ColorWhite);
        options.Children.Add(CreateSeparator());
        AddLineWidthButton(options, 2, ScreenshotUiText.LineWidth2);
        AddLineWidthButton(options, 4, ScreenshotUiText.LineWidth4);
        AddLineWidthButton(options, 8, ScreenshotUiText.LineWidth8);

        _colorButtons[ScreenshotAnnotationColor.Red].IsSelected = true;
        _lineWidthButtons[4].IsSelected = true;
        return options;
    }

    private void AddColorButton(
        Panel options,
        ScreenshotAnnotationColor color,
        string tooltip)
    {
        var rgb = ScreenshotAnnotationPalette.GetColor(color);
        var button = new ScreenshotToolbarButton(
            new ScreenshotColorSwatch(
                new SolidColorBrush(Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue))),
            tooltip,
            isEnabled: true);
        button.Invoked += (_, _) => SelectAnnotationStyle(
            new ScreenshotAnnotationStyle(color, AnnotationStyle.LineWidth));
        _colorButtons.Add(color, button);
        options.Children.Add(button);
    }

    private void AddLineWidthButton(Panel options, int lineWidth, string tooltip)
    {
        var button = new ScreenshotToolbarButton(
            new ScreenshotLineWidthSwatch(lineWidth),
            tooltip,
            isEnabled: true);
        button.Invoked += (_, _) => SelectAnnotationStyle(
            new ScreenshotAnnotationStyle(AnnotationStyle.Color, lineWidth));
        _lineWidthButtons.Add(lineWidth, button);
        options.Children.Add(button);
    }

    private void ToggleTool(ScreenshotAnnotationTool tool) =>
        SelectTool(ActiveTool == tool ? ScreenshotAnnotationTool.Select : tool);

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
        : this(new ScreenshotToolbarIcon(icon, iconBrush), tooltip, isEnabled)
    {
    }

    internal ScreenshotToolbarButton(
        Control content,
        string tooltip,
        bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(content);
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
            Child = content,
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
        UpdateVisualState();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
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
        var isHovered = IsEnabled && IsPointerOver;
        _selectedBackground.Width = isHovered && !_isSelected
            ? ScreenshotUiTheme.HoveredToolBackgroundSize
            : ScreenshotUiTheme.SelectedToolBackgroundSize;
        _selectedBackground.Height = _selectedBackground.Width;
        _selectedBackground.Background = _isSelected
            ? ScreenshotUiTheme.SelectedToolBrush
            : isHovered
                ? ScreenshotUiTheme.HoveredToolBrush
                : Brushes.Transparent;
    }
}

internal sealed class ScreenshotColorSwatch : Control
{
    private static readonly Pen BorderPen = new(ScreenshotUiTheme.FloatingBorderBrush, 1);
    private readonly IBrush _brush;

    internal ScreenshotColorSwatch(IBrush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        _brush = brush;
        Width = ScreenshotUiTheme.IconSize;
        Height = ScreenshotUiTheme.IconSize;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawEllipse(_brush, BorderPen, new Point(9, 9), 6, 6);
    }
}

internal sealed class ScreenshotLineWidthSwatch : Control
{
    private readonly double _lineWidth;

    internal ScreenshotLineWidthSwatch(double lineWidth)
    {
        _lineWidth = lineWidth;
        Width = ScreenshotUiTheme.IconSize;
        Height = ScreenshotUiTheme.IconSize;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var previewWidth = Math.Clamp(_lineWidth / 2, 1, 4);
        var pen = new Pen(
            ScreenshotUiTheme.PrimaryTextBrush,
            previewWidth,
            lineCap: PenLineCap.Round);
        context.DrawLine(pen, new Point(2, 9), new Point(16, 9));
    }
}
