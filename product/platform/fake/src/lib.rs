use std::collections::{BTreeMap, VecDeque};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use snaploom_platform_contract::{
    CapturePermissionState, CaptureSnapshot, CaptureSnapshotDescriptor, PlatformAdapter,
    PlatformError, PlatformEvent, PlatformNotification, SaveDisposition,
};

type ResumeSink = Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>;

#[derive(Debug)]
pub struct FakePendingSave {
    session_id: String,
}

pub struct FakeResumeLease {
    id: u64,
    sinks: Arc<Mutex<BTreeMap<u64, ResumeSink>>>,
}

impl std::fmt::Debug for FakeResumeLease {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("FakeResumeLease")
            .field("id", &self.id)
            .finish_non_exhaustive()
    }
}

impl Drop for FakeResumeLease {
    fn drop(&mut self) {
        if let Ok(mut sinks) = self.sinks.lock() {
            sinks.remove(&self.id);
        }
    }
}

#[derive(Default)]
pub struct FakePlatform {
    captures: Mutex<VecDeque<CaptureSnapshot>>,
    active_session: Mutex<Option<String>>,
    overlay_session: Mutex<Option<String>>,
    clipboard_png: Mutex<Option<Vec<u8>>>,
    saved_png: Mutex<Option<Vec<u8>>>,
    shortcut: Mutex<Option<String>>,
    autostart: Mutex<bool>,
    notifications: Mutex<Vec<PlatformNotification>>,
    resume_sinks: Arc<Mutex<BTreeMap<u64, ResumeSink>>>,
    next_resume_id: AtomicU64,
}

impl std::fmt::Debug for FakePlatform {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("FakePlatform")
            .finish_non_exhaustive()
    }
}

impl FakePlatform {
    #[must_use]
    pub fn scripted(captures: impl IntoIterator<Item = CaptureSnapshot>) -> Self {
        Self {
            captures: Mutex::new(captures.into_iter().collect()),
            ..Self::default()
        }
    }

    pub fn push_capture(&self, capture: CaptureSnapshot) -> Result<(), PlatformError> {
        self.captures
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .push_back(capture);
        Ok(())
    }

    pub fn clipboard_png(&self) -> Result<Option<Vec<u8>>, PlatformError> {
        self.clipboard_png
            .lock()
            .map_err(|_| PlatformError::InternalState)
            .map(|png| png.clone())
    }

    pub fn saved_png(&self) -> Result<Option<Vec<u8>>, PlatformError> {
        self.saved_png
            .lock()
            .map_err(|_| PlatformError::InternalState)
            .map(|png| png.clone())
    }

    pub fn trigger_resume(&self) -> Result<(), PlatformError> {
        let sinks = self
            .resume_sinks
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .values()
            .cloned()
            .collect::<Vec<_>>();
        for sink in sinks {
            sink(PlatformEvent::Resumed);
        }
        Ok(())
    }

    fn validate_session(&self, session_id: &str) -> Result<(), PlatformError> {
        let active = self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        if active.as_deref() == Some(session_id) {
            Ok(())
        } else {
            Err(PlatformError::SessionMismatch)
        }
    }
}

impl PlatformAdapter for FakePlatform {
    type PendingSave = FakePendingSave;
    type ResumeLease = FakeResumeLease;

    fn platform_name(&self) -> &'static str {
        "fake"
    }

    fn capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        Ok(CapturePermissionState::Granted)
    }

    fn request_capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        Ok(CapturePermissionState::Granted)
    }

    fn open_capture_permission_settings(&self) -> Result<(), PlatformError> {
        Ok(())
    }

    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        let capture = self
            .captures
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .pop_front()
            .ok_or(PlatformError::NoScriptedCaptureSnapshot)?;
        *self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? =
            Some(capture.descriptor().session_id.clone());
        Ok(capture)
    }

    fn show_overlay(&self, descriptor: &CaptureSnapshotDescriptor) -> Result<(), PlatformError> {
        self.validate_session(&descriptor.session_id)?;
        *self
            .overlay_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? = Some(descriptor.session_id.clone());
        Ok(())
    }

    fn hide_overlay(&self, session_id: &str) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        *self
            .overlay_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? = None;
        Ok(())
    }

    fn finish_session(&self, session_id: &str) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        *self
            .overlay_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? = None;
        *self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? = None;
        Ok(())
    }

    fn write_png(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        *self
            .clipboard_png
            .lock()
            .map_err(|_| PlatformError::InternalState)? = Some(png.to_vec());
        Ok(())
    }

    fn choose_png_destination(
        &self,
        session_id: &str,
        _suggested_name: &str,
    ) -> Result<Option<Self::PendingSave>, PlatformError> {
        self.validate_session(session_id)?;
        Ok(Some(FakePendingSave {
            session_id: session_id.to_owned(),
        }))
    }

    fn commit_png_save(
        &self,
        session_id: &str,
        pending: Self::PendingSave,
        png: &[u8],
    ) -> Result<SaveDisposition, PlatformError> {
        self.validate_session(session_id)?;
        if pending.session_id != session_id {
            return Err(PlatformError::SessionMismatch);
        }
        *self
            .saved_png
            .lock()
            .map_err(|_| PlatformError::InternalState)? = Some(png.to_vec());
        Ok(SaveDisposition::Saved)
    }

    fn replace_shortcut(&self, accelerator: &str) -> Result<(), PlatformError> {
        if accelerator.trim().is_empty() {
            return Err(PlatformError::ShortcutFailed);
        }
        *self
            .shortcut
            .lock()
            .map_err(|_| PlatformError::InternalState)? = Some(accelerator.to_owned());
        Ok(())
    }

    fn watch_resume(&self, sink: ResumeSink) -> Result<Self::ResumeLease, PlatformError> {
        let id = self.next_resume_id.fetch_add(1, Ordering::Relaxed) + 1;
        self.resume_sinks
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .insert(id, sink);
        Ok(FakeResumeLease {
            id,
            sinks: self.resume_sinks.clone(),
        })
    }

    fn autostart_enabled(&self) -> Result<bool, PlatformError> {
        self.autostart
            .lock()
            .map_err(|_| PlatformError::InternalState)
            .map(|enabled| *enabled)
    }

    fn set_autostart(&self, enabled: bool) -> Result<(), PlatformError> {
        *self
            .autostart
            .lock()
            .map_err(|_| PlatformError::InternalState)? = enabled;
        Ok(())
    }

    fn notify(&self, notification: PlatformNotification) -> Result<(), PlatformError> {
        self.notifications
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .push(notification);
        Ok(())
    }
}
