using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;

namespace Snaploom.App;

internal static class AppCursorStyles
{
    internal static readonly Cursor PointerCursor = new(StandardCursorType.Hand);
    internal static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);

    internal static IReadOnlyList<Style> Create() =>
    [
        CreateInteractiveStyle<Button>(),
        CreateDisabledStyle<Button>(),
        CreateInteractiveStyle<CheckBox>(),
        CreateDisabledStyle<CheckBox>(),
        CreateInteractiveStyle<ComboBox>(),
        CreateDisabledStyle<ComboBox>(),
        CreateInteractiveStyle<ComboBoxItem>(),
        CreateDisabledStyle<ComboBoxItem>(),
        CreateInteractiveStyle<ScreenshotToolbarButton>(),
        CreateDisabledStyle<ScreenshotToolbarButton>(),
    ];

    private static Style CreateInteractiveStyle<T>()
        where T : Control =>
        new(selector => selector.OfType<T>())
        {
            Setters =
            {
                new Setter(InputElement.CursorProperty, PointerCursor),
            },
        };

    private static Style CreateDisabledStyle<T>()
        where T : Control =>
        new(selector => selector.OfType<T>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(InputElement.CursorProperty, DefaultCursor),
            },
        };
}
