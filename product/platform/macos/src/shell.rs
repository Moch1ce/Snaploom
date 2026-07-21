use std::fs::OpenOptions;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

#[cfg(feature = "desktop-shell")]
use snaploom_platform_contract::{
    CapturePermissionState, PlatformAdapter, PlatformEvent, PlatformNotification, SaveDisposition,
};
use snaploom_platform_contract::{CaptureSnapshot, CaptureSnapshotDescriptor, PlatformError};
use tauri::{AppHandle, Manager, Runtime, WebviewWindow};
#[cfg(not(target_os = "macos"))]
use tauri::{PhysicalPosition, PhysicalSize};
use tauri_plugin_dialog::DialogExt;

use crate::MacPlatform;

const OVERLAY_LABEL: &str = "overlay";
const MAX_PNG_BYTES: usize = 100 * 1024 * 1024;
const PNG_SIGNATURE: &[u8; 8] = b"\x89PNG\r\n\x1a\n";
static NEXT_TEMP_FILE: AtomicU64 = AtomicU64::new(1);

#[derive(Debug, Clone, Copy)]
struct OverlayPlacement {
    display_id: u32,
    width: u32,
    height: u32,
}

pub struct PendingPngSave {
    session_id: String,
    destination: PathBuf,
}

impl std::fmt::Debug for PendingPngSave {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("PendingPngSave")
            .finish_non_exhaustive()
    }
}

#[cfg(feature = "desktop-shell")]
pub struct ResumeLease {
    #[cfg(target_os = "macos")]
    _native: crate::sys::ResumeLease,
}

#[cfg(feature = "desktop-shell")]
impl std::fmt::Debug for ResumeLease {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("ResumeLease")
            .finish_non_exhaustive()
    }
}

pub struct MacShell<R: Runtime> {
    app: AppHandle<R>,
    active_session: Arc<Mutex<Option<String>>>,
    placement: Arc<Mutex<Option<OverlayPlacement>>>,
    recent_directory: Arc<Mutex<Option<PathBuf>>>,
    #[cfg(feature = "desktop-shell")]
    shortcut: Arc<Mutex<Option<String>>>,
}

impl<R: Runtime> Clone for MacShell<R> {
    fn clone(&self) -> Self {
        Self {
            app: self.app.clone(),
            active_session: self.active_session.clone(),
            placement: self.placement.clone(),
            recent_directory: self.recent_directory.clone(),
            #[cfg(feature = "desktop-shell")]
            shortcut: self.shortcut.clone(),
        }
    }
}

impl<R: Runtime> MacShell<R> {
    #[must_use]
    pub fn new(app: AppHandle<R>) -> Self {
        Self {
            app,
            active_session: Arc::new(Mutex::new(None)),
            placement: Arc::new(Mutex::new(None)),
            recent_directory: Arc::new(Mutex::new(None)),
            #[cfg(feature = "desktop-shell")]
            shortcut: Arc::new(Mutex::new(None)),
        }
    }

    pub fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        if self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .is_some()
        {
            return Err(PlatformError::InternalState);
        }
        let snapshot = MacPlatform::new().capture_snapshot()?;
        let display_id = snapshot
            .descriptor()
            .display_id
            .and_then(|value| u32::try_from(value).ok())
            .ok_or(PlatformError::DisplayUnavailable)?;
        let placement = OverlayPlacement {
            display_id,
            width: snapshot.descriptor().physical_size.width,
            height: snapshot.descriptor().physical_size.height,
        };
        let work_area = self.prepare_overlay(placement)?;
        let (mut descriptor, frame) = snapshot.into_parts();
        descriptor.work_area_logical = work_area;
        let frame = snaploom_platform_contract::PremultipliedBgraFrame::new(
            &descriptor,
            frame.into_bytes(),
        )?;
        let snapshot = CaptureSnapshot::new(descriptor, frame);
        *self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)? =
            Some(snapshot.descriptor().session_id.clone());
        *self
            .placement
            .lock()
            .map_err(|_| PlatformError::InternalState)? = Some(placement);
        Ok(snapshot)
    }

    pub fn write_png_clipboard(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        validate_png(png)?;
        #[cfg(target_os = "macos")]
        {
            crate::sys::write_png(png)
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::PlatformUnavailable)
        }
    }

    pub fn hide_overlay(&self, session_id: &str) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        self.overlay()?
            .hide()
            .map_err(|_| PlatformError::OverlayFailed)
    }

    pub fn show_overlay_for(
        &self,
        descriptor: &CaptureSnapshotDescriptor,
    ) -> Result<(), PlatformError> {
        self.validate_session(&descriptor.session_id)?;
        let placement = self
            .placement
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .ok_or(PlatformError::DisplayUnavailable)?;
        if placement.width != descriptor.physical_size.width
            || placement.height != descriptor.physical_size.height
            || descriptor.display_id != Some(u64::from(placement.display_id))
        {
            return Err(PlatformError::DisplayUnavailable);
        }
        let _ = self.prepare_overlay(placement)?;
        self.overlay()?
            .show()
            .map_err(|_| PlatformError::OverlayFailed)
    }

    pub fn reload_overlay(&self) -> Result<(), PlatformError> {
        self.overlay()?
            .eval("window.location.reload()")
            .map_err(|_| PlatformError::OverlayFailed)
    }

    pub fn choose_png_destination(
        &self,
        session_id: &str,
        suggested_name: &str,
        recent_directory: Option<&Path>,
    ) -> Result<Option<PendingPngSave>, PlatformError> {
        self.validate_session(session_id)?;
        self.hide_overlay(session_id)?;
        self.activate_for_dialog()?;
        let starting_directory = recent_directory.map(Path::to_path_buf).or_else(|| {
            self.recent_directory
                .lock()
                .ok()
                .and_then(|directory| directory.clone())
        });
        let mut dialog = self
            .app
            .dialog()
            .file()
            .add_filter("PNG image", &["png"])
            .set_file_name(safe_suggested_name(suggested_name));
        if let Some(directory) = starting_directory.filter(|path| path.is_absolute()) {
            dialog = dialog.set_directory(directory);
        }
        let destination = dialog
            .blocking_save_file()
            .and_then(|path| path.into_path().ok());
        let Some(mut destination) = destination else {
            self.restore_overlay_after_dialog(session_id)?;
            return Ok(None);
        };
        if !destination
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("png"))
        {
            destination.set_extension("png");
        }
        Ok(Some(PendingPngSave {
            session_id: session_id.to_owned(),
            destination,
        }))
    }

    pub fn commit_png_save(
        &self,
        session_id: &str,
        pending: PendingPngSave,
        png: &[u8],
    ) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        if pending.session_id != session_id {
            return Err(PlatformError::SessionMismatch);
        }
        validate_png(png)?;
        if let Err(error) = atomic_save(&pending.destination, png) {
            let _ = self.restore_overlay_after_dialog(session_id);
            return Err(error);
        }
        *self
            .recent_directory
            .lock()
            .map_err(|_| PlatformError::InternalState)? =
            pending.destination.parent().map(Path::to_path_buf);
        Ok(())
    }

    pub fn finish_session(&self, session_id: &str) -> Result<(), PlatformError> {
        let mut active = self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        if active.as_deref() != Some(session_id) {
            return Err(PlatformError::SessionMismatch);
        }
        *active = None;
        *self
            .placement
            .lock()
            .map_err(|_| PlatformError::InternalState)? = None;
        Ok(())
    }

    fn validate_session(&self, session_id: &str) -> Result<(), PlatformError> {
        if self
            .active_session
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .as_deref()
            == Some(session_id)
        {
            Ok(())
        } else {
            Err(PlatformError::SessionMismatch)
        }
    }

    fn restore_overlay_after_dialog(&self, session_id: &str) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        self.overlay()?
            .show()
            .map_err(|_| PlatformError::OverlayFailed)
    }

    fn overlay(&self) -> Result<WebviewWindow<R>, PlatformError> {
        self.app
            .get_webview_window(OVERLAY_LABEL)
            .ok_or(PlatformError::OverlayFailed)
    }

    fn prepare_overlay(
        &self,
        placement: OverlayPlacement,
    ) -> Result<snaploom_platform_contract::LogicalRect, PlatformError> {
        let overlay = self.overlay()?;
        overlay.hide().map_err(|_| PlatformError::OverlayFailed)?;
        #[cfg(not(target_os = "macos"))]
        overlay
            .set_position(PhysicalPosition::new(0, 0))
            .map_err(|_| PlatformError::OverlayFailed)?;
        #[cfg(not(target_os = "macos"))]
        overlay
            .set_size(PhysicalSize::new(placement.width, placement.height))
            .map_err(|_| PlatformError::OverlayFailed)?;
        overlay
            .set_always_on_top(true)
            .map_err(|_| PlatformError::OverlayFailed)?;
        #[cfg(target_os = "macos")]
        {
            let ns_window = overlay
                .ns_window()
                .map_err(|_| PlatformError::OverlayFailed)? as usize;
            if crate::sys::is_main_thread() {
                return unsafe {
                    crate::sys::configure_overlay(
                        ns_window as *mut _,
                        placement.display_id,
                        snaploom_platform_contract::PhysicalSize {
                            width: placement.width,
                            height: placement.height,
                        },
                    )
                };
            }
            let (sender, receiver) = std::sync::mpsc::sync_channel(1);
            self.app
                .run_on_main_thread(move || {
                    let result = unsafe {
                        crate::sys::configure_overlay(
                            ns_window as *mut _,
                            placement.display_id,
                            snaploom_platform_contract::PhysicalSize {
                                width: placement.width,
                                height: placement.height,
                            },
                        )
                    };
                    let _ = sender.send(result);
                })
                .map_err(|_| PlatformError::OverlayFailed)?;
            receiver.recv().map_err(|_| PlatformError::OverlayFailed)?
        }
        #[cfg(not(target_os = "macos"))]
        Ok(snaploom_platform_contract::LogicalRect {
            x: 0.0,
            y: 0.0,
            width: f64::from(placement.width),
            height: f64::from(placement.height),
        })
    }

    fn activate_for_dialog(&self) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            if crate::sys::is_main_thread() {
                return crate::sys::activate_accessory_app();
            }
            let (sender, receiver) = std::sync::mpsc::sync_channel(1);
            self.app
                .run_on_main_thread(move || {
                    let _ = sender.send(crate::sys::activate_accessory_app());
                })
                .map_err(|_| PlatformError::SaveDialogFailed)?;
            receiver
                .recv()
                .map_err(|_| PlatformError::SaveDialogFailed)?
        }
        #[cfg(not(target_os = "macos"))]
        Ok(())
    }
}

#[cfg(feature = "desktop-shell")]
impl<R: Runtime> MacShell<R> {
    fn replace_shortcut_registration(&self, accelerator: &str) -> Result<(), PlatformError> {
        use tauri_plugin_global_shortcut::GlobalShortcutExt;

        let candidate = validate_shortcut(accelerator)?;
        let mut current = self
            .shortcut
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        if current.as_deref() == Some(candidate.as_str()) {
            return Ok(());
        }
        self.app
            .global_shortcut()
            .register(candidate.as_str())
            .map_err(map_shortcut_error)?;
        if let Some(previous) = current.as_deref()
            && self.app.global_shortcut().unregister(previous).is_err()
        {
            let _ = self.app.global_shortcut().unregister(candidate.as_str());
            return Err(PlatformError::ShortcutFailed);
        }
        *current = Some(candidate);
        Ok(())
    }

    fn watch_platform_resume(
        &self,
        sink: Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>,
    ) -> Result<ResumeLease, PlatformError> {
        if self
            .shortcut
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .is_none()
        {
            return Err(PlatformError::ShortcutFailed);
        }
        #[cfg(target_os = "macos")]
        {
            let shell = self.clone();
            let native = crate::sys::watch_resume(Arc::new(move || {
                use tauri_plugin_global_shortcut::GlobalShortcutExt;
                let restored = shell
                    .shortcut
                    .lock()
                    .map_err(|_| PlatformError::InternalState)
                    .and_then(|current| {
                        let current = current.as_deref().ok_or(PlatformError::ShortcutFailed)?;
                        if shell.app.global_shortcut().is_registered(current) {
                            Ok(())
                        } else {
                            shell
                                .app
                                .global_shortcut()
                                .register(current)
                                .map_err(map_shortcut_error)
                        }
                    });
                if restored.is_err() {
                    let _ = shell.notify_stable(PlatformNotification::ShortcutResumeFailed);
                }
                sink(PlatformEvent::Resumed);
            }));
            Ok(ResumeLease { _native: native })
        }
        #[cfg(not(target_os = "macos"))]
        {
            let _ = sink;
            Err(PlatformError::PlatformUnavailable)
        }
    }

    fn autostart_enabled_native(&self) -> Result<bool, PlatformError> {
        #[cfg(target_os = "macos")]
        {
            match crate::sys::autostart_state() {
                crate::sys::AutoStartState::Enabled => Ok(true),
                crate::sys::AutoStartState::Disabled => Ok(false),
                crate::sys::AutoStartState::RequiresApproval => {
                    Err(PlatformError::AutoStartNeedsApproval)
                }
            }
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(PlatformError::PlatformUnavailable)
        }
    }

    fn set_autostart_native(&self, enabled: bool) -> Result<(), PlatformError> {
        #[cfg(target_os = "macos")]
        {
            crate::sys::set_autostart(enabled)
        }
        #[cfg(not(target_os = "macos"))]
        {
            let _ = enabled;
            Err(PlatformError::PlatformUnavailable)
        }
    }

    fn notify_stable(&self, notification: PlatformNotification) -> Result<(), PlatformError> {
        use tauri_plugin_notification::NotificationExt;

        let body = match notification {
            PlatformNotification::ShortcutConflict => "快捷键已被其他应用占用，原快捷键保持不变。",
            PlatformNotification::ShortcutResumeFailed => {
                "系统唤醒后未能恢复截图快捷键，请在设置中重新选择。"
            }
            PlatformNotification::AutoStartFailed => "开机启动设置未能更新，请在系统登录项中检查。",
        };
        self.app
            .notification()
            .builder()
            .title("Snaploom")
            .body(body)
            .show()
            .map_err(|_| PlatformError::NotificationFailed)
    }
}

#[cfg(feature = "desktop-shell")]
impl<R: Runtime> PlatformAdapter for MacShell<R> {
    type PendingSave = PendingPngSave;
    type ResumeLease = ResumeLease;

    fn platform_name(&self) -> &'static str {
        "macos"
    }
    fn capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        Ok(if MacPlatform::new().permission_granted() {
            CapturePermissionState::Granted
        } else {
            CapturePermissionState::NotGranted
        })
    }
    fn request_capture_permission(&self) -> Result<CapturePermissionState, PlatformError> {
        MacPlatform::new().request_permission()
    }
    fn open_capture_permission_settings(&self) -> Result<(), PlatformError> {
        MacPlatform::new().open_permission_settings()
    }
    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        MacShell::capture_snapshot(self)
    }
    fn show_overlay(&self, descriptor: &CaptureSnapshotDescriptor) -> Result<(), PlatformError> {
        self.show_overlay_for(descriptor)
    }
    fn hide_overlay(&self, session_id: &str) -> Result<(), PlatformError> {
        MacShell::hide_overlay(self, session_id)
    }
    fn finish_session(&self, session_id: &str) -> Result<(), PlatformError> {
        MacShell::finish_session(self, session_id)
    }
    fn write_png(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError> {
        self.write_png_clipboard(session_id, png)
    }
    fn choose_png_destination(
        &self,
        session_id: &str,
        suggested_name: &str,
    ) -> Result<Option<Self::PendingSave>, PlatformError> {
        MacShell::choose_png_destination(self, session_id, suggested_name, None)
    }
    fn commit_png_save(
        &self,
        session_id: &str,
        pending: Self::PendingSave,
        png: &[u8],
    ) -> Result<SaveDisposition, PlatformError> {
        MacShell::commit_png_save(self, session_id, pending, png)?;
        Ok(SaveDisposition::Saved)
    }
    fn replace_shortcut(&self, accelerator: &str) -> Result<(), PlatformError> {
        let result = self.replace_shortcut_registration(accelerator);
        if result == Err(PlatformError::ShortcutConflict) {
            let _ = self.notify_stable(PlatformNotification::ShortcutConflict);
        }
        result
    }
    fn watch_resume(
        &self,
        sink: Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>,
    ) -> Result<Self::ResumeLease, PlatformError> {
        self.watch_platform_resume(sink)
    }
    fn autostart_enabled(&self) -> Result<bool, PlatformError> {
        self.autostart_enabled_native()
    }
    fn set_autostart(&self, enabled: bool) -> Result<(), PlatformError> {
        let result = self.set_autostart_native(enabled);
        if result.is_err() {
            let _ = self.notify_stable(PlatformNotification::AutoStartFailed);
        }
        result
    }
    fn notify(&self, notification: PlatformNotification) -> Result<(), PlatformError> {
        self.notify_stable(notification)
    }
}

#[cfg(feature = "desktop-shell")]
fn validate_shortcut(value: &str) -> Result<String, PlatformError> {
    let parts: Vec<_> = value
        .split('+')
        .map(str::trim)
        .filter(|part| !part.is_empty())
        .collect();
    let modifiers = parts
        .iter()
        .filter(|part| {
            matches!(
                part.to_ascii_lowercase().as_str(),
                "cmd" | "command" | "ctrl" | "control" | "alt" | "option" | "shift" | "super"
            )
        })
        .count();
    if modifiers == 0 || parts.len() != modifiers + 1 {
        Err(PlatformError::ShortcutFailed)
    } else {
        Ok(parts.join("+"))
    }
}

#[cfg(feature = "desktop-shell")]
fn map_shortcut_error(error: tauri_plugin_global_shortcut::Error) -> PlatformError {
    match error {
        tauri_plugin_global_shortcut::Error::GlobalHotkey(message)
            if message.starts_with("HotKey already registered:") =>
        {
            PlatformError::ShortcutConflict
        }
        _ => PlatformError::ShortcutFailed,
    }
}

fn validate_png(png: &[u8]) -> Result<(), PlatformError> {
    if png.len() > MAX_PNG_BYTES || !png.starts_with(PNG_SIGNATURE) {
        Err(PlatformError::ClipboardWriteFailed)
    } else {
        Ok(())
    }
}

fn safe_suggested_name(name: &str) -> String {
    let candidate = Path::new(name);
    if candidate.file_name().and_then(|value| value.to_str()) == Some(name)
        && candidate
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("png"))
    {
        name.to_owned()
    } else {
        "Snaploom.png".to_owned()
    }
}

fn atomic_save(destination: &Path, png: &[u8]) -> Result<(), PlatformError> {
    let parent = destination
        .parent()
        .filter(|path| !path.as_os_str().is_empty())
        .ok_or(PlatformError::FileWriteFailed)?;
    let file_name = destination
        .file_name()
        .and_then(|value| value.to_str())
        .ok_or(PlatformError::FileWriteFailed)?;
    let mut temporary = None;
    for _ in 0..16 {
        let suffix = NEXT_TEMP_FILE.fetch_add(1, Ordering::Relaxed);
        let candidate = parent.join(format!(".{file_name}.{suffix:016x}.tmp"));
        match OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&candidate)
        {
            Ok(file) => {
                temporary = Some((candidate, file));
                break;
            }
            Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {}
            Err(_) => return Err(PlatformError::FileWriteFailed),
        }
    }
    let (temporary_path, mut file) = temporary.ok_or(PlatformError::FileWriteFailed)?;
    if file.write_all(png).and_then(|()| file.sync_all()).is_err() {
        drop(file);
        let _ = std::fs::remove_file(&temporary_path);
        return Err(PlatformError::FileWriteFailed);
    }
    drop(file);
    let replaced =
        std::fs::rename(&temporary_path, destination).map_err(|_| PlatformError::FileWriteFailed);
    if replaced.is_err() {
        let _ = std::fs::remove_file(&temporary_path);
    }
    replaced
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_safe_png_names_and_payloads() {
        assert_eq!(
            safe_suggested_name("Snaploom_2026.png"),
            "Snaploom_2026.png"
        );
        assert_eq!(safe_suggested_name("../secret.png"), "Snaploom.png");
        assert_eq!(
            validate_png(b"no"),
            Err(PlatformError::ClipboardWriteFailed)
        );
    }

    #[cfg(feature = "desktop-shell")]
    #[test]
    fn command_shortcut_requires_one_key_and_at_least_one_modifier() {
        assert!(validate_shortcut("Cmd+Shift+A").is_ok());
        assert_eq!(validate_shortcut("A"), Err(PlatformError::ShortcutFailed));
        assert_eq!(
            validate_shortcut("Cmd+A+B"),
            Err(PlatformError::ShortcutFailed)
        );
    }

    #[cfg(feature = "desktop-shell")]
    #[test]
    fn exposes_complete_platform_adapter() {
        fn assert_adapter<T: PlatformAdapter>() {}
        assert_adapter::<MacShell<tauri::Wry>>();
    }
}
