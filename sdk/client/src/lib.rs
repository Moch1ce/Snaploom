#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ProtocolVersion {
    pub major: u16,
    pub minor: u16,
}

#[must_use]
pub const fn supported_protocol() -> ProtocolVersion {
    ProtocolVersion {
        major: snaploom_capture_protocol::PROTOCOL_MAJOR,
        minor: snaploom_capture_protocol::PROTOCOL_MINOR,
    }
}
