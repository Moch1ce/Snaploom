using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

public sealed class ShortcutSettingsWindow : Window
{
    private readonly ScreenshotHotKeyManager _hotKeyManager;
    private readonly AppSettingsService _settings;
    private readonly TrayMenuViewModel _trayViewModel;
    private readonly Action _appearanceChanged;
    private readonly PrivacyLog _log;
    private readonly IFolderLauncher? _folderLauncher;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IExternalUriLauncher? _uriLauncher;
    private readonly TextBlock _shortcutHeading = new();
    private readonly TextBlock _shortcutText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _languageLabel = new();
    private readonly TextBlock _themeLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _closeButton = new();
    private readonly CheckBox _autoStartCheckBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _themeComboBox = new();
    private readonly Button _openLogsButton = new();
    private readonly Button _clearLogsButton = new();
    private readonly TextBlock _updateHeading = new();
    private readonly Button _checkUpdatesButton = new();
    private readonly TextBlock _updateStatusText = new();
    private readonly TextBlock _releaseNotesHeading = new();
    private readonly TextBlock _releaseNotesText = new();
    private readonly ScrollViewer _releaseNotesScroll = new() { Height = 100 };
    private readonly Button _openReleaseButton = new();
    private ScreenshotHotKey _candidate;
    private ShortcutStatus _status;
    private bool _updatingControls;
    private bool _checkingUpdates;
    private UpdateCheckResult? _lastUpdateResult;

    public ShortcutSettingsWindow(
        ScreenshotHotKeyManager hotKeyManager,
        AppSettingsService settings,
        TrayMenuViewModel trayViewModel,
        Action appearanceChanged,
        PrivacyLog log,
        IFolderLauncher? folderLauncher,
        IUpdateCheckService updateCheckService,
        IExternalUriLauncher? uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(hotKeyManager);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trayViewModel);
        ArgumentNullException.ThrowIfNull(appearanceChanged);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(updateCheckService);
        _hotKeyManager = hotKeyManager;
        _settings = settings;
        _trayViewModel = trayViewModel;
        _appearanceChanged = appearanceChanged;
        _log = log;
        _folderLauncher = folderLauncher;
        _updateCheckService = updateCheckService;
        _uriLauncher = uriLauncher;
        _candidate = hotKeyManager.CurrentHotKey;
        _status = hotKeyManager.IsRegistered
            ? ShortcutStatus.Instruction
            : ShortcutStatus.StartupConflict;

        Width = 540;
        Height = 700;
        CanResize = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _shortcutHeading.FontSize = 16;
        _shortcutHeading.FontWeight = FontWeight.SemiBold;
        _shortcutText.FontSize = 18;
        _shortcutText.HorizontalAlignment = HorizontalAlignment.Center;
        _statusText.TextWrapping = TextWrapping.Wrap;
        _saveButton.MinWidth = 80;
        _saveButton.Click += HandleSave;
        _closeButton.MinWidth = 80;
        _closeButton.Click += (_, _) => Close();
        _autoStartCheckBox.Click += HandleAutoStartClicked;
        _languageComboBox.MinWidth = 190;
        _languageComboBox.SelectionChanged += HandleLanguageChanged;
        _themeComboBox.MinWidth = 190;
        _themeComboBox.SelectionChanged += HandleThemeChanged;
        _openLogsButton.Click += HandleOpenLogs;
        _clearLogsButton.Click += HandleClearLogs;
        _updateHeading.FontSize = 16;
        _updateHeading.FontWeight = FontWeight.SemiBold;
        _checkUpdatesButton.Click += HandleCheckUpdates;
        _updateStatusText.TextWrapping = TextWrapping.Wrap;
        _releaseNotesHeading.FontWeight = FontWeight.SemiBold;
        _releaseNotesHeading.IsVisible = false;
        _releaseNotesText.TextWrapping = TextWrapping.Wrap;
        _releaseNotesScroll.Content = _releaseNotesText;
        _releaseNotesScroll.IsVisible = false;
        _openReleaseButton.IsVisible = false;
        _openReleaseButton.Click += HandleOpenRelease;

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                _shortcutHeading,
                new Border
                {
                    Padding = new Thickness(14),
                    Child = _shortcutText,
                },
                _statusText,
                _autoStartCheckBox,
                CreateSettingRow(_languageLabel, _languageComboBox),
                CreateSettingRow(_themeLabel, _themeComboBox),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { _openLogsButton, _clearLogsButton },
                },
                _updateHeading,
                _checkUpdatesButton,
                _updateStatusText,
                _releaseNotesHeading,
                _releaseNotesScroll,
                _openReleaseButton,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { _closeButton, _saveButton },
                },
            },
        };
        Content = new ScrollViewer { Content = content };

        _trayViewModel.PropertyChanged += HandleTrayViewModelChanged;
        Closed += (_, _) => _trayViewModel.PropertyChanged -= HandleTrayViewModelChanged;
        KeyDown += HandleKeyDown;
        ApplyLocalizedText();
    }

    private static Grid CreateSettingRow(Control label, Control editor)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        grid.Children.Add(editor);
        return grid;
    }

    private void ApplyLocalizedText()
    {
        _updatingControls = true;
        try
        {
            Title = AppUiText.SettingsTitle;
            _shortcutHeading.Text = AppUiText.ShortcutSection;
            _shortcutText.Text = AppHotKeyFormatter.Format(_candidate);
            _statusText.Text = GetStatusText(_status);
            _saveButton.Content = AppUiText.Apply;
            _closeButton.Content = AppUiText.Close;
            _autoStartCheckBox.Content = AppUiText.AutoStart;
            _autoStartCheckBox.IsChecked = _trayViewModel.IsAutoStartEnabled;
            _languageLabel.Text = AppUiText.Language;
            _themeLabel.Text = AppUiText.Theme;
            _openLogsButton.Content = AppUiText.OpenLogs;
            _clearLogsButton.Content = AppUiText.ClearLogs;
            _updateHeading.Text = AppUiText.UpdateSection;
            _checkUpdatesButton.Content = AppUiText.CheckUpdates;
            _releaseNotesHeading.Text = AppUiText.ReleaseNotes;
            _openReleaseButton.Content = AppUiText.OpenRelease;
            _languageComboBox.ItemsSource = new[]
            {
                new Choice<AppLanguage>(AppLanguage.System, AppUiText.LanguageSystem),
                new Choice<AppLanguage>(
                    AppLanguage.SimplifiedChinese,
                    AppUiText.LanguageSimplifiedChinese),
                new Choice<AppLanguage>(AppLanguage.English, AppUiText.LanguageEnglish),
            };
            _languageComboBox.SelectedIndex = (int)_settings.Current.Language;
            _themeComboBox.ItemsSource = new[]
            {
                new Choice<AppTheme>(AppTheme.System, AppUiText.ThemeSystem),
                new Choice<AppTheme>(AppTheme.Light, AppUiText.ThemeLight),
                new Choice<AppTheme>(AppTheme.Dark, AppUiText.ThemeDark),
            };
            _themeComboBox.SelectedIndex = (int)_settings.Current.Theme;
            if (_checkingUpdates)
            {
                _updateStatusText.Text = AppUiText.CheckingUpdates;
            }
            else if (_lastUpdateResult is not null)
            {
                DisplayUpdateResult(_lastUpdateResult);
            }
        }
        finally
        {
            _updatingControls = false;
        }
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
            SetStatus(ShortcutStatus.ModifierRequired);
            return;
        }

        _candidate = new ScreenshotHotKey(modifiers, key);
        _shortcutText.Text = AppHotKeyFormatter.Format(_candidate);
        SetStatus(ShortcutStatus.ApplyHint);
        e.Handled = true;
    }

    private void HandleSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = _hotKeyManager.TryChange(_candidate);
        if (result == ScreenshotHotKeyChangeResult.Success)
        {
            _settings.Update(current => current with
            {
                HotKeyModifiers = _candidate.Modifiers,
                HotKeyKey = _candidate.Key,
            });
            SetStatus(ShortcutStatus.Saved);
        }
        else
        {
            _log.Error(AppLogEvent.HotKeyConflict);
            SetStatus(ShortcutStatus.Conflict);
        }
    }

    private void HandleAutoStartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _trayViewModel.ToggleAutoStartCommand.Execute(parameter: null);
        _autoStartCheckBox.IsChecked = _trayViewModel.IsAutoStartEnabled;
        _appearanceChanged();
    }

    private void HandleLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls ||
            _languageComboBox.SelectedItem is not Choice<AppLanguage> choice)
        {
            return;
        }

        _settings.Update(current => current with { Language = choice.Value });
        _appearanceChanged();
        ApplyLocalizedText();
    }

    private void HandleThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || _themeComboBox.SelectedItem is not Choice<AppTheme> choice)
        {
            return;
        }

        _settings.Update(current => current with { Theme = choice.Value });
        _appearanceChanged();
    }

    private void HandleOpenLogs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_folderLauncher is null)
        {
            _log.Error(AppLogEvent.PlatformUnavailable);
            SetStatus(ShortcutStatus.LogOperationFailed);
            return;
        }

        try
        {
            _folderLauncher.OpenFolder(_log.DirectoryPath);
        }
        catch (InvalidOperationException exception)
        {
            HandleLogOperationFailure(exception);
        }
        catch (IOException exception)
        {
            HandleLogOperationFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLogOperationFailure(exception);
        }
        catch (Win32Exception exception)
        {
            HandleLogOperationFailure(exception);
        }
        catch (PlatformNotSupportedException exception)
        {
            HandleLogOperationFailure(exception);
        }
    }

    private void HandleClearLogs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetStatus(_log.Clear() ? ShortcutStatus.LogsCleared : ShortcutStatus.LogOperationFailed);

    private void HandleLogOperationFailure(Exception exception)
    {
        _log.Error(AppLogEvent.PlatformUnavailable, exception);
        SetStatus(ShortcutStatus.LogOperationFailed);
    }

    private async void HandleCheckUpdates(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_checkingUpdates)
        {
            return;
        }

        _checkingUpdates = true;
        _checkUpdatesButton.IsEnabled = false;
        _openReleaseButton.IsVisible = false;
        _releaseNotesHeading.IsVisible = false;
        _releaseNotesScroll.IsVisible = false;
        _releaseNotesText.Text = string.Empty;
        _updateStatusText.Text = AppUiText.CheckingUpdates;
        try
        {
            _lastUpdateResult = await _updateCheckService.CheckAsync();
            DisplayUpdateResult(_lastUpdateResult);
            if (_lastUpdateResult.Status is UpdateCheckStatus.NetworkFailure or
                UpdateCheckStatus.RateLimited or
                UpdateCheckStatus.InvalidResponse)
            {
                _log.Error(AppLogEvent.UpdateCheckFailed);
            }
        }
        finally
        {
            _checkingUpdates = false;
            _checkUpdatesButton.IsEnabled = true;
        }
    }

    private void DisplayUpdateResult(UpdateCheckResult result)
    {
        _releaseNotesHeading.IsVisible = false;
        _releaseNotesScroll.IsVisible = false;
        _releaseNotesText.Text = string.Empty;
        _openReleaseButton.IsVisible = false;
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
            case UpdateCheckStatus.UpToDate:
                if (result.LatestVersion is null || result.PublishedAt is null)
                {
                    _updateStatusText.Text = AppUiText.UpdateInvalidResponse;
                    return;
                }

                var version = FormatVersion(result.LatestVersion);
                var summary = result.Status == UpdateCheckStatus.UpdateAvailable
                    ? AppUiText.UpdateAvailableFormat.Replace(
                        "{0}",
                        version,
                        StringComparison.Ordinal)
                    : AppUiText.UpToDateFormat.Replace(
                        "{0}",
                        version,
                        StringComparison.Ordinal);
                var publishedDate = result.PublishedAt.Value.LocalDateTime.ToString(
                    "D",
                    CultureInfo.CurrentCulture);
                var published = AppUiText.PublishedFormat.Replace(
                    "{0:D}",
                    publishedDate,
                    StringComparison.Ordinal);
                _updateStatusText.Text = $"{summary}{Environment.NewLine}{published}";
                _releaseNotesHeading.IsVisible = true;
                _releaseNotesScroll.IsVisible = true;
                _releaseNotesText.Text = result.ReleaseNotes ?? string.Empty;
                _openReleaseButton.IsVisible =
                    result.Status == UpdateCheckStatus.UpdateAvailable &&
                    result.ReleasePage is not null;
                break;

            case UpdateCheckStatus.NetworkFailure:
                _updateStatusText.Text = AppUiText.UpdateNetworkFailure;
                break;

            case UpdateCheckStatus.RateLimited:
                _updateStatusText.Text = AppUiText.UpdateRateLimited;
                break;

            case UpdateCheckStatus.InvalidResponse:
                _updateStatusText.Text = AppUiText.UpdateInvalidResponse;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    private void HandleOpenRelease(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_lastUpdateResult?.ReleasePage is not { } releasePage || _uriLauncher is null)
        {
            _updateStatusText.Text = AppUiText.OpenReleaseFailed;
            return;
        }

        try
        {
            _uriLauncher.OpenUri(releasePage);
        }
        catch (InvalidOperationException exception)
        {
            HandleOpenReleaseFailure(exception);
        }
        catch (Win32Exception exception)
        {
            HandleOpenReleaseFailure(exception);
        }
        catch (PlatformNotSupportedException exception)
        {
            HandleOpenReleaseFailure(exception);
        }
    }

    private void HandleOpenReleaseFailure(Exception exception)
    {
        _log.Error(AppLogEvent.PlatformUnavailable, exception);
        _updateStatusText.Text = AppUiText.OpenReleaseFailed;
    }

    private static string FormatVersion(Version version) => version.Revision > 0
        ? version.ToString(fieldCount: 4)
        : version.ToString(fieldCount: 3);

    private void HandleTrayViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayMenuViewModel.IsAutoStartEnabled))
        {
            _autoStartCheckBox.IsChecked = _trayViewModel.IsAutoStartEnabled;
        }
    }

    private void SetStatus(ShortcutStatus status)
    {
        _status = status;
        _statusText.Text = GetStatusText(status);
    }

    private static string GetStatusText(ShortcutStatus status) => status switch
    {
        ShortcutStatus.Instruction => AppUiText.ShortcutInstruction,
        ShortcutStatus.StartupConflict => AppUiText.ShortcutStartupConflict,
        ShortcutStatus.ModifierRequired => AppUiText.ShortcutModifierRequired,
        ShortcutStatus.ApplyHint => AppUiText.ShortcutApplyHint,
        ShortcutStatus.Saved => AppUiText.ShortcutSaved,
        ShortcutStatus.Conflict => AppUiText.ShortcutConflict,
        ShortcutStatus.LogsCleared => AppUiText.LogsCleared,
        ShortcutStatus.LogOperationFailed => AppUiText.LogOperationFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private enum ShortcutStatus
    {
        Instruction,
        StartupConflict,
        ModifierRequired,
        ApplyHint,
        Saved,
        Conflict,
        LogsCleared,
        LogOperationFailed,
    }

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
