using System.Globalization;
using System.Resources;

namespace Snaploom.App;

internal static class ScreenshotUiText
{
    private static readonly ResourceManager ResourceManager = new(
        "Snaploom.App.Resources.ScreenshotUiStrings",
        typeof(ScreenshotUiText).Assembly);

    internal static string WindowTitle => Get(nameof(WindowTitle));

    internal static string RectangleUnavailable => Get(nameof(RectangleUnavailable));

    internal static string ArrowUnavailable => Get(nameof(ArrowUnavailable));

    internal static string TextUnavailable => Get(nameof(TextUnavailable));

    internal static string MosaicUnavailable => Get(nameof(MosaicUnavailable));

    internal static string UndoUnavailable => Get(nameof(UndoUnavailable));

    internal static string SavePng => Get(nameof(SavePng));

    internal static string ExitScreenshot => Get(nameof(ExitScreenshot));

    internal static string CompleteAndCopy => Get(nameof(CompleteAndCopy));

    internal static string ChoosingSaveLocation => Get(nameof(ChoosingSaveLocation));

    internal static string CopyingImage => Get(nameof(CopyingImage));

    internal static string SaveFailed => Get(nameof(SaveFailed));

    internal static string CopyImageFailed => Get(nameof(CopyImageFailed));

    internal static string CopyColorFailed => Get(nameof(CopyColorFailed));

    internal static string ClipboardUnavailable => Get(nameof(ClipboardUnavailable));

    internal static string CoordinateLabel => Get(nameof(CoordinateLabel));

    internal static string ColorLabel => Get(nameof(ColorLabel));

    internal static string CopyColorHint => OperatingSystem.IsMacOS()
        ? Get("CopyColorHintMacOS")
        : Get("CopyColorHintWindows");

    private static string Get(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
