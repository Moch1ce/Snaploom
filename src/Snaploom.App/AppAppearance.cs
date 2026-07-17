using System.Globalization;

namespace Snaploom.App;

public static class AppAppearance
{
    public static CultureInfo GetCulture(AppLanguage language, CultureInfo systemCulture) =>
        language switch
        {
            AppLanguage.System => systemCulture,
            AppLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-Hans"),
            AppLanguage.English => CultureInfo.GetCultureInfo("en"),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };
}
