using System.Runtime.InteropServices;

namespace Snaploom.Platform.MacOS;

internal static unsafe partial class MacOSNative
{
    private const string LibraryName = "SnaploomMacOS";

    [LibraryImport(LibraryName, EntryPoint = "snaploom_register_screenshot_hot_key")]
    internal static partial int RegisterScreenshotHotKey(
        uint keyCode,
        uint modifiers,
        delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_unregister_screenshot_hot_key")]
    internal static partial void UnregisterScreenshotHotKey();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_screen_capture_permission")]
    internal static partial int GetScreenCapturePermission();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_request_screen_capture_permission")]
    internal static partial int RequestScreenCapturePermission();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_open_screen_capture_settings")]
    internal static partial void OpenScreenCaptureSettings();

    [LibraryImport(LibraryName, EntryPoint = "snaploom_configure_capture_overlay")]
    internal static partial void ConfigureCaptureOverlay(nint nativeWindowHandle);

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

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_display_origin_x")]
    internal static partial int GetFrameDisplayOriginX(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_display_origin_y")]
    internal static partial int GetFrameDisplayOriginY(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_count")]
    internal static partial int GetFrameWindowCount(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_id")]
    internal static partial long GetFrameWindowId(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_x")]
    internal static partial int GetFrameWindowX(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_y")]
    internal static partial int GetFrameWindowY(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_width")]
    internal static partial int GetFrameWindowWidth(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_height")]
    internal static partial int GetFrameWindowHeight(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_z_order")]
    internal static partial int GetFrameWindowZOrder(nint handle, int index);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_frame_window_exclusion")]
    internal static partial uint GetFrameWindowExclusion(nint handle, int index);

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

    [LibraryImport(LibraryName, EntryPoint = "snaploom_copy_png_to_clipboard")]
    internal static partial int CopyPngToClipboard(byte* png, nuint length);

    [LibraryImport(
        LibraryName,
        EntryPoint = "snaploom_copy_text_to_clipboard",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CopyTextToClipboard(string text);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_release_string")]
    internal static partial void ReleaseString(nint value);
}
