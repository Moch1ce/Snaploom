using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(
    typeof(Snaploom.App.HeadlessTests.TestAppBuilder))]

namespace Snaploom.App.HeadlessTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<Snaploom.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                ShouldRenderOnUIThread = true,
                UseHeadlessDrawing = false,
            });
}
