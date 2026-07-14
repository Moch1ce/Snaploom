using System.Runtime.InteropServices;

namespace Snaploom.Platform.MacOS;

internal static unsafe partial class MacOSNative
{
    private const string LibraryName = "SnaploomMacOS";

    [LibraryImport(LibraryName, EntryPoint = "snaploom_register_screenshot_hot_key")]
    internal static partial int RegisterScreenshotHotKey(delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_unregister_screenshot_hot_key")]
    internal static partial void UnregisterScreenshotHotKey();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_screen_capture_permission")]
    internal static partial int GetScreenCapturePermission();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_request_screen_capture_permission")]
    internal static partial int RequestScreenCapturePermission();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_open_screen_capture_settings")]
    internal static partial void OpenScreenCaptureSettings();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_current_display")]
    internal static partial nint CaptureCurrentDisplay();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_status")]
    internal static partial int GetFrameStatus(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_width")]
    internal static partial int GetFrameWidth(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_height")]
    internal static partial int GetFrameHeight(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_stride")]
    internal static partial int GetFrameStride(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_logical_width")]
    internal static partial double GetFrameLogicalWidth(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_logical_height")]
    internal static partial double GetFrameLogicalHeight(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_cursor_x")]
    internal static partial double GetFrameCursorX(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_cursor_y")]
    internal static partial double GetFrameCursorY(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_pixel_data")]
    internal static partial nint GetFramePixelData(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_pixel_length")]
    internal static partial nint GetFramePixelLength(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_error_message")]
    internal static partial nint GetFrameErrorMessage(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_release_frame")]
    internal static partial void ReleaseFrame(nint handle);

    [LibraryImport(
        LibraryName,
        EntryPoint = "snaploom_show_png_save_panel",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint ShowPngSavePanel(string suggestedName);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_release_string")]
    internal static partial void ReleaseString(nint value);
}
