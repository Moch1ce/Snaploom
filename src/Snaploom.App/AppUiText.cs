using System.Globalization;
using System.Resources;

namespace Snaploom.App;

public static class AppUiText
{
    private static readonly ResourceManager ResourceManager = new(
        "Snaploom.App.Resources.AppStrings",
        typeof(AppUiText).Assembly);

    public static string Get(string name, CultureInfo? culture = null) =>
        ResourceManager.GetString(name, culture ?? CultureInfo.CurrentUICulture) ?? name;

    public static string StartScreenshot => Get(nameof(StartScreenshot));
    public static string SettingsMenu => Get(nameof(SettingsMenu));
    public static string AutoStart => Get(nameof(AutoStart));
    public static string Exit => Get(nameof(Exit));
    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string ShortcutSection => Get(nameof(ShortcutSection));
    public static string ShortcutInstruction => Get(nameof(ShortcutInstruction));
    public static string ShortcutStartupConflict => Get(nameof(ShortcutStartupConflict));
    public static string Apply => Get(nameof(Apply));
    public static string Close => Get(nameof(Close));
    public static string ShortcutModifierRequired => Get(nameof(ShortcutModifierRequired));
    public static string ShortcutApplyHint => Get(nameof(ShortcutApplyHint));
    public static string ShortcutSaved => Get(nameof(ShortcutSaved));
    public static string ShortcutConflict => Get(nameof(ShortcutConflict));
    public static string Language => Get(nameof(Language));
    public static string LanguageSystem => Get(nameof(LanguageSystem));
    public static string LanguageSimplifiedChinese => Get(nameof(LanguageSimplifiedChinese));
    public static string LanguageEnglish => Get(nameof(LanguageEnglish));
    public static string Theme => Get(nameof(Theme));
    public static string ThemeSystem => Get(nameof(ThemeSystem));
    public static string ThemeLight => Get(nameof(ThemeLight));
    public static string ThemeDark => Get(nameof(ThemeDark));
    public static string PermissionTitle => Get(nameof(PermissionTitle));
    public static string PermissionDescription => Get(nameof(PermissionDescription));
    public static string ContinueAuthorization => Get(nameof(ContinueAuthorization));
    public static string OpenSystemSettings => Get(nameof(OpenSystemSettings));
    public static string PermissionRestartHint => Get(nameof(PermissionRestartHint));
    public static string Later => Get(nameof(Later));
    public static string PermissionNotGranted => Get(nameof(PermissionNotGranted));
    public static string CaptureFailureTitle => Get(nameof(CaptureFailureTitle));
    public static string ExitScreenshot => Get(nameof(ExitScreenshot));
    public static string CaptureFailureHeading => Get(nameof(CaptureFailureHeading));
    public static string CaptureSystemFailure => Get(nameof(CaptureSystemFailure));
    public static string CaptureUnexpectedFailure => Get(nameof(CaptureUnexpectedFailure));
    public static string ModifierCommand => Get(nameof(ModifierCommand));
    public static string ModifierControl => Get(nameof(ModifierControl));
    public static string ModifierAlt => Get(nameof(ModifierAlt));
    public static string ModifierShift => Get(nameof(ModifierShift));
}
