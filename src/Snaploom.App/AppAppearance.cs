using System.Globalization;
using Avalonia.Styling;

namespace Snaploom.App;

public static class AppAppearance
{
    public static ThemeVariant GetThemeVariant(AppTheme theme) => theme switch
    {
        AppTheme.System => ThemeVariant.Default,
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(theme)),
    };

    public static CultureInfo GetCulture(AppLanguage language, CultureInfo systemCulture) =>
        language switch
        {
            AppLanguage.System => systemCulture,
            AppLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-Hans"),
            AppLanguage.English => CultureInfo.GetCultureInfo("en"),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };
}
