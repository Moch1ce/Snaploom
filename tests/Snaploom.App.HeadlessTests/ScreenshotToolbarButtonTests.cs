using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotToolbarButtonTests
{
    [AvaloniaFact]
    public void InvokesWhenPointerLeavesAndReturnsBeforeRelease()
    {
        var button = new ScreenshotToolbarButton(
            ScreenshotToolbarIconKind.Confirm,
            "Confirm",
            Brushes.Green,
            isEnabled: true);
        var invocationCount = 0;
        button.Invoked += (_, _) => invocationCount++;

        var window = new Window
        {
            Width = 100,
            Height = 100,
            Content = button,
        };
        try
        {
            window.Show();

            window.MouseMove(new Point(50, 50), RawInputModifiers.None);
            window.MouseDown(
                new Point(50, 50),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(90, 90), RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(50, 50), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(50, 50), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, invocationCount);
        }
        finally
        {
            window.Close();
        }
    }
}
