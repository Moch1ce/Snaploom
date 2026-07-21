use std::{error::Error, fmt, sync::Arc};

use serde::{Deserialize, Serialize};
use zeroize::Zeroize;

pub const MAX_CAPTURE_FRAME_BYTES: usize = 256 * 1024 * 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PhysicalSize {
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PhysicalPoint {
    pub x: i32,
    pub y: i32,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LogicalSize {
    pub width: f64,
    pub height: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LogicalRect {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PhysicalRect {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WindowCandidate {
    pub stable_id: String,
    pub z_order: u32,
    pub bounds: PhysicalRect,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CaptureSnapshotDescriptor {
    pub session_id: String,
    /// Opaque native display identity bound to this capture. Consumers must
    /// not perform arithmetic on it; it exists so the platform shell can
    /// place the overlay on the exact display that produced the frame.
    pub display_id: Option<u64>,
    pub physical_size: PhysicalSize,
    pub logical_size: LogicalSize,
    /// Global physical origin when the platform exposes one coherent physical
    /// desktop plane. This is `None` on mixed-scale macOS desktops, where
    /// `display_id` is the only valid overlay-placement identity.
    pub global_origin: Option<PhysicalPoint>,
    pub pointer_physical: PhysicalPoint,
    pub work_area_logical: LogicalRect,
    pub stride: u32,
    pub windows: Vec<WindowCandidate>,
}

impl CaptureSnapshotDescriptor {
    #[must_use]
    pub fn scale(&self) -> (f64, f64) {
        (
            f64::from(self.physical_size.width) / self.logical_size.width,
            f64::from(self.physical_size.height) / self.logical_size.height,
        )
    }

    fn expected_frame_bytes(&self) -> Result<usize, PlatformError> {
        if self.physical_size.width == 0
            || self.physical_size.height == 0
            || !self.logical_size.width.is_finite()
            || !self.logical_size.height.is_finite()
            || self.logical_size.width <= 0.0
            || self.logical_size.height <= 0.0
            || self.stride < self.physical_size.width.saturating_mul(4)
        {
            return Err(PlatformError::InvalidCaptureDescriptor);
        }
        usize::try_from(self.stride)
            .ok()
            .and_then(|stride| {
                usize::try_from(self.physical_size.height)
                    .ok()
                    .and_then(|height| stride.checked_mul(height))
            })
            .ok_or(PlatformError::InvalidCaptureDescriptor)
    }
}

pub struct PremultipliedBgraFrame {
    bytes: Vec<u8>,
}

impl PremultipliedBgraFrame {
    pub fn new(
        descriptor: &CaptureSnapshotDescriptor,
        mut bytes: Vec<u8>,
    ) -> Result<Self, PlatformError> {
        let expected = match descriptor.expected_frame_bytes() {
            Ok(expected) => expected,
            Err(error) => {
                bytes.zeroize();
                return Err(error);
            }
        };
        if let Err(error) = Self::validate_length(bytes.len(), expected) {
            bytes.zeroize();
            return Err(error);
        }
        Ok(Self { bytes })
    }

    pub fn validate_length(actual: usize, expected: usize) -> Result<(), PlatformError> {
        if actual > MAX_CAPTURE_FRAME_BYTES {
            return Err(PlatformError::CaptureFrameExceedsLimit);
        }
        if actual != expected {
            return Err(PlatformError::CaptureFrameLengthMismatch);
        }
        Ok(())
    }

    #[must_use]
    pub fn bytes(&self) -> &[u8] {
        &self.bytes
    }

    #[must_use]
    pub const fn pixel_format(&self) -> &'static str {
        "BGRA8_PREMULTIPLIED"
    }

    #[must_use]
    pub fn into_bytes(mut self) -> Vec<u8> {
        std::mem::take(&mut self.bytes)
    }
}

impl fmt::Debug for PremultipliedBgraFrame {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("PremultipliedBgraFrame")
            .field("byte_length", &self.bytes.len())
            .field("pixel_format", &self.pixel_format())
            .finish()
    }
}

impl Drop for PremultipliedBgraFrame {
    fn drop(&mut self) {
        self.bytes.zeroize();
    }
}

#[derive(Debug)]
pub struct CaptureSnapshot {
    descriptor: CaptureSnapshotDescriptor,
    frame: PremultipliedBgraFrame,
}

impl CaptureSnapshot {
    #[must_use]
    pub fn new(descriptor: CaptureSnapshotDescriptor, frame: PremultipliedBgraFrame) -> Self {
        Self { descriptor, frame }
    }

    #[must_use]
    pub fn descriptor(&self) -> &CaptureSnapshotDescriptor {
        &self.descriptor
    }

    #[must_use]
    pub fn frame(&self) -> &PremultipliedBgraFrame {
        &self.frame
    }

    #[must_use]
    pub fn into_parts(self) -> (CaptureSnapshotDescriptor, PremultipliedBgraFrame) {
        (self.descriptor, self.frame)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PlatformError {
    InvalidCaptureDescriptor,
    CaptureFrameExceedsLimit,
    CaptureFrameLengthMismatch,
    NoScriptedCaptureSnapshot,
    PlatformUnavailable,
    DisplayUnavailable,
    CaptureUnavailable,
    PermissionNotGranted,
    PermissionRevoked,
    PermissionSettingsUnavailable,
    FrameTimeout,
    PixelConversionFailed,
    ClipboardBusy,
    ClipboardWriteFailed,
    SaveDialogFailed,
    FileWriteFailed,
    OverlayFailed,
    SessionMismatch,
    ShortcutConflict,
    ShortcutFailed,
    ShortcutResumeFailed,
    AutoStartFailed,
    AutoStartNeedsApproval,
    NotificationFailed,
    InternalState,
}

impl fmt::Display for PlatformError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(match self {
            Self::InvalidCaptureDescriptor => "invalid capture descriptor",
            Self::CaptureFrameExceedsLimit => "capture frame exceeds limit",
            Self::CaptureFrameLengthMismatch => "capture frame length mismatch",
            Self::NoScriptedCaptureSnapshot => "no scripted capture snapshot",
            Self::PlatformUnavailable => "platform capture unavailable",
            Self::DisplayUnavailable => "capture display unavailable",
            Self::CaptureUnavailable => "native capture unavailable",
            Self::PermissionNotGranted => "screen capture permission not granted",
            Self::PermissionRevoked => "screen capture permission revoked",
            Self::PermissionSettingsUnavailable => "screen capture permission settings unavailable",
            Self::FrameTimeout => "capture frame timed out",
            Self::PixelConversionFailed => "capture pixel conversion failed",
            Self::ClipboardBusy => "clipboard is busy",
            Self::ClipboardWriteFailed => "clipboard write failed",
            Self::SaveDialogFailed => "save dialog failed",
            Self::FileWriteFailed => "PNG file write failed",
            Self::OverlayFailed => "capture overlay unavailable",
            Self::SessionMismatch => "capture session mismatch",
            Self::ShortcutConflict => "shortcut is already registered",
            Self::ShortcutFailed => "shortcut operation failed",
            Self::ShortcutResumeFailed => "shortcut resume registration failed",
            Self::AutoStartFailed => "autostart operation failed",
            Self::AutoStartNeedsApproval => "autostart requires user approval",
            Self::NotificationFailed => "notification operation failed",
            Self::InternalState => "platform adapter internal state unavailable",
        })
    }
}

impl Error for PlatformError {}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SaveDisposition {
    Saved,
    Cancelled,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CapturePermissionState {
    Granted,
    NotGranted,
    RestartRequired,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PlatformEvent {
    Resumed,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PlatformNotification {
    ShortcutConflict,
    ShortcutResumeFailed,
    AutoStartFailed,
}

pub trait PlatformLease: Send + Sync + fmt::Debug {}

impl<T> PlatformLease for T where T: Send + Sync + fmt::Debug {}

pub trait PlatformAdapter: Send + Sync {
    type PendingSave: Send;
    type ResumeLease: PlatformLease;

    fn platform_name(&self) -> &'static str;

    fn capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        Err(PlatformError::PlatformUnavailable)
    }

    /// Must only be invoked in response to an explicit user action.
    fn request_capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        Err(PlatformError::PlatformUnavailable)
    }

    fn open_capture_permission_settings(&self) -> Result<(), PlatformError> {
        Err(PlatformError::PermissionSettingsUnavailable)
    }

    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError>;

    fn show_overlay(&self, descriptor: &CaptureSnapshotDescriptor) -> Result<(), PlatformError>;

    fn hide_overlay(&self, session_id: &str) -> Result<(), PlatformError>;

    fn finish_session(&self, session_id: &str) -> Result<(), PlatformError>;

    fn write_png(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError>;

    fn choose_png_destination(
        &self,
        session_id: &str,
        suggested_name: &str,
    ) -> Result<Option<Self::PendingSave>, PlatformError>;

    fn commit_png_save(
        &self,
        session_id: &str,
        pending: Self::PendingSave,
        png: &[u8],
    ) -> Result<SaveDisposition, PlatformError>;

    fn replace_shortcut(&self, accelerator: &str) -> Result<(), PlatformError>;

    fn watch_resume(
        &self,
        sink: Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>,
    ) -> Result<Self::ResumeLease, PlatformError>;

    fn autostart_enabled(&self) -> Result<bool, PlatformError>;

    fn set_autostart(&self, enabled: bool) -> Result<(), PlatformError>;

    fn notify(&self, notification: PlatformNotification) -> Result<(), PlatformError>;
}
