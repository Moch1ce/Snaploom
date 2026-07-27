using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using Snaploom.Rendering;

namespace Snaploom.App;

public sealed class ScreenshotOverlayWindow : Window, IDisposable
{
    private static string? s_rememberedSaveDirectory;
    private readonly CapturedScreen _capturedScreen;
    private readonly IPngSaveDialogService _saveDialogService;
    private readonly IScreenshotClipboardService _clipboardService;
    private readonly IScreenshotOverlayConfigurator _overlayConfigurator;
    private readonly AppSettingsService? _settings;
    private readonly PrivacyLog? _log;
    private readonly ScreenshotSelectionCanvas _selectionCanvas;
    private readonly TextBlock _sizeText;
    private readonly Border _sizeBadge;
    private readonly ScreenshotToolbar _toolbar;
    private readonly TextBox _textEditor;
    private readonly Grid _textEditorHost;
    private readonly Avalonia.Controls.Shapes.Ellipse[] _textEditorControlPoints;
    private TextPresenter? _textEditorPresenter;
    private readonly TranslateTransform _sizeBadgeTransform = new();
    private readonly TranslateTransform _toolbarTransform = new();
    private readonly TranslateTransform _annotationOptionsFlyoutTransform = new();
    private readonly TranslateTransform _textEditorTransform = new();
    private Rect _availableUiBounds;
    private bool _floatingUiFrozen;
    private bool _changingTextEditor;
    private bool _synchronizingAnnotationStyle;
    private bool _resourcesDisposed;

    internal event EventHandler? Interactive;

    internal bool OutputCompleted { get; private set; }

    internal ScreenshotAnnotationTool ActiveAnnotationTool => _toolbar.ActiveTool;

    internal Point ToolbarOrigin => new(_toolbarTransform.X, _toolbarTransform.Y);

    internal Point AnnotationOptionsFlyoutOrigin =>
        new(_annotationOptionsFlyoutTransform.X, _annotationOptionsFlyoutTransform.Y);

    internal bool TextEditorVisible => _textEditorHost.IsVisible;

    internal TextBox TextEditor => _textEditor;

    internal double TextEditorVisualWidth => _textEditorHost.Width;

    internal double TextEditorVisualHeight => _textEditorHost.Height;

    internal int TextEditorControlPointCount => _textEditorControlPoints.Length;

    internal bool AnnotationOptionsFlyoutOpen => _toolbar.AnnotationOptionsFlyoutOpen;

    internal bool AnnotationOptionsFlyoutSuspended =>
        _toolbar.AnnotationOptionsFlyoutSuspended;

    internal IReadOnlyList<IScreenshotAnnotation> Annotations => _selectionCanvas.Annotations;

    internal ScreenshotTextEdit? TextEdit => _selectionCanvas.TextEdit;

    internal IScreenshotAnnotation? SelectedAnnotation => _selectionCanvas.SelectedAnnotation;

    internal bool CanUndo => _selectionCanvas.CanUndo;

    internal static string? RememberedSaveDirectory
    {
        get => s_rememberedSaveDirectory;
        set => s_rememberedSaveDirectory = value;
    }

    public ScreenshotOverlayWindow(
        CapturedScreen capturedScreen,
        IPngSaveDialogService saveDialogService,
        IScreenshotClipboardService clipboardService,
        IScreenshotOverlayConfigurator overlayConfigurator,
        AppSettingsService? settings = null,
        PrivacyLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(capturedScreen);
        ArgumentNullException.ThrowIfNull(saveDialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(overlayConfigurator);
        _capturedScreen = capturedScreen;
        _saveDialogService = saveDialogService;
        _clipboardService = clipboardService;
        _overlayConfigurator = overlayConfigurator;
        _settings = settings;
        _log = log;
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

        _selectionCanvas = new ScreenshotSelectionCanvas(
            capturedScreen.Frame,
            capturedScreen.WindowCandidates,
            capturedScreen.CursorPosition);
        _selectionCanvas.SelectionChanged += HandleSelectionChanged;
        _selectionCanvas.SelectionDoubleClicked += HandleConfirm;
        _selectionCanvas.AnnotationStarted += HandleAnnotationStarted;
        _selectionCanvas.TextEditingStarted += HandleTextEditingStarted;
        _selectionCanvas.AnnotationSelectionChanged += HandleAnnotationSelectionChanged;
        _selectionCanvas.AnnotationHistoryChanged += HandleAnnotationHistoryChanged;
        _selectionCanvas.SelectionReplaced += HandleSelectionReplaced;

        _sizeText = new TextBlock
        {
            Foreground = ScreenshotUiTheme.SizeBadgeTextBrush,
            FontSize = ScreenshotUiTheme.SizeBadgeFontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _sizeBadge = new Border
        {
            Background = ScreenshotUiTheme.SizeBadgeBrush,
            CornerRadius = new CornerRadius(ScreenshotUiTheme.SizeBadgeCornerRadius),
            Padding = new Thickness(
                ScreenshotUiTheme.SizeBadgeHorizontalPadding,
                ScreenshotUiTheme.SizeBadgeVerticalPadding),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            RenderTransform = _sizeBadgeTransform,
            Child = _sizeText,
        };

        _toolbar = new ScreenshotToolbar(
            settings?.Current.AnnotationStyle,
            settings?.Current.TextStyle,
            settings?.Current.MosaicStyle)
        {
            RenderTransform = _toolbarTransform,
        };
        _toolbar.AnnotationOptionsFlyout.RenderTransform =
            _annotationOptionsFlyoutTransform;
        _toolbar.SaveRequested += HandleSave;
        _toolbar.ConfirmRequested += HandleConfirm;
        _toolbar.CancelRequested += HandleCancel;
        _toolbar.ToolChanged += HandleToolChanged;
        _toolbar.AnnotationStyleChanged += HandleAnnotationStyleChanged;
        _toolbar.UndoRequested += HandleUndo;
        _toolbar.RedoRequested += HandleRedo;
        _selectionCanvas.SetAnnotationStyle(_toolbar.AnnotationStyle);
        _selectionCanvas.SetTextStyle(_toolbar.TextStyle);
        _selectionCanvas.SetMosaicStyle(_toolbar.MosaicStyle);

        _textEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Default,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(ScreenshotUiTheme.TextEditorPadding),
            BorderThickness = new Thickness(0),
            Background = ScreenshotUiTheme.TransparentBrush,
            CaretBrush = ScreenshotUiTheme.AccentBrush,
            SelectionBrush = ScreenshotUiTheme.TextEditorSelectionBrush,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(
            _textEditor,
            ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(
            _textEditor,
            ScrollBarVisibility.Disabled);
        _textEditorControlPoints =
        [
            CreateTextEditorControlPoint(HorizontalAlignment.Left, VerticalAlignment.Top),
            CreateTextEditorControlPoint(HorizontalAlignment.Right, VerticalAlignment.Top),
            CreateTextEditorControlPoint(HorizontalAlignment.Left, VerticalAlignment.Bottom),
            CreateTextEditorControlPoint(HorizontalAlignment.Right, VerticalAlignment.Bottom),
        ];
        _textEditorHost = new Grid
        {
            Width = ScreenshotUiTheme.TextEditorMinimumWidth,
            Height = ScreenshotTextStyle.Default.FontSize *
                ScreenshotTextMetrics.LineHeightMultiplier +
                ScreenshotUiTheme.TextEditorMeasuredHeightPadding,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = false,
            IsVisible = false,
            RenderTransform = _textEditorTransform,
        };
        _textEditorHost.Styles.Add(
            ScreenshotUiTheme.CreateTextEditorFocusChromeStyle());
        _textEditorHost.Children.Add(new Border
        {
            Background = ScreenshotUiTheme.TransparentBrush,
            BorderBrush = ScreenshotUiTheme.TextEditorBorderBrush,
            BorderThickness = new Thickness(ScreenshotUiTheme.FloatingBorderThickness),
            CornerRadius = new CornerRadius(ScreenshotUiTheme.TextEditorCornerRadius),
            Child = _textEditor,
        });
        foreach (var controlPoint in _textEditorControlPoints)
        {
            _textEditorHost.Children.Add(controlPoint);
        }

        _textEditor.TextChanged += HandleTextChanged;
        _textEditor.TemplateApplied += HandleTextEditorTemplateApplied;
        _textEditor.AddHandler(
            InputElement.KeyDownEvent,
            HandleTextEditorKeyDown,
            RoutingStrategies.Tunnel);
        _textEditor.AddHandler(
            InputElement.PointerWheelChangedEvent,
            HandleTextEditorPointerWheelChanged,
            RoutingStrategies.Tunnel);

        var root = new Grid();
        root.Children.Add(_selectionCanvas);
        root.Children.Add(_sizeBadge);
        root.Children.Add(_toolbar);
        root.Children.Add(_textEditorHost);
        root.Children.Add(_toolbar.AnnotationOptionsFlyout);
        Content = root;

        Opened += HandleOpened;
        KeyDown += HandleKeyDown;
        AddHandler(
            InputElement.PointerPressedEvent,
            HandlePointerPressedWhileEditingText,
            RoutingStrategies.Tunnel);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeResources();
        base.OnClosed(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            Close();
        }
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
        var cursor = _capturedScreen.GlobalCursorPosition;
        var screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
        }

        Activate();
        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is not null)
        {
            _overlayConfigurator.ConfigureScreenshotOverlay(platformHandle.Handle);
        }

        screen = Screens.ScreenFromWindow(this) ?? screen;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
            UpdateAvailableUiBounds(screen);
        }

        _selectionCanvas.Focus();
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }

        Interactive?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAvailableUiBounds(Screen screen)
    {
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
        if (!_floatingUiFrozen)
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

        PositionAnnotationOptionsFlyout();
    }

    private void PositionAnnotationOptionsFlyout()
    {
        _annotationOptionsFlyoutTransform.X =
            _toolbarTransform.X + _toolbar.AnnotationOptionsHorizontalOffset;
        _annotationOptionsFlyoutTransform.Y =
            _toolbarTransform.Y + ScreenshotUiTheme.ToolbarHeight +
            ScreenshotUiTheme.AnnotationOptionsFlyoutVerticalOffset;
    }

    private async void HandleSave(object? sender, EventArgs e)
    {
        if (_selectionCanvas.Session.Selection is not { } selection ||
            _selectionCanvas.Session.State != ScreenshotSessionState.Selected)
        {
            return;
        }

        var textEditingWasVisible = _textEditorHost.IsVisible;
        _selectionCanvas.Session.BeginSave();
        _toolbar.SuspendAnnotationOptionsFlyout();
        _sizeText.Text = ScreenshotUiText.ChoosingSaveLocation;
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }

        try
        {
            var suggestedName = $"Snaploom_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            Hide();
            var path = _saveDialogService.ShowSaveDialog(
                suggestedName,
                _settings?.Current.LastSaveDirectory ?? s_rememberedSaveDirectory);
            if (path is null)
            {
                _selectionCanvas.Session.CancelSave();
                RestoreOverlayAfterSaveDialog(textEditingWasVisible);
                _toolbar.ResumeAnnotationOptionsFlyout();
                RestoreSelectedUi(selection);
                return;
            }

            CommitTextEditing(force: textEditingWasVisible);
            path = Path.ChangeExtension(path, ".png");
            s_rememberedSaveDirectory = Path.GetDirectoryName(path);
            _settings?.Update(current => current with
            {
                LastSaveDirectory = s_rememberedSaveDirectory,
            });

            await File.WriteAllBytesAsync(path, EncodeSelection(selection));
            OutputCompleted = true;
            Close();
        }
        catch (Exception exception)
        {
            _log?.Error(AppLogEvent.SaveFailed, exception);
            if (_selectionCanvas.Session.State == ScreenshotSessionState.Saving)
            {
                _selectionCanvas.Session.CancelSave();
            }

            RestoreOverlayAfterSaveDialog(
                textEditingWasVisible && _selectionCanvas.TextEdit is not null);
            _toolbar.ResumeAnnotationOptionsFlyout();
            _toolbar.SetSelectionActionsEnabled(isEnabled: true);
            _sizeText.Text = ScreenshotUiText.SaveFailed;
            ToolTip.SetTip(_sizeBadge, ScreenshotUiText.SaveFailed);
        }
    }

    private void RestoreOverlayAfterSaveDialog(bool restoreTextEditor)
    {
        if (!IsVisible)
        {
            Show();
        }

        _textEditorHost.IsVisible = restoreTextEditor;
        Activate();
        if (restoreTextEditor)
        {
            _textEditor.Focus();
        }
        else
        {
            _selectionCanvas.Focus();
        }
    }

    private void HandleConfirm(object? sender, EventArgs e)
    {
        CommitTextEditing();
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
                OutputCompleted = true;
                Close();
            }
            else
            {
                RestoreSelectedUi(selection);
            }
        }
        catch (Exception exception)
        {
            _log?.Error(AppLogEvent.ClipboardFailed, exception);
            _toolbar.SetSelectionActionsEnabled(isEnabled: true);
            _sizeText.Text = ScreenshotUiText.CopyImageFailed;
            ToolTip.SetTip(_sizeBadge, ScreenshotUiText.CopyImageFailed);
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

    private void HandleToolChanged(object? sender, EventArgs e)
    {
        CommitTextEditing();
        _selectionCanvas.SelectAnnotationTool(_toolbar.ActiveTool);
        if (_selectionCanvas.LogicalSelection is { } logicalSelection)
        {
            PositionFloatingUi(logicalSelection);
        }
    }

    private void HandleAnnotationStyleChanged(object? sender, EventArgs e)
    {
        if (_synchronizingAnnotationStyle)
        {
            return;
        }

        CommitTextEditing();
        _selectionCanvas.SetAnnotationStyle(_toolbar.AnnotationStyle);
        _selectionCanvas.SetTextStyle(_toolbar.TextStyle);
        _selectionCanvas.SetMosaicStyle(_toolbar.MosaicStyle);
        _settings?.Update(current => current with
        {
            AnnotationColor = _toolbar.AnnotationStyle.Color,
            AnnotationLineWidth = _toolbar.AnnotationStyle.LineWidth,
            TextColor = _toolbar.TextStyle.Color,
            TextFontSize = _toolbar.TextStyle.FontSize,
            MosaicBrushSize = _toolbar.MosaicStyle.BrushSize,
        });
    }

    private void HandleAnnotationSelectionChanged(object? sender, EventArgs e)
    {
        _synchronizingAnnotationStyle = true;
        try
        {
            if (_toolbar.ActiveTool != _selectionCanvas.ActiveAnnotationTool)
            {
                _toolbar.SynchronizeTool(_selectionCanvas.ActiveAnnotationTool);
            }

            _toolbar.SetSelectedAnnotation(_selectionCanvas.SelectedAnnotation);
            PositionAnnotationOptionsFlyout();
        }
        finally
        {
            _synchronizingAnnotationStyle = false;
        }
    }

    private void HandleAnnotationHistoryChanged(object? sender, EventArgs e) =>
        _toolbar.SetHistoryActionsEnabled(_selectionCanvas.CanUndo, _selectionCanvas.CanRedo);

    private void HandleUndo(object? sender, EventArgs e)
    {
        CommitTextEditing();
        _selectionCanvas.UndoAnnotation();
    }

    private void HandleRedo(object? sender, EventArgs e)
    {
        CommitTextEditing();
        _selectionCanvas.RedoAnnotation();
    }

    private void HandleAnnotationStarted(object? sender, EventArgs e) =>
        _floatingUiFrozen = true;

    private void HandleTextEditingStarted(object? sender, EventArgs e)
    {
        if (_selectionCanvas.TextEdit is not { } edit ||
            _selectionCanvas.LogicalSelection is not { } selection)
        {
            return;
        }

        var color = ScreenshotAnnotationPalette.GetColor(edit.Style.Color);
        _changingTextEditor = true;
        try
        {
            _textEditor.Text = edit.Text;
            _textEditor.FontSize = edit.Style.FontSize;
            _textEditor.LineHeight = edit.Style.FontSize *
                ScreenshotTextMetrics.LineHeightMultiplier;
            _textEditor.Foreground = new SolidColorBrush(
                Color.FromRgb(color.Red, color.Green, color.Blue));
            var editorBounds = ScreenshotTextEditorLayout.Measure(edit, selection);
            _textEditorTransform.X = editorBounds.X;
            _textEditorTransform.Y = editorBounds.Y;
            _textEditorHost.Width = ScreenshotUiTheme.TextEditorMinimumWidth;
            _textEditorHost.Height = (edit.Style.FontSize *
                ScreenshotTextMetrics.LineHeightMultiplier) +
                ScreenshotUiTheme.TextEditorMeasuredHeightPadding;
            UpdateTextEditorSize(edit, selection);
            _textEditorHost.IsVisible = true;
            _textEditor.Focus();
            if (edit.AnnotationIndex is not null)
            {
                _textEditor.SelectAll();
            }
            else
            {
                _textEditor.CaretIndex = _textEditor.Text?.Length ?? 0;
            }
        }
        finally
        {
            _changingTextEditor = false;
        }
    }

    private static Avalonia.Controls.Shapes.Ellipse CreateTextEditorControlPoint(
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment)
    {
        var halfSize = ScreenshotUiTheme.TextEditorControlPointSize / 2;
        return new Avalonia.Controls.Shapes.Ellipse
        {
            Width = ScreenshotUiTheme.TextEditorControlPointSize,
            Height = ScreenshotUiTheme.TextEditorControlPointSize,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = new Thickness(
                horizontalAlignment == HorizontalAlignment.Left ? -halfSize : 0,
                verticalAlignment == VerticalAlignment.Top ? -halfSize : 0,
                horizontalAlignment == HorizontalAlignment.Right ? -halfSize : 0,
                verticalAlignment == VerticalAlignment.Bottom ? -halfSize : 0),
            Fill = ScreenshotUiTheme.TextEditorControlPointBrush,
            Stroke = ScreenshotUiTheme.TextEditorBorderBrush,
            StrokeThickness = ScreenshotUiTheme.FloatingBorderThickness,
            IsHitTestVisible = false,
        };
    }

    private void UpdateTextEditorSize(
        ScreenshotTextEdit edit,
        Rect selection,
        string? measurementText = null,
        bool allowHeightShrink = false)
    {
        var editorBounds = ScreenshotTextEditorLayout.Measure(
            edit,
            selection,
            measurementText);
        _textEditorHost.Width = Math.Max(_textEditorHost.Width, editorBounds.Width);
        _textEditorHost.Height = allowHeightShrink
            ? editorBounds.Height
            : Math.Max(_textEditorHost.Height, editorBounds.Height);
    }

    private void HandleTextEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (_textEditorPresenter is not null)
        {
            _textEditorPresenter.PropertyChanged -= HandleTextEditorPresenterPropertyChanged;
        }

        _textEditorPresenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        if (_textEditorPresenter is not null)
        {
            _textEditorPresenter.PropertyChanged += HandleTextEditorPresenterPropertyChanged;
        }
    }

    private void HandleTextEditorPresenterPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextPresenter.PreeditTextProperty ||
            _selectionCanvas.TextEdit is not { } edit ||
            _selectionCanvas.LogicalSelection is not { } selection)
        {
            return;
        }

        UpdateTextEditorSize(
            edit,
            selection,
            BuildTextEditorMeasurementText(_textEditorPresenter?.PreeditText));
    }

    private string BuildTextEditorMeasurementText(string? preeditText)
    {
        var text = _textEditor.Text ?? string.Empty;
        if (string.IsNullOrEmpty(preeditText))
        {
            return NormalizeTextEditorLineEndings(text);
        }

        var selectionStart = Math.Clamp(
            Math.Min(_textEditor.SelectionStart, _textEditor.SelectionEnd),
            0,
            text.Length);
        var selectionEnd = Math.Clamp(
            Math.Max(_textEditor.SelectionStart, _textEditor.SelectionEnd),
            selectionStart,
            text.Length);
        return NormalizeTextEditorLineEndings(
            text[..selectionStart] + preeditText + text[selectionEnd..]);
    }

    private void HandleTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_changingTextEditor && _selectionCanvas.TextEdit is not null)
        {
            var text = NormalizeTextEditorLineEndings(_textEditor.Text);
            var allowHeightShrink =
                text.Length < _selectionCanvas.TextEdit.Text.Length;
            _selectionCanvas.UpdateTextDraft(
                text,
                isComposing: false);
            if (_selectionCanvas.TextEdit is { } edit &&
                _selectionCanvas.LogicalSelection is { } selection)
            {
                UpdateTextEditorSize(
                    edit,
                    selection,
                    allowHeightShrink: allowHeightShrink);
            }
        }
    }

    private static string NormalizeTextEditorLineEndings(string? text) =>
        (text ?? string.Empty).ReplaceLineEndings("\n");

    private void HandleTextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        var commitModifier = OperatingSystem.IsMacOS()
            ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Enter && commitModifier)
        {
            e.Handled = true;
            CommitTextEditing();
        }
    }

    private static void HandleTextEditorPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e) =>
        e.Handled = true;

    private void HandlePointerPressedWhileEditingText(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (!_textEditorHost.IsVisible ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            !ReferenceEquals(e.Source, _selectionCanvas))
        {
            return;
        }

        if (_selectionCanvas.HandleMaskPointerPressed(
                e.GetPosition(_selectionCanvas),
                e.ClickCount))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        CommitTextEditing();
    }

    private void CommitTextEditing(bool force = false)
    {
        if ((!_textEditorHost.IsVisible && !force) || _changingTextEditor)
        {
            return;
        }

        _changingTextEditor = true;
        try
        {
            if (_selectionCanvas.TextEdit is not null)
            {
                _selectionCanvas.UpdateTextDraft(
                    _textEditor.Text ?? string.Empty,
                    isComposing: false);
                _selectionCanvas.CommitTextEdit();
            }

            _textEditorHost.IsVisible = false;
            _selectionCanvas.Focus();
        }
        finally
        {
            _changingTextEditor = false;
        }
    }

    private void CancelTextEditing()
    {
        if (!_textEditorHost.IsVisible)
        {
            return;
        }

        _changingTextEditor = true;
        try
        {
            _selectionCanvas.CancelTextEdit();
            _textEditorHost.IsVisible = false;
            _selectionCanvas.Focus();
        }
        finally
        {
            _changingTextEditor = false;
        }
    }

    private void HandleSelectionReplaced(object? sender, EventArgs e)
    {
        CancelTextEditing();
        _floatingUiFrozen = false;
        _toolbar.SelectTool(ScreenshotAnnotationTool.Select);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (_textEditorHost.IsVisible)
        {
            var editorCommandModifier = OperatingSystem.IsMacOS()
                ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                : e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (editorCommandModifier && e.Key == Key.S)
            {
                e.Handled = true;
                HandleSave(this, EventArgs.Empty);
            }
            else if (editorCommandModifier && e.Key == Key.C)
            {
                e.Handled = true;
                CommitTextEditing();
                if (_selectionCanvas.Session.Selection is { } textCopySelection)
                {
                    CopySelection(textCopySelection, closeAfterCopy: false);
                }
            }
            else
            {
                if (e.Key == Key.Enter && editorCommandModifier)
                {
                    e.Handled = true;
                    CommitTextEditing();
                }
            }

            return;
        }

        var commandModifierPressed = OperatingSystem.IsMacOS()
            ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (commandModifierPressed && e.Key == Key.Z)
        {
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _selectionCanvas.RedoAnnotation();
            }
            else
            {
                _selectionCanvas.UndoAnnotation();
            }

            return;
        }

        if (commandModifierPressed && e.Key == Key.Y)
        {
            e.Handled = true;
            _selectionCanvas.RedoAnnotation();
            return;
        }

        if (commandModifierPressed && e.Key == Key.S)
        {
            e.Handled = true;
            HandleSave(this, EventArgs.Empty);
            return;
        }

        if (e.Key is Key.Delete or Key.Back &&
            _selectionCanvas.DeleteSelectedAnnotation())
        {
            e.Handled = true;
            return;
        }

        var annotationShortcutAllowed =
            !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (annotationShortcutAllowed &&
            _selectionCanvas.Session.State == ScreenshotSessionState.Selected &&
            e.Key is Key.R or Key.A or Key.T or Key.M or Key.V)
        {
            e.Handled = true;
            _toolbar.SelectTool(e.Key switch
            {
                Key.R => ScreenshotAnnotationTool.Rectangle,
                Key.A => ScreenshotAnnotationTool.Arrow,
                Key.T => ScreenshotAnnotationTool.Text,
                Key.M => ScreenshotAnnotationTool.Mosaic,
                _ => ScreenshotAnnotationTool.Select,
            }, showAnnotationOptions: false);
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
            CopySelection(copySelection, closeAfterCopy: false);
        }
    }

    private byte[] EncodeSelection(PhysicalRect selection) =>
        SelectionPngEncoder.Encode(
            _capturedScreen.Frame,
            selection,
            _selectionCanvas.Annotations);

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
        _toolbar.ToolChanged -= HandleToolChanged;
        _toolbar.AnnotationStyleChanged -= HandleAnnotationStyleChanged;
        _toolbar.UndoRequested -= HandleUndo;
        _toolbar.RedoRequested -= HandleRedo;
        _textEditor.TextChanged -= HandleTextChanged;
        _textEditor.TemplateApplied -= HandleTextEditorTemplateApplied;
        if (_textEditorPresenter is not null)
        {
            _textEditorPresenter.PropertyChanged -= HandleTextEditorPresenterPropertyChanged;
        }
        _textEditor.RemoveHandler(
            InputElement.KeyDownEvent,
            HandleTextEditorKeyDown);
        _textEditor.RemoveHandler(
            InputElement.PointerWheelChangedEvent,
            HandleTextEditorPointerWheelChanged);
        RemoveHandler(
            InputElement.PointerPressedEvent,
            HandlePointerPressedWhileEditingText);
        _selectionCanvas.SelectionChanged -= HandleSelectionChanged;
        _selectionCanvas.SelectionDoubleClicked -= HandleConfirm;
        _selectionCanvas.AnnotationStarted -= HandleAnnotationStarted;
        _selectionCanvas.TextEditingStarted -= HandleTextEditingStarted;
        _selectionCanvas.AnnotationSelectionChanged -= HandleAnnotationSelectionChanged;
        _selectionCanvas.AnnotationHistoryChanged -= HandleAnnotationHistoryChanged;
        _selectionCanvas.SelectionReplaced -= HandleSelectionReplaced;
        _selectionCanvas.Dispose();
        _capturedScreen.Dispose();
    }
}
