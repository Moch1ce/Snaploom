using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Snaploom.App;

internal static class AppWindowChrome
{
    internal const string TitleBarName = "PART_AppWindowTitleBar";
    internal const string CloseButtonName = "PART_AppWindowCloseButton";

    internal static void Configure(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.RequestedThemeVariant = ThemeVariant.Light;
        window.WindowDecorations = WindowDecorations.None;
        window.Background = AppUiTheme.WindowSurfaceBrush;
    }

    internal static Control Wrap(Window window, Control content)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(content);

        var closeButton = CreateCloseButton(window);
        var titleText = new TextBlock
        {
            Text = window.Title,
            Foreground = AppUiTheme.PrimaryTextBrush,
            FontSize = AppUiTheme.TitleFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(AppUiTheme.TitleHorizontalPadding, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        window.PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.TitleProperty)
            {
                titleText.Text = window.Title;
                AutomationProperties.SetName(closeButton, AppUiText.Close);
                ToolTip.SetTip(closeButton, AppUiText.Close);
            }
        };

        Grid.SetColumn(closeButton, 1);
        var titleBarLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { titleText, closeButton },
        };
        var titleBar = new Border
        {
            Name = TitleBarName,
            Height = AppUiTheme.TitleBarHeight,
            Background = AppUiTheme.WindowChromeBrush,
            BorderBrush = AppUiTheme.WindowBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, AppUiTheme.WindowBorderThickness),
            Child = titleBarLayout,
        };
        titleBar.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
            {
                return;
            }

            window.BeginMoveDrag(args);
            args.Handled = true;
        };

        Grid.SetRow(content, 1);
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { titleBar, content },
        };
        return new Border
        {
            Background = AppUiTheme.WindowSurfaceBrush,
            BorderBrush = AppUiTheme.WindowBorderBrush,
            BorderThickness = new Thickness(AppUiTheme.WindowBorderThickness),
            Child = layout,
        };
    }

    private static Button CreateCloseButton(Window window)
    {
        var closeButton = new Button
        {
            Name = CloseButtonName,
            Width = AppUiTheme.CloseButtonSize,
            Height = AppUiTheme.CloseButtonSize,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Width = AppUiTheme.CloseIconSize,
                Height = AppUiTheme.CloseIconSize,
                Data = AppUiTheme.CloseIconGeometry,
                Stroke = AppUiTheme.PrimaryTextBrush,
                StrokeThickness = AppUiTheme.CloseIconThickness,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
            },
        };
        closeButton.Resources["ButtonBackgroundPointerOver"] =
            AppUiTheme.CloseButtonHoverBrush;
        closeButton.Resources["ButtonBackgroundPressed"] =
            AppUiTheme.CloseButtonPressedBrush;
        closeButton.Resources["ButtonBorderBrushPointerOver"] =
            AppUiTheme.CloseButtonHoverBrush;
        closeButton.Resources["ButtonBorderBrushPressed"] =
            AppUiTheme.CloseButtonPressedBrush;
        AutomationProperties.SetName(closeButton, AppUiText.Close);
        ToolTip.SetTip(closeButton, AppUiText.Close);
        closeButton.Click += (_, _) => window.Close();
        return closeButton;
    }
}
