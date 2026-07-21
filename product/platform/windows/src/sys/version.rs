use super::Windows;

pub(crate) fn windows_build() -> Option<u32> {
    use Windows::Win32::System::SystemInformation::{GetVersionExW, OSVERSIONINFOW};

    let mut version = OSVERSIONINFOW {
        dwOSVersionInfoSize: u32::try_from(std::mem::size_of::<OSVERSIONINFOW>()).ok()?,
        ..Default::default()
    };
    unsafe { GetVersionExW(&mut version) }.ok()?;
    Some(version.dwBuildNumber)
}

pub(crate) fn current_process_id() -> u32 {
    unsafe { Windows::Win32::System::Threading::GetCurrentProcessId() }
}
