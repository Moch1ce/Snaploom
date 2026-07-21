#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CaptureSessionState {
    Idle,
}

#[derive(Debug, Default)]
pub struct CaptureSession;

impl CaptureSession {
    #[must_use]
    pub const fn state(&self) -> CaptureSessionState {
        CaptureSessionState::Idle
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn new_shell_is_idle() {
        assert_eq!(CaptureSession.state(), CaptureSessionState::Idle);
    }
}
