use std::ffi::c_void;
use std::path::{Path, PathBuf};

use snaploom_platform_contract::PlatformError;

use super::Windows;

const ERROR_CANCELLED_HRESULT: i32 = 0x8007_04c7_u32 as i32;

pub(crate) fn choose_png_destination(
    owner: isize,
    suggested_name: &str,
    recent_directory: Option<&Path>,
) -> Result<Option<PathBuf>, PlatformError> {
    let suggested_name = suggested_name.to_owned();
    let recent_directory = recent_directory.map(Path::to_path_buf);
    std::thread::Builder::new()
        .name("snaploom-windows-save-dialog".into())
        .spawn(move || choose_on_sta(owner, &suggested_name, recent_directory.as_deref()))
        .map_err(|_| PlatformError::SaveDialogFailed)?
        .join()
        .map_err(|_| PlatformError::SaveDialogFailed)?
}

fn choose_on_sta(
    owner: isize,
    suggested_name: &str,
    recent_directory: Option<&Path>,
) -> Result<Option<PathBuf>, PlatformError> {
    use Windows::Win32::Foundation::HWND;
    use Windows::Win32::System::Com::{
        CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx,
        CoTaskMemFree, CoUninitialize,
    };
    use Windows::Win32::UI::Shell::Common::COMDLG_FILTERSPEC;
    use Windows::Win32::UI::Shell::{
        FOS_FORCEFILESYSTEM, FOS_OVERWRITEPROMPT, FOS_PATHMUSTEXIST, FileSaveDialog,
        IFileSaveDialog, IShellItem, SHCreateItemFromParsingName, SIGDN_FILESYSPATH,
    };

    let initialized = unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED) };
    initialized
        .ok()
        .map_err(|_| PlatformError::SaveDialogFailed)?;
    struct Apartment;
    impl Drop for Apartment {
        fn drop(&mut self) {
            unsafe { CoUninitialize() };
        }
    }
    let _apartment = Apartment;

    let dialog: IFileSaveDialog = unsafe {
        CoCreateInstance(
            &FileSaveDialog,
            None::<&windows_core::IUnknown>,
            CLSCTX_INPROC_SERVER,
        )
    }
    .map_err(|_| PlatformError::SaveDialogFailed)?;
    let filter_name = wide("PNG image");
    let filter_pattern = wide("*.png");
    let extension = wide("png");
    let file_name = wide(suggested_name);
    let filter = [COMDLG_FILTERSPEC {
        pszName: windows_core::PCWSTR::from_raw(filter_name.as_ptr()),
        pszSpec: windows_core::PCWSTR::from_raw(filter_pattern.as_ptr()),
    }];
    unsafe {
        let options = dialog
            .GetOptions()
            .map_err(|_| PlatformError::SaveDialogFailed)?;
        dialog
            .SetOptions(options | FOS_FORCEFILESYSTEM | FOS_OVERWRITEPROMPT | FOS_PATHMUSTEXIST)
            .and_then(|()| dialog.SetFileTypes(&filter))
            .and_then(|()| {
                dialog.SetDefaultExtension(windows_core::PCWSTR::from_raw(extension.as_ptr()))
            })
            .and_then(|()| dialog.SetFileName(windows_core::PCWSTR::from_raw(file_name.as_ptr())))
            .map_err(|_| PlatformError::SaveDialogFailed)?;
    }

    if let Some(directory) = recent_directory.filter(|directory| directory.is_absolute()) {
        let directory = wide_path(directory);
        if let Ok(item) = unsafe {
            SHCreateItemFromParsingName::<_, _, IShellItem>(
                windows_core::PCWSTR::from_raw(directory.as_ptr()),
                None::<&Windows::Win32::System::Com::IBindCtx>,
            )
        } {
            let _ = unsafe { dialog.SetFolder(&item) };
        }
    }

    let shown = unsafe { dialog.Show(Some(HWND(owner as *mut c_void))) };
    if let Err(error) = shown {
        return if error.code().0 == ERROR_CANCELLED_HRESULT {
            Ok(None)
        } else {
            Err(PlatformError::SaveDialogFailed)
        };
    }
    let item = unsafe { dialog.GetResult() }.map_err(|_| PlatformError::SaveDialogFailed)?;
    let display_name = unsafe { item.GetDisplayName(SIGDN_FILESYSPATH) }
        .map_err(|_| PlatformError::SaveDialogFailed)?;
    if display_name.is_null() {
        return Err(PlatformError::SaveDialogFailed);
    }
    let path = PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        display_name.as_wide()
    }));
    unsafe { CoTaskMemFree(Some(display_name.as_ptr().cast())) };
    if path.as_os_str().is_empty() {
        Err(PlatformError::SaveDialogFailed)
    } else {
        Ok(Some(path))
    }
}

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(std::iter::once(0)).collect()
}

fn wide_path(path: &Path) -> Vec<u16> {
    use std::os::windows::ffi::OsStrExt;

    path.as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}

use std::os::windows::ffi::OsStringExt;
