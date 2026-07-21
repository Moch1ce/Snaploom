use std::fs::OpenOptions;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use snaploom_platform_contract::{CaptureSnapshot, CaptureSnapshotDescriptor, PlatformError};
#[cfg(feature = "desktop-shell")]
use snaploom_platform_contract::{
    PlatformAdapter, PlatformEvent, PlatformNotification, SaveDisposition,
};
use tauri::{AppHandle, Manager, PhysicalPosition, PhysicalSize, Runtime, WebviewWindow};
#[cfg(not(windows))]
use tauri_plugin_dialog::DialogExt;

use crate::WindowsPlatform;
#[cfg(feature = "desktop-shell")]
use crate::{Shortcut, ShortcutError, ShortcutManager, ShortcutRegistrar};

const OVERLAY_LABEL: &str = "overlay";
const MAX_PNG_BYTES: usize = 100 * 1024 * 1024;
const PNG_SIGNATURE: &[u8; 8] = b"\x89PNG\r\n\x1a\n";
static NEXT_TEMP_FILE: AtomicU64 = AtomicU64::new(1);

/// Opaque destination selected by the native save dialog.
///
/// Keeping the path private prevents platform paths from escaping the Adapter
/// while still allowing the Session layer to revalidate its lease before the
/// first filesystem side effect.
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
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StableNotification {
    ShortcutConflict,
    ShortcutResumeFailed,
    AutoStartFailed,
}

#[cfg(feature = "desktop-shell")]
pub struct TauriShortcutRegistrar<R: Runtime> {
    app: AppHandle<R>,
}

#[cfg(feature = "desktop-shell")]
impl<R: Runtime> ShortcutRegistrar for TauriShortcutRegistrar<R> {
    fn register(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
        use tauri_plugin_global_shortcut::GlobalShortcutExt;

        self.app
            .global_shortcut()
            .register(shortcut.as_str())
            .map_err(map_shortcut_error)
    }

    fn unregister(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
        use tauri_plugin_global_shortcut::GlobalShortcutExt;

        self.app
            .global_shortcut()
            .unregister(shortcut.as_str())
            .map_err(map_shortcut_error)
    }

    fn ensure_registered(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
        use tauri_plugin_global_shortcut::GlobalShortcutExt;

        if self.app.global_shortcut().is_registered(shortcut.as_str()) {
            Ok(())
        } else {
            self.register(shortcut)
        }
    }
}

#[cfg(feature = "desktop-shell")]
fn map_shortcut_error(error: tauri_plugin_global_shortcut::Error) -> ShortcutError {
    match error {
        tauri_plugin_global_shortcut::Error::GlobalHotkey(message)
            if message.starts_with("HotKey already registered:") =>
        {
            ShortcutError::Conflict
        }
        _ => ShortcutError::System,
    }
}

pub struct WindowsShell<R: Runtime> {
    app: AppHandle<R>,
    active_session: Arc<Mutex<Option<String>>>,
    recent_directory: Arc<Mutex<Option<PathBuf>>>,
    #[cfg(feature = "desktop-shell")]
    shortcut: Arc<Mutex<Option<ShortcutManager<TauriShortcutRegistrar<R>>>>>,
}

impl<R: Runtime> Clone for WindowsShell<R> {
    fn clone(&self) -> Self {
        Self {
            app: self.app.clone(),
            active_session: self.active_session.clone(),
            recent_directory: self.recent_directory.clone(),
            #[cfg(feature = "desktop-shell")]
            shortcut: self.shortcut.clone(),
        }
    }
}

impl<R: Runtime> WindowsShell<R> {
    #[must_use]
    pub fn new(app: AppHandle<R>) -> Self {
        Self {
            app,
            active_session: Arc::new(Mutex::new(None)),
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
        let platform = WindowsPlatform::new();
        for _ in 0..2 {
            let cursor = self
                .app
                .cursor_position()
                .map_err(|_| PlatformError::DisplayUnavailable)?;
            let monitor = self
                .app
                .monitor_from_point(cursor.x, cursor.y)
                .map_err(|_| PlatformError::DisplayUnavailable)?
                .ok_or(PlatformError::DisplayUnavailable)?;
            let scale = monitor.scale_factor();
            let logical_size = monitor.size().to_logical::<f64>(scale);
            let scale_x = f64::from(monitor.size().width) / logical_size.width;
            let scale_y = f64::from(monitor.size().height) / logical_size.height;
            let snapshot = platform.capture_snapshot_at_scale(scale_x, scale_y)?;
            let descriptor = snapshot.descriptor();
            let Some(global_origin) = descriptor.global_origin else {
                continue;
            };
            if global_origin.x == monitor.position().x
                && global_origin.y == monitor.position().y
                && descriptor.physical_size.width == monitor.size().width
                && descriptor.physical_size.height == monitor.size().height
            {
                let prepared = self.prepare_overlay(
                    global_origin.x,
                    global_origin.y,
                    descriptor.physical_size.width,
                    descriptor.physical_size.height,
                    scale,
                );
                match prepared {
                    Ok(()) => {
                        *self
                            .active_session
                            .lock()
                            .map_err(|_| PlatformError::InternalState)? =
                            Some(descriptor.session_id.clone());
                        return Ok(snapshot);
                    }
                    Err(PlatformError::DisplayUnavailable) => continue,
                    Err(error) => return Err(error),
                }
            }
        }
        Err(PlatformError::DisplayUnavailable)
    }

    pub fn write_png_clipboard(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError> {
        self.validate_session(session_id)?;
        validate_png(png)?;
        #[cfg(windows)]
        {
            let owner = self.overlay_owner()?;
            crate::sys::write_png(owner, png)
        }
        #[cfg(not(windows))]
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
        let global_origin = descriptor
            .global_origin
            .ok_or(PlatformError::DisplayUnavailable)?;
        let center_x = f64::from(global_origin.x) + f64::from(descriptor.physical_size.width) / 2.0;
        let center_y =
            f64::from(global_origin.y) + f64::from(descriptor.physical_size.height) / 2.0;
        let (descriptor_scale_x, descriptor_scale_y) = descriptor.scale();

        for _ in 0..2 {
            let monitor = self
                .app
                .monitor_from_point(center_x, center_y)
                .map_err(|_| PlatformError::DisplayUnavailable)?
                .ok_or(PlatformError::DisplayUnavailable)?;
            let scale = monitor.scale_factor();
            if monitor.position().x != global_origin.x
                || monitor.position().y != global_origin.y
                || monitor.size().width != descriptor.physical_size.width
                || monitor.size().height != descriptor.physical_size.height
                || (scale - descriptor_scale_x).abs() > 0.01
                || (scale - descriptor_scale_y).abs() > 0.01
            {
                return Err(PlatformError::DisplayUnavailable);
            }

            match self.prepare_overlay(
                global_origin.x,
                global_origin.y,
                descriptor.physical_size.width,
                descriptor.physical_size.height,
                scale,
            ) {
                Ok(()) => {}
                Err(PlatformError::DisplayUnavailable) => continue,
                Err(error) => return Err(error),
            }

            let verified = self
                .app
                .monitor_from_point(center_x, center_y)
                .map_err(|_| PlatformError::DisplayUnavailable)?
                .is_some_and(|current| {
                    current.position() == monitor.position()
                        && current.size() == monitor.size()
                        && (current.scale_factor() - scale).abs() <= 0.01
                });
            if verified {
                return self
                    .overlay()?
                    .show()
                    .map_err(|_| PlatformError::OverlayFailed);
            }
        }
        Err(PlatformError::DisplayUnavailable)
    }

    pub fn reload_overlay(&self) -> Result<(), PlatformError> {
        self.overlay()?
            .eval("window.location.reload()")
            .map_err(|_| PlatformError::OverlayFailed)
    }

    #[cfg(feature = "desktop-shell")]
    pub fn shortcut_manager(
        &self,
        current: Shortcut,
    ) -> Result<ShortcutManager<TauriShortcutRegistrar<R>>, ShortcutError> {
        let mut registrar = TauriShortcutRegistrar {
            app: self.app.clone(),
        };
        registrar.register(&current)?;
        Ok(ShortcutManager::new(registrar, current))
    }

    #[cfg(feature = "desktop-shell")]
    pub fn watch_shortcut_resume(
        &self,
        manager: Arc<Mutex<ShortcutManager<TauriShortcutRegistrar<R>>>>,
    ) -> Result<crate::ResumeLease, PlatformError> {
        let shell = self.clone();
        crate::watch_resume(Arc::new(move || {
            let restored = manager
                .lock()
                .map_err(|_| ShortcutError::System)
                .and_then(|mut manager| manager.restore_after_resume());
            if restored.is_err() {
                let _ = shell.notify(StableNotification::ShortcutResumeFailed);
            }
        }))
    }

    #[cfg(feature = "desktop-shell")]
    pub fn autostart_enabled(&self) -> Result<bool, PlatformError> {
        use tauri_plugin_autostart::ManagerExt;

        self.app
            .autolaunch()
            .is_enabled()
            .map_err(|_| PlatformError::AutoStartFailed)
    }

    #[cfg(feature = "desktop-shell")]
    pub fn set_autostart(&self, enabled: bool) -> Result<(), PlatformError> {
        use tauri_plugin_autostart::ManagerExt;

        let manager = self.app.autolaunch();
        let previous = manager
            .is_enabled()
            .map_err(|_| PlatformError::AutoStartFailed)?;
        if previous == enabled {
            return Ok(());
        }
        let changed = if enabled {
            manager.enable()
        } else {
            manager.disable()
        };
        if changed.is_ok() && manager.is_enabled().ok() == Some(enabled) {
            return Ok(());
        }
        let _ = if previous {
            manager.enable()
        } else {
            manager.disable()
        };
        Err(PlatformError::AutoStartFailed)
    }

    #[cfg(feature = "desktop-shell")]
    pub fn notify(&self, notification: StableNotification) -> Result<(), PlatformError> {
        use tauri_plugin_notification::NotificationExt;

        let body = match notification {
            StableNotification::ShortcutConflict => "快捷键已被其他应用占用，原快捷键保持不变。",
            StableNotification::ShortcutResumeFailed => {
                "系统唤醒后未能恢复截图快捷键，请在设置中重新选择。"
            }
            StableNotification::AutoStartFailed => "开机启动设置未能更新，原设置保持不变。",
        };
        self.app
            .notification()
            .builder()
            .title("Snaploom")
            .body(body)
            .show()
            .map_err(|_| PlatformError::NotificationFailed)
    }

    #[cfg(feature = "desktop-shell")]
    fn replace_shortcut_registration(&self, accelerator: &str) -> Result<(), PlatformError> {
        let candidate = Shortcut::parse(accelerator).map_err(map_platform_shortcut_error)?;
        let result = {
            let mut shortcut = self
                .shortcut
                .lock()
                .map_err(|_| PlatformError::InternalState)?;
            if let Some(manager) = shortcut.as_mut() {
                manager
                    .replace(candidate)
                    .map_err(map_platform_shortcut_error)
            } else {
                self.shortcut_manager(candidate)
                    .map_err(map_platform_shortcut_error)
                    .map(|manager| *shortcut = Some(manager))
            }
        };
        if result == Err(PlatformError::ShortcutConflict) {
            let _ = self.notify(StableNotification::ShortcutConflict);
        }
        result
    }

    #[cfg(feature = "desktop-shell")]
    fn watch_platform_resume(
        &self,
        sink: Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>,
    ) -> Result<crate::ResumeLease, PlatformError> {
        if self
            .shortcut
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .is_none()
        {
            return Err(PlatformError::ShortcutFailed);
        }
        let shortcut = self.shortcut.clone();
        let shell = self.clone();
        crate::watch_resume(Arc::new(move || {
            let restored =
                shortcut
                    .lock()
                    .map_err(|_| ShortcutError::System)
                    .and_then(|mut shortcut| {
                        shortcut
                            .as_mut()
                            .ok_or(ShortcutError::System)?
                            .restore_after_resume()
                    });
            if restored.is_err() {
                let _ = shell.notify(StableNotification::ShortcutResumeFailed);
            }
            sink(PlatformEvent::Resumed);
        }))
    }

    pub fn choose_png_destination(
        &self,
        session_id: &str,
        suggested_name: &str,
        recent_directory: Option<&Path>,
    ) -> Result<Option<PendingPngSave>, PlatformError> {
        self.validate_session(session_id)?;
        let starting_directory = if let Some(directory) = recent_directory {
            Some(directory.to_path_buf())
        } else {
            self.recent_directory
                .lock()
                .map_err(|_| PlatformError::InternalState)?
                .clone()
        };
        let suggested_name = safe_suggested_name(suggested_name);
        #[cfg(windows)]
        let destination = crate::sys::choose_png_destination(
            self.overlay_owner()?,
            &suggested_name,
            starting_directory.as_deref(),
        )?;
        #[cfg(not(windows))]
        let destination = {
            let overlay = self.overlay()?;
            let mut dialog = self
                .app
                .dialog()
                .file()
                .set_parent(&overlay)
                .add_filter("PNG image", &["png"])
                .set_file_name(suggested_name);
            if let Some(directory) = starting_directory.filter(|path| path.is_absolute()) {
                dialog = dialog.set_directory(directory);
            }
            dialog
                .blocking_save_file()
                .map(|destination| destination.into_path())
                .transpose()
                .map_err(|_| PlatformError::SaveDialogFailed)?
        };
        let Some(mut destination) = destination else {
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
        atomic_save(&pending.destination, png)?;
        let mut recent = self
            .recent_directory
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        *recent = pending.destination.parent().map(Path::to_path_buf);
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

    fn overlay(&self) -> Result<WebviewWindow<R>, PlatformError> {
        self.app
            .get_webview_window(OVERLAY_LABEL)
            .ok_or(PlatformError::OverlayFailed)
    }

    fn prepare_overlay(
        &self,
        x: i32,
        y: i32,
        width: u32,
        height: u32,
        expected_scale: f64,
    ) -> Result<(), PlatformError> {
        let overlay = self.overlay()?;
        overlay.hide().map_err(|_| PlatformError::OverlayFailed)?;
        overlay
            .set_position(PhysicalPosition::new(x, y))
            .map_err(|_| PlatformError::OverlayFailed)?;
        overlay
            .set_size(PhysicalSize::new(width, height))
            .map_err(|_| PlatformError::OverlayFailed)?;
        overlay
            .set_always_on_top(true)
            .map_err(|_| PlatformError::OverlayFailed)?;
        #[cfg(windows)]
        {
            let dpi = crate::sys::prepare_overlay(self.overlay_owner()?, x, y, width, height)?;
            let actual_scale = f64::from(dpi) / 96.0;
            if (actual_scale - expected_scale).abs() > 0.01 {
                return Err(PlatformError::DisplayUnavailable);
            }
        }
        #[cfg(not(windows))]
        let _ = expected_scale;
        Ok(())
    }

    #[cfg(windows)]
    fn overlay_owner(&self) -> Result<isize, PlatformError> {
        self.overlay()?
            .hwnd()
            .map(|handle| handle.0 as isize)
            .map_err(|_| PlatformError::OverlayFailed)
    }
}

#[cfg(feature = "desktop-shell")]
fn map_platform_shortcut_error(error: ShortcutError) -> PlatformError {
    match error {
        ShortcutError::Conflict => PlatformError::ShortcutConflict,
        ShortcutError::Invalid | ShortcutError::System | ShortcutError::RollbackFailed => {
            PlatformError::ShortcutFailed
        }
    }
}

#[cfg(feature = "desktop-shell")]
impl<R: Runtime> PlatformAdapter for WindowsShell<R> {
    type PendingSave = PendingPngSave;
    type ResumeLease = crate::ResumeLease;

    fn platform_name(&self) -> &'static str {
        "windows"
    }

    fn capture_snapshot(&self) -> Result<CaptureSnapshot, PlatformError> {
        WindowsShell::capture_snapshot(self)
    }

    fn show_overlay(&self, descriptor: &CaptureSnapshotDescriptor) -> Result<(), PlatformError> {
        self.show_overlay_for(descriptor)
    }

    fn hide_overlay(&self, session_id: &str) -> Result<(), PlatformError> {
        WindowsShell::hide_overlay(self, session_id)
    }

    fn finish_session(&self, session_id: &str) -> Result<(), PlatformError> {
        WindowsShell::finish_session(self, session_id)
    }

    fn write_png(&self, session_id: &str, png: &[u8]) -> Result<(), PlatformError> {
        self.write_png_clipboard(session_id, png)
    }

    fn choose_png_destination(
        &self,
        session_id: &str,
        suggested_name: &str,
    ) -> Result<Option<Self::PendingSave>, PlatformError> {
        WindowsShell::choose_png_destination(self, session_id, suggested_name, None)
    }

    fn commit_png_save(
        &self,
        session_id: &str,
        pending: Self::PendingSave,
        png: &[u8],
    ) -> Result<SaveDisposition, PlatformError> {
        WindowsShell::commit_png_save(self, session_id, pending, png)?;
        Ok(SaveDisposition::Saved)
    }

    fn replace_shortcut(&self, accelerator: &str) -> Result<(), PlatformError> {
        self.replace_shortcut_registration(accelerator)
    }

    fn watch_resume(
        &self,
        sink: Arc<dyn Fn(PlatformEvent) + Send + Sync + 'static>,
    ) -> Result<Self::ResumeLease, PlatformError> {
        self.watch_platform_resume(sink)
    }

    fn autostart_enabled(&self) -> Result<bool, PlatformError> {
        WindowsShell::autostart_enabled(self)
    }

    fn set_autostart(&self, enabled: bool) -> Result<(), PlatformError> {
        let result = WindowsShell::set_autostart(self, enabled);
        if result.is_err() {
            let _ = WindowsShell::notify(self, StableNotification::AutoStartFailed);
        }
        result
    }

    fn notify(&self, notification: PlatformNotification) -> Result<(), PlatformError> {
        let notification = match notification {
            PlatformNotification::ShortcutConflict => StableNotification::ShortcutConflict,
            PlatformNotification::ShortcutResumeFailed => StableNotification::ShortcutResumeFailed,
            PlatformNotification::AutoStartFailed => StableNotification::AutoStartFailed,
        };
        WindowsShell::notify(self, notification)
    }
}

fn validate_png(png: &[u8]) -> Result<(), PlatformError> {
    if png.len() > MAX_PNG_BYTES || !png.starts_with(PNG_SIGNATURE) {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    Ok(())
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
    let written = file.write_all(png).and_then(|()| file.sync_all());
    drop(file);
    if written.is_err() {
        let _ = std::fs::remove_file(&temporary_path);
        return Err(PlatformError::FileWriteFailed);
    }
    #[cfg(windows)]
    let replaced = crate::sys::atomic_replace(&temporary_path, destination);
    #[cfg(not(windows))]
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
    fn suggested_name_must_be_a_png_file_name_not_a_path() {
        assert_eq!(
            safe_suggested_name("Snaploom_2026.png"),
            "Snaploom_2026.png"
        );
        assert_eq!(safe_suggested_name("../secret.png"), "Snaploom.png");
        assert_eq!(safe_suggested_name("capture.jpg"), "Snaploom.png");
    }

    #[test]
    fn rejects_non_png_and_oversized_payloads_before_native_side_effects() {
        assert_eq!(
            validate_png(b"not-png"),
            Err(PlatformError::ClipboardWriteFailed)
        );
        let mut oversized = vec![0_u8; MAX_PNG_BYTES + 1];
        oversized[..PNG_SIGNATURE.len()].copy_from_slice(PNG_SIGNATURE);
        assert_eq!(
            validate_png(&oversized),
            Err(PlatformError::ClipboardWriteFailed)
        );
    }

    #[cfg(feature = "desktop-shell")]
    #[test]
    fn windows_shell_exposes_the_complete_platform_adapter_contract() {
        fn assert_platform_adapter<T: PlatformAdapter>() {}

        assert_platform_adapter::<WindowsShell<tauri::Wry>>();
    }

    #[cfg(feature = "desktop-shell")]
    #[test]
    fn only_an_explicit_already_registered_error_is_a_shortcut_conflict() {
        assert_eq!(
            map_shortcut_error(tauri_plugin_global_shortcut::Error::GlobalHotkey(
                "HotKey already registered: Alt+Shift+A".into()
            )),
            ShortcutError::Conflict
        );
        assert_eq!(
            map_shortcut_error(tauri_plugin_global_shortcut::Error::GlobalHotkey(
                "Failed to unregister hotkey".into()
            )),
            ShortcutError::System
        );
    }
}
