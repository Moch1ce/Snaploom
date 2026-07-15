using Snaploom.Platform.Abstractions;

namespace Snaploom.App;

internal static class AppHotKeyFormatter
{
    internal static string Format(ScreenshotHotKey hotKey)
    {
        List<string> parts = [];
        if (hotKey.Modifiers.HasFlag(ScreenshotHotKeyModifiers.Command))
        {
            parts.Add(AppUiText.ModifierCommand);
        }

        if (hotKey.Modifiers.HasFlag(ScreenshotHotKeyModifiers.Control))
        {
            parts.Add(AppUiText.ModifierControl);
        }

        if (hotKey.Modifiers.HasFlag(ScreenshotHotKeyModifiers.Alt))
        {
            parts.Add(AppUiText.ModifierAlt);
        }

        if (hotKey.Modifiers.HasFlag(ScreenshotHotKeyModifiers.Shift))
        {
            parts.Add(AppUiText.ModifierShift);
        }

        parts.Add(hotKey.Key.ToString());
        return string.Join('+', parts);
    }
}
