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

    internal static string RectangleTool => Get(nameof(RectangleTool));

    internal static string ArrowUnavailable => Get(nameof(ArrowUnavailable));

    internal static string ArrowTool => Get(nameof(ArrowTool));

    internal static string ColorRed => Get(nameof(ColorRed));

    internal static string ColorYellow => Get(nameof(ColorYellow));

    internal static string ColorGreen => Get(nameof(ColorGreen));

    internal static string ColorBlue => Get(nameof(ColorBlue));

    internal static string ColorBlack => Get(nameof(ColorBlack));

    internal static string ColorWhite => Get(nameof(ColorWhite));

    internal static string LineWidth2 => Get(nameof(LineWidth2));

    internal static string LineWidth4 => Get(nameof(LineWidth4));

    internal static string LineWidth8 => Get(nameof(LineWidth8));

    internal static string TextTool => Get(nameof(TextTool));

    internal static string FontSize16 => Get(nameof(FontSize16));

    internal static string FontSize24 => Get(nameof(FontSize24));

    internal static string FontSize32 => Get(nameof(FontSize32));

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

    internal static string CoordinateLabel => Get(nameof(CoordinateLabel));

    internal static string ColorLabel => Get(nameof(ColorLabel));

    internal static string CopyColorHint => OperatingSystem.IsMacOS()
        ? Get("CopyColorHintMacOS")
        : Get("CopyColorHintWindows");

    private static string Get(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
