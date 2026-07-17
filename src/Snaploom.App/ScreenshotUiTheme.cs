using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Snaploom.App;

internal static class ScreenshotUiTheme
{
    internal static readonly Color AccentColor = Color.Parse("#07C977");
    internal static readonly Color DangerColor = Color.Parse("#FF4D4F");
    internal static readonly IBrush AccentBrush = new SolidColorBrush(AccentColor);
    internal static readonly IBrush DangerBrush = new SolidColorBrush(DangerColor);
    internal static readonly IBrush TransparentBrush = Brushes.Transparent;
    internal static readonly IBrush PrimaryTextBrush = new SolidColorBrush(Color.Parse("#202124"));
    internal static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#96999F"));
    internal static readonly IBrush DisabledIconBrush = new SolidColorBrush(Color.Parse("#A8ABB2"));
    internal static readonly IBrush FloatingSurfaceBrush = new SolidColorBrush(Color.Parse("#FAFAFA"));
    internal static readonly IBrush FloatingBorderBrush = new SolidColorBrush(Color.Parse("#D8DADF"));
    internal static readonly IBrush TextEditorBorderBrush = new SolidColorBrush(Color.Parse("#8F949B"));
    internal static readonly IBrush TextEditorControlPointBrush = Brushes.White;
    internal static readonly IBrush TextEditorSelectionBrush = new SolidColorBrush(
        Color.FromArgb(64, AccentColor.R, AccentColor.G, AccentColor.B));
    internal static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.Parse("#E1E3E7"));
    internal static readonly IBrush HoveredToolBrush = new SolidColorBrush(Color.Parse("#F2F2F2"));
    internal static readonly IBrush SelectedToolBrush = new SolidColorBrush(Color.Parse("#EDEDED"));
    internal static readonly IBrush SizeBadgeBrush = new SolidColorBrush(Color.FromArgb(235, 20, 21, 23));
    internal static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
    internal static readonly Geometry AnnotationOptionsPointerGeometry =
        Geometry.Parse("M 0 7 L 6 0 L 12 7 Z");
    internal static readonly BoxShadows FloatingShadow = new(
        new BoxShadow
        {
            OffsetY = 5,
            Blur = 18,
            Color = Color.FromArgb(48, 0, 0, 0),
        });

    internal static Style CreateTextEditorFocusChromeStyle() =>
        new(
            selector => selector
                .OfType<TextBox>()
                .Class(":focus-within")
                .Template()
                .OfType<Border>()
                .Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, TransparentBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
            },
        };

    internal const double FloatingCornerRadius = 8;
    internal const double FloatingBorderThickness = 1;
    internal const double ToolbarHeight = 44;
    internal const double ToolbarButtonSize = 40;
    internal const double ToolbarHorizontalPadding = 14;
    internal const double ToolbarSeparatorWidth = 1;
    internal const double ToolbarSeparatorMargin = 10;
    internal const double HoveredToolBackgroundSize = 28;
    internal const double SelectedToolBackgroundSize = 24;
    internal const double SelectionHandleSize = 8;
    internal const double IconSize = 18;
    internal const double TextEditorMinimumWidth = 24;
    internal const double TextEditorCornerRadius = 2;
    internal const double TextEditorControlPointSize = 6;
    internal const double TextEditorHorizontalPadding = 4;
    internal const double TextEditorVerticalPadding = 1;
    internal const double TextEditorLineHeightMultiplier = 1.35;
    internal const double TextEditorMeasuredWidthPadding = 10;
    internal const double TextEditorMeasuredHeightPadding = 4;
    internal const double AnnotationOptionsFlyoutVerticalOffset = 4;
    internal const double AnnotationOptionsPointerWidth = 12;
    internal const double AnnotationOptionsPointerHeight = 7;
    internal const double AnnotationOptionsPointerLeftMargin = 14;
    internal const double AnnotationOptionsPointerOverlap = 1;
    internal const double AnnotationOptionsSurfaceHorizontalPadding = 4;
}
