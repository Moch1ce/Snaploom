use std::path::{Path, PathBuf};

use snaploom_capture_protocol::StableError;

pub(crate) fn resolve_host_executable(
    override_path: Option<&Path>,
) -> Result<PathBuf, StableError> {
    resolve_host_executable_with(override_path, discover_installed_host)
}

fn resolve_host_executable_with(
    override_path: Option<&Path>,
    discover_installed: impl FnOnce() -> Option<PathBuf>,
) -> Result<PathBuf, StableError> {
    override_path
        .map(Path::to_path_buf)
        .or_else(discover_installed)
        .ok_or(StableError::HostNotFound)
}

#[cfg(target_os = "macos")]
fn discover_installed_host() -> Option<PathBuf> {
    use objc2_app_kit::NSWorkspace;
    use objc2_foundation::NSString;

    let bundle_identifier = NSString::from_str("com.snaploom.capture-host");
    let workspace = NSWorkspace::sharedWorkspace();
    let application = workspace.URLForApplicationWithBundleIdentifier(&bundle_identifier)?;
    let application_path = application.path()?;
    Some(PathBuf::from(application_path.to_string()).join("Contents/MacOS/snaploom-capture-host"))
}

#[cfg(windows)]
fn discover_installed_host() -> Option<PathBuf> {
    use std::ptr;

    use windows_sys::Win32::Foundation::ERROR_SUCCESS;
    use windows_sys::Win32::System::Registry::{
        HKEY, HKEY_CURRENT_USER, KEY_READ, REG_SZ, RegCloseKey, RegOpenKeyExW, RegQueryValueExW,
    };

    struct RegistryKey(HKEY);

    impl Drop for RegistryKey {
        fn drop(&mut self) {
            unsafe {
                RegCloseKey(self.0);
            }
        }
    }

    let subkey: Vec<u16> = "Software\\Snaploom\\CaptureHost\0".encode_utf16().collect();
    let mut raw_key = ptr::null_mut();
    let open_result = unsafe {
        RegOpenKeyExW(
            HKEY_CURRENT_USER,
            subkey.as_ptr(),
            0,
            KEY_READ,
            &mut raw_key,
        )
    };
    if open_result != ERROR_SUCCESS {
        return None;
    }
    let capture_host = RegistryKey(raw_key);

    let value_name: Vec<u16> = "InstallPath\0".encode_utf16().collect();
    let mut value_type = 0;
    let mut value_bytes = 0;
    let size_result = unsafe {
        RegQueryValueExW(
            capture_host.0,
            value_name.as_ptr(),
            ptr::null(),
            &mut value_type,
            ptr::null_mut(),
            &mut value_bytes,
        )
    };
    if size_result != ERROR_SUCCESS || value_type != REG_SZ || value_bytes % 2 != 0 {
        return None;
    }

    let mut value = vec![0_u16; usize::try_from(value_bytes / 2).ok()?];
    let read_result = unsafe {
        RegQueryValueExW(
            capture_host.0,
            value_name.as_ptr(),
            ptr::null(),
            &mut value_type,
            value.as_mut_ptr().cast(),
            &mut value_bytes,
        )
    };
    if read_result != ERROR_SUCCESS || value_type != REG_SZ || value_bytes % 2 != 0 {
        return None;
    }

    let value_len = usize::try_from(value_bytes / 2).ok()?;
    let value = value.get(..value_len)?;
    let value = value.strip_suffix(&[0]).unwrap_or(value);
    let path = String::from_utf16(value).ok()?;
    Some(PathBuf::from(path))
}

#[cfg(not(any(target_os = "macos", windows)))]
fn discover_installed_host() -> Option<PathBuf> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn explicit_override_wins_over_platform_registration() {
        let override_path = Path::new("/opt/company/snaploom-capture-host");
        let resolved = resolve_host_executable_with(Some(override_path), || {
            Some(PathBuf::from("/Applications/Snaploom Capture Host.app"))
        })
        .unwrap();

        assert_eq!(resolved, override_path);
    }

    #[test]
    fn platform_registration_is_used_when_override_is_absent() {
        let installed = PathBuf::from(
            "/Applications/Snaploom Capture Host.app/Contents/MacOS/snaploom-capture-host",
        );
        let resolved = resolve_host_executable_with(None, || Some(installed.clone())).unwrap();

        assert_eq!(resolved, installed);
    }

    #[test]
    fn missing_override_and_registration_is_host_not_found() {
        assert_eq!(
            resolve_host_executable_with(None, || None),
            Err(StableError::HostNotFound)
        );
    }
}
