using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Snaploom.App;

internal static class AppUiTheme
{
    internal static readonly IBrush WindowSurfaceBrush =
        new SolidColorBrush(Color.Parse("#FFFFFF"));
    internal static readonly IBrush WindowChromeBrush =
        new SolidColorBrush(Color.Parse("#FAFAFA"));
    internal static readonly IBrush WindowBorderBrush =
        new SolidColorBrush(Color.Parse("#D8DADF"));
    internal static readonly IBrush PrimaryTextBrush =
        new SolidColorBrush(Color.Parse("#202124"));
    internal static readonly IBrush AccentBrush =
        new SolidColorBrush(Color.Parse("#07C977"));
    internal static readonly IBrush AccentHoverBrush =
        new SolidColorBrush(Color.Parse("#06B96D"));
    internal static readonly IBrush AccentPressedBrush =
        new SolidColorBrush(Color.Parse("#05A862"));
    internal static readonly IBrush AccentTextBrush = Brushes.White;
    internal static readonly IBrush SecondaryButtonBrush =
        new SolidColorBrush(Color.Parse("#FFFFFF"));
    internal static readonly IBrush SecondaryButtonHoverBrush =
        new SolidColorBrush(Color.Parse("#F2F2F2"));
    internal static readonly IBrush CloseButtonHoverBrush =
        new SolidColorBrush(Color.Parse("#F2F2F2"));
    internal static readonly IBrush CloseButtonPressedBrush =
        new SolidColorBrush(Color.Parse("#E7E7E7"));
    internal static readonly Geometry CloseIconGeometry =
        Geometry.Parse("M 2 2 L 10 10 M 10 2 L 2 10");

    internal const double WindowBorderThickness = 1;
    internal const double TitleBarHeight = 44;
    internal const double TitleFontSize = 13;
    internal const double TitleHorizontalPadding = 16;
    internal const double CloseButtonSize = 42;
    internal const double CloseIconSize = 12;
    internal const double CloseIconThickness = 1.5;
    internal const double ButtonCornerRadius = 6;

    internal static void StylePrimaryButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        StyleButton(
            button,
            AccentBrush,
            AccentHoverBrush,
            AccentPressedBrush,
            AccentTextBrush,
            borderThickness: 0);
    }

    internal static void StyleSecondaryButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        StyleButton(
            button,
            SecondaryButtonBrush,
            SecondaryButtonHoverBrush,
            CloseButtonPressedBrush,
            PrimaryTextBrush,
            WindowBorderThickness);
    }

    private static void StyleButton(
        Button button,
        IBrush background,
        IBrush hoveredBackground,
        IBrush pressedBackground,
        IBrush foreground,
        double borderThickness)
    {
        button.Background = background;
        button.Foreground = foreground;
        button.BorderBrush = WindowBorderBrush;
        button.BorderThickness = new Thickness(borderThickness);
        button.CornerRadius = new CornerRadius(ButtonCornerRadius);
        button.Padding = new Thickness(14, 7);
        button.Resources["ButtonBackgroundPointerOver"] = hoveredBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonBorderBrushPointerOver"] = WindowBorderBrush;
        button.Resources["ButtonBorderBrushPressed"] = WindowBorderBrush;
    }
}
