use std::{collections::VecDeque, sync::Mutex};

use snaploom_platform_contract::{CaptureSnapshot, PlatformAdapter, PlatformError};

#[derive(Debug, Default)]
pub struct FakePlatform {
    captures: Mutex<VecDeque<CaptureSnapshot>>,
}

impl FakePlatform {
    #[must_use]
    pub fn scripted(captures: impl IntoIterator<Item = CaptureSnapshot>) -> Self {
        Self {
            captures: Mutex::new(captures.into_iter().collect()),
        }
    }

    pub fn push_capture(&self, capture: CaptureSnapshot) -> Result<(), PlatformError> {
        self.captures
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .push_back(capture);
        Ok(())
    }
}

impl PlatformAdapter for FakePlatform {
    fn platform_name(&self) -> &'static str {
        "fake"
    }

    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        self.captures
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .pop_front()
            .ok_or(PlatformError::NoScriptedCaptureSnapshot)
    }
}
