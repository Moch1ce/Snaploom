#[cfg(any(windows, test))]
mod clipboard;
#[cfg(any(windows, test))]
mod hdr;
#[cfg(feature = "tauri-shell")]
mod shell;
mod shortcut;
#[cfg(windows)]
mod sys;
#[cfg(any(windows, test))]
mod window_catalog;

use snaploom_platform_contract::{CaptureSnapshot, PlatformError};
#[cfg(windows)]
use snaploom_platform_contract::{
    CaptureSnapshotDescriptor, LogicalRect, LogicalSize, PhysicalSize, PremultipliedBgraFrame,
};

/// The product supports Windows 10 22H2 (build 19045) and newer.
pub const MINIMUM_WINDOWS_BUILD: u32 = 19_045;

#[cfg(any(windows, test))]
const fn supports_windows_build(build: Option<u32>) -> bool {
    matches!(build, Some(value) if value >= MINIMUM_WINDOWS_BUILD)
}

#[cfg(feature = "tauri-shell")]
pub use shell::{PendingPngSave, WindowsShell};
#[cfg(feature = "desktop-shell")]
pub use shell::{StableNotification, TauriShortcutRegistrar};
pub use shortcut::{Shortcut, ShortcutError, ShortcutManager, ShortcutRegistrar};
#[cfg(feature = "tauri-shell")]
pub use snaploom_platform_contract::SaveDisposition;

#[derive(Debug, Default)]
pub struct WindowsPlatform;

impl WindowsPlatform {
    #[must_use]
    pub const fn new() -> Self {
        Self
    }

    #[must_use]
    pub fn capture_supported(&self) -> bool {
        #[cfg(windows)]
        {
            sys::is_supported()
        }
        #[cfg(not(windows))]
        {
            false
        }
    }

    pub fn capture_snapshot_at_scale(
        &self,
        scale_x: f64,
        scale_y: f64,
    ) -> Result<CaptureSnapshot, PlatformError> {
        if !scale_x.is_finite() || !scale_y.is_finite() || scale_x <= 0.0 || scale_y <= 0.0 {
            return Err(PlatformError::InvalidCaptureDescriptor);
        }
        #[cfg(windows)]
        {
            let capture = sys::capture_current_display()?;
            let mut session_random = [0_u8; 32];
            getrandom::fill(&mut session_random).map_err(|_| PlatformError::InternalState)?;
            let session_id = format!("windows-{}", encode_hex(&session_random[..16]));
            let window_id_secret: [u8; 16] = session_random[16..]
                .try_into()
                .expect("fixed random secret slice");
            let windows = window_catalog::normalize_windows(
                capture.windows,
                sys::current_process_id(),
                capture.display,
                &window_id_secret,
            );
            let descriptor = CaptureSnapshotDescriptor {
                session_id,
                physical_size: PhysicalSize {
                    width: capture.display.width,
                    height: capture.display.height,
                },
                logical_size: LogicalSize {
                    width: f64::from(capture.display.width) / scale_x,
                    height: f64::from(capture.display.height) / scale_y,
                },
                global_origin: snaploom_platform_contract::PhysicalPoint {
                    x: capture.display.x,
                    y: capture.display.y,
                },
                pointer_physical: capture.pointer,
                work_area_logical: LogicalRect {
                    x: f64::from(capture.work_area.x - capture.display.x) / scale_x,
                    y: f64::from(capture.work_area.y - capture.display.y) / scale_y,
                    width: f64::from(capture.work_area.width) / scale_x,
                    height: f64::from(capture.work_area.height) / scale_y,
                },
                stride: capture.stride,
                windows,
            };
            let frame = PremultipliedBgraFrame::new(&descriptor, capture.pixels)?;
            Ok(CaptureSnapshot::new(descriptor, frame))
        }
        #[cfg(not(windows))]
        {
            let _ = (scale_x, scale_y);
            Err(PlatformError::PlatformUnavailable)
        }
    }
}

#[cfg(windows)]
fn encode_hex(bytes: &[u8]) -> String {
    use std::fmt::Write;

    let mut encoded = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        write!(&mut encoded, "{byte:02x}").expect("writing to String cannot fail");
    }
    encoded
}

pub struct ResumeLease {
    #[cfg(windows)]
    _native: sys::ResumeLease,
}

impl std::fmt::Debug for ResumeLease {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("ResumeLease")
            .finish_non_exhaustive()
    }
}

pub fn watch_resume(
    callback: std::sync::Arc<dyn Fn() + Send + Sync + 'static>,
) -> Result<ResumeLease, PlatformError> {
    #[cfg(windows)]
    {
        sys::watch_resume(callback).map(|native| ResumeLease { _native: native })
    }
    #[cfg(not(windows))]
    {
        let _ = callback;
        Err(PlatformError::PlatformUnavailable)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_windows_builds_older_than_10_22h2() {
        assert!(!supports_windows_build(None));
        assert!(!supports_windows_build(Some(MINIMUM_WINDOWS_BUILD - 1)));
        assert!(supports_windows_build(Some(MINIMUM_WINDOWS_BUILD)));
        assert!(supports_windows_build(Some(MINIMUM_WINDOWS_BUILD + 1)));
    }
}
