using System.Globalization;
using Snaploom.App;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.IntegrationTests;

public sealed class AppSettingsTests
{
    [Fact]
    public void SettingsRoundTripEveryPersistedPreference()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var defaults = AppSettings.CreateDefault(DesktopPlatformKind.Windows);
            var expected = defaults with
            {
                HotKeyModifiers = ScreenshotHotKeyModifiers.Control |
                    ScreenshotHotKeyModifiers.Shift,
                HotKeyKey = ScreenshotHotKeyKey.G,
                AutoStart = true,
                AnnotationColor = ScreenshotAnnotationColor.Blue,
                AnnotationLineWidth = 8,
                TextColor = ScreenshotAnnotationColor.Green,
                TextFontSize = 32,
                MosaicBrushSize = 64,
                LastSaveDirectory = "/screenshots",
                Language = AppLanguage.English,
                Theme = AppTheme.Dark,
            };
            var store = new JsonAppSettingsStore(path, defaults);

            store.Save(expected);
            var actual = store.Load();

            Assert.Equal(expected, actual);
            Assert.Equal(
                new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 8),
                actual.AnnotationStyle);
            Assert.Equal(
                new ScreenshotTextStyle(ScreenshotAnnotationColor.Green, 32),
                actual.TextStyle);
            Assert.Equal(new ScreenshotMosaicStyle(64, 16), actual.MosaicStyle);
            Assert.DoesNotContain(".tmp", Directory.EnumerateFiles(directory).Single());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"schemaVersion\":999,\"theme\":2}")]
    [InlineData("{\"schemaVersion\":1,\"annotationLineWidth\":99}")]
    public void InvalidOrFutureSettingsFallBackSafely(string content)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, content);
            var defaults = AppSettings.CreateDefault(DesktopPlatformKind.MacOS);
            var store = new JsonAppSettingsStore(path, defaults);

            var actual = store.Load();

            Assert.Equal(defaults, actual);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResourceFallbackUsesEnglishForUnsupportedCultures()
    {
        Assert.Equal("开始截图", AppUiText.Get("StartScreenshot", new CultureInfo("zh-CN")));
        Assert.Equal("Start screenshot", AppUiText.Get("StartScreenshot", new CultureInfo("fr-FR")));
    }

    [Theory]
    [InlineData(AppTheme.System, "Default")]
    [InlineData(AppTheme.Light, "Light")]
    [InlineData(AppTheme.Dark, "Dark")]
    public void ThemePreferenceMapsToAvaloniaThemeVariant(AppTheme theme, string expectedKey)
    {
        Assert.Equal(expectedKey, AppAppearance.GetThemeVariant(theme).Key);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"snaploom-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
