using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App.HeadlessTests;

public sealed class CrossPlatformWindowStyleTests
{
    [AvaloniaFact]
    public void PermissionGuideUsesTheSharedWindowChrome()
    {
        var window = new ScreenCapturePermissionWindow(new FakePermissionService());
        try
        {
            window.Show();

            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
            Assert.Same(AppUiTheme.WindowSurfaceBrush, window.Background);
            var titleBar = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == AppWindowChrome.TitleBarName);
            Assert.Equal(AppUiTheme.TitleBarHeight, titleBar.Height);
            Assert.Same(AppUiTheme.WindowChromeBrush, titleBar.Background);

            var closeButton = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(control => control.Name == AppWindowChrome.CloseButtonName);
            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    private sealed class FakePermissionService : IScreenCapturePermissionService
    {
        public ScreenCapturePermissionStatus GetPermissionStatus() =>
            ScreenCapturePermissionStatus.NotGranted;

        public bool RequestPermission() => false;

        public void OpenPermissionSettings()
        {
        }
    }
}
