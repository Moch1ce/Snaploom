use std::ffi::c_void;
use std::sync::Arc;

use super::Windows;
use snaploom_platform_contract::PlatformError;

type ResumeCallback = Arc<dyn Fn() + Send + Sync + 'static>;

pub(crate) struct ResumeLease {
    handle: *mut c_void,
    callback: Box<ResumeCallback>,
}

unsafe impl Send for ResumeLease {}
unsafe impl Sync for ResumeLease {}

pub(crate) fn watch_resume(callback: ResumeCallback) -> Result<ResumeLease, PlatformError> {
    use Windows::Win32::Foundation::HANDLE;
    use Windows::Win32::System::Power::{
        DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS, PowerRegisterSuspendResumeNotification,
    };
    use Windows::Win32::UI::WindowsAndMessaging::DEVICE_NOTIFY_CALLBACK;

    let callback = Box::new(callback);
    let mut parameters = DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS {
        Callback: Some(resume_callback),
        Context: (&*callback as *const ResumeCallback).cast_mut().cast(),
    };
    let mut handle = std::ptr::null_mut();
    let status = unsafe {
        PowerRegisterSuspendResumeNotification(
            DEVICE_NOTIFY_CALLBACK,
            HANDLE((&mut parameters as *mut DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS).cast()),
            &mut handle,
        )
    };
    if status.0 != 0 || handle.is_null() {
        return Err(PlatformError::InternalState);
    }
    Ok(ResumeLease { handle, callback })
}

unsafe extern "system" fn resume_callback(
    context: *const c_void,
    event_type: u32,
    _setting: *const c_void,
) -> u32 {
    if event_type == Windows::Win32::UI::WindowsAndMessaging::PBT_APMRESUMEAUTOMATIC
        && !context.is_null()
    {
        let callback = unsafe { &*context.cast::<ResumeCallback>() };
        callback();
    }
    0
}

impl Drop for ResumeLease {
    fn drop(&mut self) {
        use Windows::Win32::System::Power::{
            HPOWERNOTIFY, PowerUnregisterSuspendResumeNotification,
        };

        let _keep_alive = &self.callback;
        let _ =
            unsafe { PowerUnregisterSuspendResumeNotification(HPOWERNOTIFY(self.handle as isize)) };
    }
}
