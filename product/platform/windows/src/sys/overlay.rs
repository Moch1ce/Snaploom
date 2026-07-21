use std::path::Path;

use super::Windows;
use snaploom_platform_contract::PlatformError;

pub(crate) fn prepare_overlay(
    owner: isize,
    x: i32,
    y: i32,
    width: u32,
    height: u32,
) -> Result<u32, PlatformError> {
    use Windows::Win32::Foundation::HWND;
    use Windows::Win32::UI::HiDpi::GetDpiForWindow;
    use Windows::Win32::UI::WindowsAndMessaging::{HWND_TOPMOST, SWP_NOACTIVATE, SetWindowPos};

    if owner == 0 {
        return Err(PlatformError::OverlayFailed);
    }
    let hwnd = HWND(owner as *mut _);
    unsafe {
        SetWindowPos(
            hwnd,
            Some(HWND_TOPMOST),
            x,
            y,
            i32::try_from(width).map_err(|_| PlatformError::OverlayFailed)?,
            i32::try_from(height).map_err(|_| PlatformError::OverlayFailed)?,
            SWP_NOACTIVATE,
        )
    }
    .map_err(|_| PlatformError::OverlayFailed)?;
    let dpi = unsafe { GetDpiForWindow(hwnd) };
    if dpi == 0 {
        return Err(PlatformError::OverlayFailed);
    }
    Ok(dpi)
}

pub(crate) fn atomic_replace(source: &Path, destination: &Path) -> Result<(), PlatformError> {
    use Windows::Win32::Storage::FileSystem::{
        MOVEFILE_REPLACE_EXISTING, MOVEFILE_WRITE_THROUGH, MoveFileExW,
    };
    use std::os::windows::ffi::OsStrExt;

    let source: Vec<u16> = source
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let destination: Vec<u16> = destination
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    unsafe {
        MoveFileExW(
            windows_core::PCWSTR(source.as_ptr()),
            windows_core::PCWSTR(destination.as_ptr()),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH,
        )
    }
    .map_err(|_| PlatformError::FileWriteFailed)
}
