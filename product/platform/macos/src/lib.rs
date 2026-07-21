#[cfg(any(target_os = "macos", test))]
mod coordinates;
#[cfg(any(target_os = "macos", test))]
mod frame;
#[cfg(feature = "tauri-shell")]
mod shell;
#[cfg(target_os = "macos")]
mod sys;
#[cfg(any(target_os = "macos", test))]
mod window_catalog;

use snaploom_platform_contract::{CapturePermissionState, CaptureSnapshot, PlatformError};

#[cfg(feature = "tauri-shell")]
pub use shell::{MacShell, PendingPngSave};
#[cfg(feature = "tauri-shell")]
pub use snaploom_platform_contract::SaveDisposition;

/// Snaploom v1 supports macOS 14 Sonoma and newer on Apple Silicon.
pub const MINIMUM_MACOS_MAJOR: u32 = 14;

#[derive(Debug, Default)]
pub struct MacPlatform;

impl MacPlatform {
    #[must_use]
    pub const fn new() -> Self {
        Self
    }

    #[must_use]
    pub fn permission_granted(&self) -> bool {
        #[cfg(target_os = "macos")]
        {
            sys::preflight_screen_capture_access()
        }
        #[cfg(not(target_os = "macos"))]
        {
            false
        }
    }

    /// This is intentionally separate from capture; callers may invoke it only
    /// from an explicit user authorization action.
    pub fn request_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            Ok(sys::request_screen_capture_access())
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::PlatformUnavailable)
        }
    }

    pub fn open_permission_settings(&self) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            sys::open_screen_capture_settings()
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::PlatformUnavailable)
        }
    }

    pub fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            sys::capture_current_display()
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::PlatformUnavailable)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn declares_sonoma_as_the_minimum_supported_release() {
        assert_eq!(MINIMUM_MACOS_MAJOR, 14);
    }

    #[test]
    fn non_macos_build_does_not_fake_capture_or_prompt_permission() {
        #[cfg(not(target_os = "macos"))]
        {
            let platform = MacPlatform::new();
            assert!(!platform.permission_granted());
            assert!(matches!(
                platform.request_permission(),
                Err(PlatformError::PlatformUnavailable)
            ));
            assert!(matches!(
                platform.open_permission_settings(),
                Err(PlatformError::PlatformUnavailable)
            ));
            assert!(matches!(
                platform.capture_snapshot(),
                Err(PlatformError::PlatformUnavailable)
            ));
        }
    }

    #[cfg(target_os = "macos")]
    #[test]
    #[ignore = "requires an installed, TCC-authorized macOS app identity"]
    fn captures_one_real_sck_frame() {
        assert_eq!(
            std::env::var("SNAPLOOM_RUN_SCREEN_CAPTURE_INTEGRATION").as_deref(),
            Ok("1"),
            "set the explicit integration-test opt-in"
        );
        let snapshot = MacPlatform::new().capture_snapshot().unwrap();
        let descriptor = snapshot.descriptor();
        assert!(descriptor.physical_size.width > 0);
        assert!(descriptor.physical_size.height > 0);
        assert_eq!(snapshot.frame().pixel_format(), "BGRA8_PREMULTIPLIED");
        assert_eq!(
            snapshot.frame().bytes().len(),
            descriptor.stride as usize * descriptor.physical_size.height as usize
        );
    }
}
