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
    private readonly ScreenshotToolbarButton _textButton;
    private readonly ScreenshotToolbarButton _mosaicButton;
    private readonly ScreenshotToolbarButton _saveButton;
    private readonly ScreenshotToolbarButton _confirmButton;
    private readonly ScreenshotToolbarButton _undoButton;
    private readonly ScreenshotToolbarButton _redoButton;
    private readonly StackPanel _annotationOptions;
    private readonly StackPanel _colorOptions = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _lineWidthOptions = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _fontSizeOptions = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _mosaicBrushOptions = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Dictionary<ScreenshotAnnotationColor, ScreenshotToolbarButton> _colorButtons = [];
    private readonly Dictionary<int, ScreenshotToolbarButton> _lineWidthButtons = [];
    private readonly Dictionary<int, ScreenshotToolbarButton> _fontSizeButtons = [];
    private readonly Dictionary<int, ScreenshotToolbarButton> _mosaicBrushButtons = [];
    private ScreenshotAnnotationTool? _selectedObjectTool;

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

        _textButton = CreateButton(
            ScreenshotToolbarIconKind.Text,
            ScreenshotUiText.TextTool,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _textButton.Invoked += (_, _) => ToggleTool(ScreenshotAnnotationTool.Text);

        _annotationOptions = CreateAnnotationOptions();
        _mosaicButton = CreateButton(
            ScreenshotToolbarIconKind.Mosaic,
            ScreenshotUiText.MosaicTool,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _mosaicButton.Invoked += (_, _) => ToggleTool(ScreenshotAnnotationTool.Mosaic);
        _undoButton = CreateButton(
            ScreenshotToolbarIconKind.Undo,
            ScreenshotUiText.Undo,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _undoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        _redoButton = CreateButton(
            ScreenshotToolbarIconKind.Redo,
            ScreenshotUiText.Redo,
            ScreenshotUiTheme.PrimaryTextBrush,
            isEnabled: false);
        _redoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);

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
        content.Children.Add(_textButton);
        content.Children.Add(_mosaicButton);
        content.Children.Add(_annotationOptions);
        content.Children.Add(CreateSeparator());
        content.Children.Add(_undoButton);
        content.Children.Add(_redoButton);
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

    internal event EventHandler? UndoRequested;

    internal event EventHandler? RedoRequested;

    internal ScreenshotAnnotationTool ActiveTool { get; private set; }

    internal ScreenshotAnnotationStyle AnnotationStyle { get; private set; } =
        ScreenshotAnnotationStyle.Default;

    internal ScreenshotTextStyle TextStyle { get; private set; } =
        ScreenshotTextStyle.Default;

    internal ScreenshotMosaicStyle MosaicStyle { get; private set; } =
        ScreenshotMosaicStyle.Default;

    internal bool AnnotationOptionsVisible => _annotationOptions.IsVisible;

    internal bool ColorOptionsVisible => _colorOptions.IsVisible;

    internal bool LineWidthOptionsVisible => _lineWidthOptions.IsVisible;

    internal bool FontSizeOptionsVisible => _fontSizeOptions.IsVisible;

    internal bool MosaicBrushOptionsVisible => _mosaicBrushOptions.IsVisible;

    internal void SetSelectionActionsEnabled(bool isEnabled)
    {
        _rectangleButton.SetEnabled(isEnabled);
        _arrowButton.SetEnabled(isEnabled);
        _textButton.SetEnabled(isEnabled);
        _mosaicButton.SetEnabled(isEnabled);
        _saveButton.SetEnabled(isEnabled);
        _confirmButton.SetEnabled(isEnabled);
        if (!isEnabled && ActiveTool != ScreenshotAnnotationTool.Select)
        {
            SelectTool(ScreenshotAnnotationTool.Select);
        }
    }

    internal void SetHistoryActionsEnabled(bool canUndo, bool canRedo)
    {
        _undoButton.SetEnabled(canUndo);
        _redoButton.SetEnabled(canRedo);
    }

    internal void SelectTool(ScreenshotAnnotationTool tool)
    {
        if (ActiveTool == tool)
        {
            return;
        }

        ActiveTool = tool;
        if (tool != ScreenshotAnnotationTool.Select)
        {
            _selectedObjectTool = null;
        }

        _rectangleButton.IsSelected = tool == ScreenshotAnnotationTool.Rectangle;
        _arrowButton.IsSelected = tool == ScreenshotAnnotationTool.Arrow;
        _textButton.IsSelected = tool == ScreenshotAnnotationTool.Text;
        _mosaicButton.IsSelected = tool == ScreenshotAnnotationTool.Mosaic;
        UpdateOptionVisibility();
        UpdateStyleSelection();
        ToolChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetSelectedAnnotation(IScreenshotAnnotation? annotation)
    {
        _selectedObjectTool = annotation switch
        {
            ScreenshotRectangleAnnotation => ScreenshotAnnotationTool.Rectangle,
            ScreenshotArrowAnnotation => ScreenshotAnnotationTool.Arrow,
            ScreenshotTextAnnotation => ScreenshotAnnotationTool.Text,
            ScreenshotMosaicAnnotation => ScreenshotAnnotationTool.Mosaic,
            _ => null,
        };
        switch (annotation)
        {
            case ScreenshotRectangleAnnotation rectangle:
                AnnotationStyle = rectangle.Style;
                break;
            case ScreenshotArrowAnnotation arrow:
                AnnotationStyle = arrow.Style;
                break;
            case ScreenshotTextAnnotation text:
                TextStyle = text.Style;
                break;
            case ScreenshotMosaicAnnotation mosaic:
                MosaicStyle = mosaic.Style;
                break;
        }

        UpdateOptionVisibility();
        UpdateStyleSelection();
    }

    internal void SelectAnnotationStyle(ScreenshotAnnotationStyle style)
    {
        if (AnnotationStyle == style)
        {
            return;
        }

        AnnotationStyle = style;
        UpdateStyleSelection();
        AnnotationStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectTextStyle(ScreenshotTextStyle style)
    {
        if (TextStyle == style)
        {
            return;
        }

        TextStyle = style;
        UpdateStyleSelection();
        AnnotationStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SelectMosaicStyle(ScreenshotMosaicStyle style)
    {
        if (MosaicStyle == style)
        {
            return;
        }

        MosaicStyle = style;
        UpdateStyleSelection();
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

        AddColorButton(_colorOptions, ScreenshotAnnotationColor.Red, ScreenshotUiText.ColorRed);
        AddColorButton(_colorOptions, ScreenshotAnnotationColor.Yellow, ScreenshotUiText.ColorYellow);
        AddColorButton(_colorOptions, ScreenshotAnnotationColor.Green, ScreenshotUiText.ColorGreen);
        AddColorButton(_colorOptions, ScreenshotAnnotationColor.Blue, ScreenshotUiText.ColorBlue);
        AddColorButton(_colorOptions, ScreenshotAnnotationColor.Black, ScreenshotUiText.ColorBlack);
        AddColorButton(_colorOptions, ScreenshotAnnotationColor.White, ScreenshotUiText.ColorWhite);
        _colorOptions.Children.Add(CreateSeparator());
        options.Children.Add(_colorOptions);
        AddLineWidthButton(_lineWidthOptions, 2, ScreenshotUiText.LineWidth2);
        AddLineWidthButton(_lineWidthOptions, 4, ScreenshotUiText.LineWidth4);
        AddLineWidthButton(_lineWidthOptions, 8, ScreenshotUiText.LineWidth8);
        options.Children.Add(_lineWidthOptions);
        AddFontSizeButton(_fontSizeOptions, 16, ScreenshotUiText.FontSize16);
        AddFontSizeButton(_fontSizeOptions, 24, ScreenshotUiText.FontSize24);
        AddFontSizeButton(_fontSizeOptions, 32, ScreenshotUiText.FontSize32);
        _fontSizeOptions.IsVisible = false;
        options.Children.Add(_fontSizeOptions);
        AddMosaicBrushButton(_mosaicBrushOptions, 16, 8, ScreenshotUiText.MosaicBrush16);
        AddMosaicBrushButton(_mosaicBrushOptions, 32, 12, ScreenshotUiText.MosaicBrush32);
        AddMosaicBrushButton(_mosaicBrushOptions, 64, 16, ScreenshotUiText.MosaicBrush64);
        _mosaicBrushOptions.IsVisible = false;
        options.Children.Add(_mosaicBrushOptions);

        _colorButtons[ScreenshotAnnotationColor.Red].IsSelected = true;
        _lineWidthButtons[4].IsSelected = true;
        _fontSizeButtons[24].IsSelected = true;
        _mosaicBrushButtons[32].IsSelected = true;
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
        button.Invoked += (_, _) => SelectColor(color);
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

    private void AddFontSizeButton(Panel options, int fontSize, string tooltip)
    {
        var button = new ScreenshotToolbarButton(
            new ScreenshotFontSizeSwatch(fontSize),
            tooltip,
            isEnabled: true);
        button.Invoked += (_, _) => SelectTextStyle(
            new ScreenshotTextStyle(TextStyle.Color, fontSize));
        _fontSizeButtons.Add(fontSize, button);
        options.Children.Add(button);
    }

    private void AddMosaicBrushButton(
        Panel options,
        int brushSize,
        int pixelSize,
        string tooltip)
    {
        var button = new ScreenshotToolbarButton(
            new ScreenshotMosaicBrushSwatch(brushSize),
            tooltip,
            isEnabled: true);
        button.Invoked += (_, _) => SelectMosaicStyle(
            new ScreenshotMosaicStyle(brushSize, pixelSize));
        _mosaicBrushButtons.Add(brushSize, button);
        options.Children.Add(button);
    }

    private void SelectColor(ScreenshotAnnotationColor color)
    {
        if (EffectiveTool == ScreenshotAnnotationTool.Text)
        {
            SelectTextStyle(new ScreenshotTextStyle(color, TextStyle.FontSize));
        }
        else
        {
            SelectAnnotationStyle(new ScreenshotAnnotationStyle(color, AnnotationStyle.LineWidth));
        }
    }

    private void UpdateStyleSelection()
    {
        var color = EffectiveTool == ScreenshotAnnotationTool.Text
            ? TextStyle.Color
            : AnnotationStyle.Color;
        foreach (var colorButton in _colorButtons)
        {
            colorButton.Value.IsSelected = colorButton.Key == color;
        }

        foreach (var widthButton in _lineWidthButtons)
        {
            widthButton.Value.IsSelected = widthButton.Key == AnnotationStyle.LineWidth;
        }

        foreach (var fontSizeButton in _fontSizeButtons)
        {
            fontSizeButton.Value.IsSelected = fontSizeButton.Key == TextStyle.FontSize;
        }

        foreach (var mosaicBrushButton in _mosaicBrushButtons)
        {
            mosaicBrushButton.Value.IsSelected =
                mosaicBrushButton.Key == MosaicStyle.BrushSize;
        }
    }

    private ScreenshotAnnotationTool EffectiveTool =>
        ActiveTool == ScreenshotAnnotationTool.Select && _selectedObjectTool is { } selectedTool
            ? selectedTool
            : ActiveTool;

    private void UpdateOptionVisibility()
    {
        var tool = EffectiveTool;
        _annotationOptions.IsVisible = tool != ScreenshotAnnotationTool.Select;
        _colorOptions.IsVisible = tool is ScreenshotAnnotationTool.Rectangle or
            ScreenshotAnnotationTool.Arrow or ScreenshotAnnotationTool.Text;
        _lineWidthOptions.IsVisible = tool is ScreenshotAnnotationTool.Rectangle or
            ScreenshotAnnotationTool.Arrow;
        _fontSizeOptions.IsVisible = tool == ScreenshotAnnotationTool.Text;
        _mosaicBrushOptions.IsVisible = tool == ScreenshotAnnotationTool.Mosaic;
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

internal sealed class ScreenshotFontSizeSwatch : TextBlock
{
    internal ScreenshotFontSizeSwatch(int fontSize)
    {
        Text = fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Foreground = ScreenshotUiTheme.PrimaryTextBrush;
        FontSize = 10;
        FontWeight = FontWeight.SemiBold;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }
}

internal sealed class ScreenshotMosaicBrushSwatch : Control
{
    private readonly double _diameter;

    internal ScreenshotMosaicBrushSwatch(int brushSize)
    {
        _diameter = brushSize switch
        {
            16 => 6,
            32 => 10,
            _ => 14,
        };
        Width = ScreenshotUiTheme.IconSize;
        Height = ScreenshotUiTheme.IconSize;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var topLeft = new Point(
            (ScreenshotUiTheme.IconSize - _diameter) / 2,
            (ScreenshotUiTheme.IconSize - _diameter) / 2);
        var cell = Math.Max(2, _diameter / 3);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                context.DrawRectangle(
                    ScreenshotUiTheme.PrimaryTextBrush,
                    pen: null,
                    new Rect(
                        topLeft.X + (column * cell),
                        topLeft.Y + (row * cell),
                        Math.Max(1, cell - 0.5),
                        Math.Max(1, cell - 0.5)));
            }
        }
    }
}
