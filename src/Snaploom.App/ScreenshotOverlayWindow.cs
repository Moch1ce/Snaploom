using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly TranslateTransform _sizeBadgeTransform = new();
    private readonly TranslateTransform _toolbarTransform = new();
    private readonly TranslateTransform _textEditorTransform = new();
    private Rect _availableUiBounds;
    private bool _floatingUiFrozen;
    private bool _changingTextEditor;
    private bool _synchronizingAnnotationStyle;
    private bool _resourcesDisposed;

    internal ScreenshotAnnotationTool ActiveAnnotationTool => _toolbar.ActiveTool;

    internal Point ToolbarOrigin => new(_toolbarTransform.X, _toolbarTransform.Y);

    internal bool TextEditorVisible => _textEditor.IsVisible;

    internal TextBox TextEditor => _textEditor;

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
            capturedScreen.WindowCandidates);
        _selectionCanvas.SelectionChanged += HandleSelectionChanged;
        _selectionCanvas.SelectionDoubleClicked += HandleConfirm;
        _selectionCanvas.AnnotationStarted += HandleAnnotationStarted;
        _selectionCanvas.TextEditingStarted += HandleTextEditingStarted;
        _selectionCanvas.AnnotationSelectionChanged += HandleAnnotationSelectionChanged;
        _selectionCanvas.AnnotationHistoryChanged += HandleAnnotationHistoryChanged;
        _selectionCanvas.SelectionReplaced += HandleSelectionReplaced;

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

        _toolbar = new ScreenshotToolbar(
            settings?.Current.AnnotationStyle,
            settings?.Current.TextStyle,
            settings?.Current.MosaicStyle)
        {
            RenderTransform = _toolbarTransform,
        };
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
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(2, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = ScreenshotUiTheme.AccentBrush,
            Background = new SolidColorBrush(Color.FromArgb(88, 0, 0, 0)),
            IsVisible = false,
            RenderTransform = _textEditorTransform,
        };
        _textEditor.TextChanged += HandleTextChanged;
        _textEditor.KeyDown += HandleTextEditorKeyDown;

        var root = new Grid();
        root.Children.Add(_selectionCanvas);
        root.Children.Add(_sizeBadge);
        root.Children.Add(_toolbar);
        root.Children.Add(_textEditor);
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
        if (_floatingUiFrozen)
        {
            return;
        }

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
        CommitTextEditing();
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
            var path = _saveDialogService.ShowSaveDialog(
                suggestedName,
                _settings?.Current.LastSaveDirectory ?? s_rememberedSaveDirectory);
            if (path is null)
            {
                _selectionCanvas.Session.CancelSave();
                RestoreSelectedUi(selection);
                return;
            }

            path = Path.ChangeExtension(path, ".png");
            s_rememberedSaveDirectory = Path.GetDirectoryName(path);
            _settings?.Update(current => current with
            {
                LastSaveDirectory = s_rememberedSaveDirectory,
            });

            await File.WriteAllBytesAsync(path, EncodeSelection(selection));
            Close();
        }
        catch (Exception exception)
        {
            _log?.Error(AppLogEvent.SaveFailed, exception);
            if (_selectionCanvas.Session.State == ScreenshotSessionState.Saving)
            {
                _selectionCanvas.Session.CancelSave();
            }

            _toolbar.SetSelectionActionsEnabled(isEnabled: true);
            _sizeText.Text = ScreenshotUiText.SaveFailed;
            ToolTip.SetTip(_sizeBadge, ScreenshotUiText.SaveFailed);
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
            _toolbar.SetSelectedAnnotation(_selectionCanvas.SelectedAnnotation);
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
            _textEditor.Foreground = new SolidColorBrush(
                Color.FromRgb(color.Red, color.Green, color.Blue));
            _textEditor.Width = Math.Max(1, edit.MaxWidth);
            _textEditor.MinHeight = edit.Style.FontSize * 1.35;
            _textEditor.MaxHeight = Math.Max(
                _textEditor.MinHeight,
                selection.Height - edit.Origin.Y);
            _textEditorTransform.X = selection.X + edit.Origin.X;
            _textEditorTransform.Y = selection.Y + edit.Origin.Y;
            _textEditor.IsVisible = true;
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

    private void HandleTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_changingTextEditor && _selectionCanvas.TextEdit is not null)
        {
            _selectionCanvas.UpdateTextDraft(
                _textEditor.Text ?? string.Empty,
                isComposing: false);
        }
    }

    private void HandleTextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelTextEditing();
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

    private void CommitTextEditing()
    {
        if (!_textEditor.IsVisible || _changingTextEditor)
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

            _textEditor.IsVisible = false;
            _selectionCanvas.Focus();
        }
        finally
        {
            _changingTextEditor = false;
        }
    }

    private void CancelTextEditing()
    {
        if (!_textEditor.IsVisible)
        {
            return;
        }

        _changingTextEditor = true;
        try
        {
            _selectionCanvas.CancelTextEdit();
            _textEditor.IsVisible = false;
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
        if (_textEditor.IsVisible)
        {
            var editorCommandModifier = OperatingSystem.IsMacOS()
                ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                : e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelTextEditing();
            }
            else if (editorCommandModifier && e.Key == Key.S)
            {
                e.Handled = true;
                CommitTextEditing();
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

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            var result = _selectionCanvas.CancelCurrentLayer();
            if (_toolbar.ActiveTool != _selectionCanvas.ActiveAnnotationTool)
            {
                _toolbar.SelectTool(_selectionCanvas.ActiveAnnotationTool);
            }

            if (result == ScreenshotCancelResult.ExitRequested)
            {
                Close();
            }

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
            });
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
        _textEditor.KeyDown -= HandleTextEditorKeyDown;
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
