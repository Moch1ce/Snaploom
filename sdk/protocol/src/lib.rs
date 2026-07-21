use std::fmt;

use zeroize::Zeroize;

pub const PROTOCOL_MAJOR: u16 = 1;
pub const PROTOCOL_MINOR: u16 = 0;
pub const FRAME_HEADER_BYTES: usize = 12;
pub const MAX_FRAME_BYTES: usize = 1_048_576;
pub const MAX_RESULT_CHUNK_BYTES: usize = 1_048_000;
pub const MAX_PNG_BYTES: u64 = 134_217_728;
pub const MAX_PIXEL_DIMENSION: u32 = 32_768;

mod generated {
    include!(concat!(env!("OUT_DIR"), "/snaploom.capture.v1.rs"));
}

pub use generated::*;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u16)]
pub enum FrameType {
    Hello = 1,
    Welcome = 2,
    StartCapture = 3,
    CaptureAccepted = 4,
    CaptureRejected = 5,
    CancelCapture = 6,
    CancelAcknowledged = 7,
    ResultBegin = 8,
    ResultChunk = 9,
    ResultEnd = 10,
    CaptureCanceled = 11,
    CaptureFailed = 12,
}

impl TryFrom<u16> for FrameType {
    type Error = WireError;

    fn try_from(value: u16) -> Result<Self, Self::Error> {
        match value {
            1 => Ok(Self::Hello),
            2 => Ok(Self::Welcome),
            3 => Ok(Self::StartCapture),
            4 => Ok(Self::CaptureAccepted),
            5 => Ok(Self::CaptureRejected),
            6 => Ok(Self::CancelCapture),
            7 => Ok(Self::CancelAcknowledged),
            8 => Ok(Self::ResultBegin),
            9 => Ok(Self::ResultChunk),
            10 => Ok(Self::ResultEnd),
            11 => Ok(Self::CaptureCanceled),
            12 => Ok(Self::CaptureFailed),
            _ => Err(WireError::UnknownFrameType),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Frame {
    pub frame_type: FrameType,
    pub payload: Vec<u8>,
}

impl Frame {
    pub fn new(frame_type: FrameType, payload: Vec<u8>) -> Result<Self, WireError> {
        if payload.len() > MAX_FRAME_BYTES {
            return Err(WireError::FrameTooLarge);
        }
        Ok(Self {
            frame_type,
            payload,
        })
    }

    #[must_use]
    pub fn encode(&self) -> Vec<u8> {
        let mut bytes = Vec::with_capacity(FRAME_HEADER_BYTES + self.payload.len());
        bytes.extend_from_slice(b"SLCP");
        bytes.extend_from_slice(&1_u16.to_le_bytes());
        bytes.extend_from_slice(&(self.frame_type as u16).to_le_bytes());
        bytes.extend_from_slice(&(self.payload.len() as u32).to_le_bytes());
        bytes.extend_from_slice(&self.payload);
        bytes
    }
}

#[derive(Debug, Default)]
pub struct FrameDecoder {
    buffer: Vec<u8>,
}

impl FrameDecoder {
    pub fn push(&mut self, bytes: &[u8]) -> Result<Vec<Frame>, WireError> {
        let mut frames = Vec::new();
        let mut remaining = bytes;
        while !remaining.is_empty() {
            if self.buffer.len() < FRAME_HEADER_BYTES {
                let count = (FRAME_HEADER_BYTES - self.buffer.len()).min(remaining.len());
                self.buffer
                    .try_reserve(count)
                    .map_err(|_| WireError::OutOfMemory)?;
                self.buffer.extend_from_slice(&remaining[..count]);
                remaining = &remaining[count..];
                if self.buffer.len() < FRAME_HEADER_BYTES {
                    continue;
                }
            }
            if &self.buffer[..4] != b"SLCP" {
                return Err(WireError::InvalidMagic);
            }
            let framing_version = u16::from_le_bytes([self.buffer[4], self.buffer[5]]);
            if framing_version != 1 {
                return Err(WireError::UnsupportedFramingVersion);
            }
            let frame_type =
                FrameType::try_from(u16::from_le_bytes([self.buffer[6], self.buffer[7]]))?;
            let payload_len = u32::from_le_bytes([
                self.buffer[8],
                self.buffer[9],
                self.buffer[10],
                self.buffer[11],
            ]) as usize;
            if payload_len > MAX_FRAME_BYTES {
                return Err(WireError::FrameTooLarge);
            }
            let frame_len = FRAME_HEADER_BYTES + payload_len;
            if self.buffer.len() < frame_len {
                let count = (frame_len - self.buffer.len()).min(remaining.len());
                self.buffer
                    .try_reserve(count)
                    .map_err(|_| WireError::OutOfMemory)?;
                self.buffer.extend_from_slice(&remaining[..count]);
                remaining = &remaining[count..];
            }
            if self.buffer.len() < frame_len {
                continue;
            }
            let payload = self.buffer[FRAME_HEADER_BYTES..frame_len].to_vec();
            self.buffer.clear();
            frames.push(Frame {
                frame_type,
                payload,
            });
        }
        Ok(frames)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ProtocolRange {
    pub major: u16,
    pub min_minor: u16,
    pub max_minor: u16,
}

impl ProtocolRange {
    pub fn new(major: u16, min_minor: u16, max_minor: u16) -> Result<Self, WireError> {
        if major == 0 || min_minor > max_minor {
            return Err(WireError::ProtocolIncompatible);
        }
        Ok(Self {
            major,
            min_minor,
            max_minor,
        })
    }
}

pub fn negotiate_version(
    client: ProtocolRange,
    host: ProtocolRange,
    required_capabilities: u64,
    host_capabilities: u64,
) -> Result<u16, WireError> {
    if client.major != host.major
        || required_capabilities & !host_capabilities != 0
        || client.min_minor > host.max_minor
        || host.min_minor > client.max_minor
    {
        return Err(WireError::ProtocolIncompatible);
    }
    Ok(client.max_minor.min(host.max_minor))
}

#[derive(Debug)]
pub struct SensitivePng {
    bytes: Vec<u8>,
    pixel_width: u32,
    pixel_height: u32,
}

impl SensitivePng {
    #[must_use]
    pub fn as_slice(&self) -> &[u8] {
        &self.bytes
    }

    #[must_use]
    pub const fn pixel_width(&self) -> u32 {
        self.pixel_width
    }

    #[must_use]
    pub const fn pixel_height(&self) -> u32 {
        self.pixel_height
    }

    #[must_use]
    pub fn into_vec(mut self) -> Vec<u8> {
        std::mem::take(&mut self.bytes)
    }

    pub fn zeroize(&mut self) {
        self.bytes.zeroize();
    }
}

impl Drop for SensitivePng {
    fn drop(&mut self) {
        self.bytes.zeroize();
    }
}

#[derive(Debug)]
pub struct PngAccumulator {
    expected_len: usize,
    pixel_width: u32,
    pixel_height: u32,
    bytes: Vec<u8>,
}

impl PngAccumulator {
    pub fn begin(
        expected_len: u64,
        pixel_width: u32,
        pixel_height: u32,
    ) -> Result<Self, WireError> {
        if expected_len == 0 || expected_len > MAX_PNG_BYTES {
            return Err(WireError::PngTooLarge);
        }
        if pixel_width == 0
            || pixel_height == 0
            || pixel_width > MAX_PIXEL_DIMENSION
            || pixel_height > MAX_PIXEL_DIMENSION
        {
            return Err(WireError::InvalidDimensions);
        }
        let expected_len = usize::try_from(expected_len).map_err(|_| WireError::PngTooLarge)?;
        Ok(Self {
            expected_len,
            pixel_width,
            pixel_height,
            bytes: Vec::new(),
        })
    }

    pub fn push(&mut self, offset: u64, data: &[u8]) -> Result<(), WireError> {
        if data.is_empty() || data.len() > MAX_RESULT_CHUNK_BYTES {
            return Err(WireError::InvalidChunk);
        }
        if offset != self.bytes.len() as u64 {
            return Err(WireError::NonContiguousChunk);
        }
        let next_len = self
            .bytes
            .len()
            .checked_add(data.len())
            .ok_or(WireError::PngTooLarge)?;
        if next_len > self.expected_len {
            return Err(WireError::PngTooLarge);
        }
        self.bytes
            .try_reserve(data.len())
            .map_err(|_| WireError::OutOfMemory)?;
        self.bytes.extend_from_slice(data);
        Ok(())
    }

    pub fn finish(self) -> Result<SensitivePng, WireError> {
        if self.bytes.len() != self.expected_len {
            return Err(WireError::IncompletePng);
        }
        validate_png(&self.bytes, self.pixel_width, self.pixel_height)?;
        let mut this = std::mem::ManuallyDrop::new(self);
        let bytes = std::mem::take(&mut this.bytes);
        Ok(SensitivePng {
            bytes,
            pixel_width: this.pixel_width,
            pixel_height: this.pixel_height,
        })
    }
}

impl Drop for PngAccumulator {
    fn drop(&mut self) {
        self.bytes.zeroize();
    }
}

fn validate_png(bytes: &[u8], width: u32, height: u32) -> Result<(), WireError> {
    if bytes.len() < 33
        || &bytes[..8] != b"\x89PNG\r\n\x1a\n"
        || &bytes[12..16] != b"IHDR"
        || u32::from_be_bytes(bytes[8..12].try_into().expect("four bytes")) != 13
        || u32::from_be_bytes(bytes[16..20].try_into().expect("four bytes")) != width
        || u32::from_be_bytes(bytes[20..24].try_into().expect("four bytes")) != height
    {
        return Err(WireError::InvalidPng);
    }
    Ok(())
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WireError {
    InvalidMagic,
    UnsupportedFramingVersion,
    UnknownFrameType,
    FrameTooLarge,
    ProtocolIncompatible,
    InvalidDimensions,
    InvalidChunk,
    NonContiguousChunk,
    PngTooLarge,
    IncompletePng,
    InvalidPng,
    OutOfMemory,
}

impl fmt::Display for WireError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{self:?}")
    }
}

impl std::error::Error for WireError {}
