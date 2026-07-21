use std::{error::Error, fmt};

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
    pub physical_size: PhysicalSize,
    pub logical_size: LogicalSize,
    pub global_origin: PhysicalPoint,
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
        bytes: Vec<u8>,
    ) -> Result<Self, PlatformError> {
        let expected = descriptor.expected_frame_bytes()?;
        Self::validate_length(bytes.len(), expected)?;
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
    InternalState,
}

impl fmt::Display for PlatformError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(match self {
            Self::InvalidCaptureDescriptor => "invalid capture descriptor",
            Self::CaptureFrameExceedsLimit => "capture frame exceeds limit",
            Self::CaptureFrameLengthMismatch => "capture frame length mismatch",
            Self::NoScriptedCaptureSnapshot => "no scripted capture snapshot",
            Self::InternalState => "platform adapter internal state unavailable",
        })
    }
}

impl Error for PlatformError {}

pub trait PlatformAdapter: Send + Sync {
    fn platform_name(&self) -> &'static str;

    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError>;
}
