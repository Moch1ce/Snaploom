use std::io::Cursor;
use std::sync::mpsc::{Receiver, SyncSender, sync_channel};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

use snaploom_capture_session::{
    CapturePermission, CaptureRequest, SensitivePng, SessionBackend, SessionControl,
    SessionFailure, SessionTerminal,
};
#[cfg(target_os = "macos")]
use snaploom_platform_contract::CapturePermissionState;
use snaploom_platform_contract::{
    CaptureSnapshot, CaptureSnapshotDescriptor, MAX_CAPTURE_FRAME_BYTES, PlatformError,
};
#[cfg(target_os = "macos")]
use snaploom_platform_macos::SaveDisposition;
#[cfg(all(not(target_os = "macos"), any(windows, test)))]
use snaploom_platform_windows::SaveDisposition;
use tauri::ipc::{InvokeBody, Request, Response};
use tauri::{State, Wry};
use zeroize::Zeroize;

#[cfg(target_os = "macos")]
pub(crate) type NativeShell = snaploom_platform_macos::MacShell<Wry>;
#[cfg(all(not(target_os = "macos"), any(windows, test)))]
pub(crate) type NativeShell = snaploom_platform_windows::WindowsShell<Wry>;

const TERMINAL_POLL: Duration = Duration::from_millis(25);
const MAX_OUTPUT_PNG_BYTES: usize = 100 * 1024 * 1024;
const SESSION_ID_HEADER: &str = "x-snaploom-session-id";
const SUGGESTED_NAME_HEADER: &str = "x-snaploom-suggested-name";

#[derive(Debug)]
struct PendingOutput {
    png: SensitiveBytes,
    pixel_width: u32,
    pixel_height: u32,
    clipboard_written: bool,
}

#[derive(Debug)]
struct SensitiveBytes(Vec<u8>);

impl SensitiveBytes {
    fn as_slice(&self) -> &[u8] {
        &self.0
    }

    fn into_vec(mut self) -> Vec<u8> {
        std::mem::take(&mut self.0)
    }
}

impl Drop for SensitiveBytes {
    fn drop(&mut self) {
        self.0.zeroize();
    }
}

struct ActiveSession {
    descriptor: CaptureSnapshotDescriptor,
    frame: Option<Vec<u8>>,
    pending_output: Option<PendingOutput>,
    clipboard_enabled: bool,
    terminal: SyncSender<SessionTerminal>,
}

pub(crate) struct NativeBridge {
    shell: OnceLock<NativeShell>,
    active: Mutex<Option<ActiveSession>>,
}

impl NativeBridge {
    pub(crate) fn new() -> Self {
        Self {
            shell: OnceLock::new(),
            active: Mutex::new(None),
        }
    }

    pub(crate) fn initialize_shell(&self, shell: NativeShell) -> Result<(), PlatformError> {
        self.shell
            .set(shell)
            .map_err(|_| PlatformError::InternalState)
    }

    fn shell(&self) -> Result<&NativeShell, PlatformError> {
        self.shell.get().ok_or(PlatformError::InternalState)
    }

    fn with_session<T>(
        &self,
        session_id: &str,
        operation: impl FnOnce(&mut ActiveSession) -> Result<T, PlatformError>,
    ) -> Result<T, PlatformError> {
        let mut active = self
            .active
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        let session = active.as_mut().ok_or(PlatformError::SessionMismatch)?;
        if session.descriptor.session_id != session_id {
            return Err(PlatformError::SessionMismatch);
        }
        operation(session)
    }

    fn start(
        &self,
        snapshot: CaptureSnapshot,
        clipboard_enabled: bool,
    ) -> Result<Receiver<SessionTerminal>, PlatformError> {
        let (descriptor, frame) = snapshot.into_parts();
        let (terminal, receiver) = sync_channel(1);
        let mut active = self
            .active
            .lock()
            .map_err(|_| PlatformError::InternalState)?;
        if active.is_some() {
            return Err(PlatformError::InternalState);
        }
        *active = Some(ActiveSession {
            descriptor,
            frame: Some(frame.into_bytes()),
            pending_output: None,
            clipboard_enabled,
            terminal,
        });
        Ok(receiver)
    }

    fn finish(&self) {
        let session_id = {
            let Ok(mut active) = self.active.lock() else {
                return;
            };
            let Some(mut active) = active.take() else {
                return;
            };
            if let Some(frame) = &mut active.frame {
                frame.fill(0);
            }
            if let Some(output) = &mut active.pending_output {
                output.png.0.zeroize();
            }
            active.descriptor.session_id.clone()
        };
        if let Ok(shell) = self.shell() {
            let _ = shell.finish_session(&session_id);
        }
    }

    fn cancel(&self, session_id: &str, by_user: bool) -> Result<(), PlatformError> {
        self.with_session(session_id, |session| {
            let hidden = self.shell()?.hide_overlay(session_id);
            let terminal = session
                .terminal
                .send(SessionTerminal::Canceled { by_user })
                .map_err(|_| PlatformError::InternalState);
            hidden.and(terminal)
        })
    }

    fn complete(&self, session_id: &str) -> Result<(), PlatformError> {
        self.with_session(session_id, |session| {
            let output = session
                .pending_output
                .take()
                .ok_or(PlatformError::InternalState)?;
            if let Err(error) = self.shell()?.hide_overlay(session_id) {
                session.pending_output = Some(output);
                return Err(error);
            }
            session
                .terminal
                .send(SessionTerminal::Completed {
                    png: SensitivePng::new(output.png.into_vec()),
                    pixel_width: output.pixel_width,
                    pixel_height: output.pixel_height,
                    clipboard_written: output.clipboard_written,
                })
                .map_err(|_| PlatformError::InternalState)
        })
    }

    fn descriptor(&self) -> Result<CaptureSnapshotDescriptor, PlatformError> {
        self.active
            .lock()
            .map_err(|_| PlatformError::InternalState)?
            .as_ref()
            .map(|session| session.descriptor.clone())
            .ok_or(PlatformError::InternalState)
    }

    fn take_frame(&self, session_id: &str) -> Result<Vec<u8>, PlatformError> {
        self.with_session(session_id, |session| {
            session.frame.take().ok_or(PlatformError::InternalState)
        })
    }

    fn write_clipboard(&self, session_id: &str, png: SensitiveBytes) -> Result<(), PlatformError> {
        let (width, height) = png_dimensions(png.as_slice())?;
        self.with_session(session_id, |session| {
            if session.clipboard_enabled {
                self.shell()?
                    .write_png_clipboard(session_id, png.as_slice())?;
            }
            session.pending_output = Some(PendingOutput {
                png,
                pixel_width: width,
                pixel_height: height,
                clipboard_written: session.clipboard_enabled,
            });
            Ok(())
        })
    }

    fn save_png(
        &self,
        session_id: &str,
        suggested_name: &str,
        png: SensitiveBytes,
    ) -> Result<SaveDisposition, PlatformError> {
        let (width, height) = png_dimensions(png.as_slice())?;
        self.with_session(session_id, |_| Ok(()))?;
        let Some(destination) =
            self.shell()?
                .choose_png_destination(session_id, suggested_name, None)?
        else {
            return Ok(SaveDisposition::Cancelled);
        };
        self.commit_save(session_id, png, width, height, |bytes| {
            self.shell()?
                .commit_png_save(session_id, destination, bytes)
        })
    }

    fn commit_save(
        &self,
        session_id: &str,
        png: SensitiveBytes,
        width: u32,
        height: u32,
        commit: impl FnOnce(&[u8]) -> Result<(), PlatformError>,
    ) -> Result<SaveDisposition, PlatformError> {
        self.with_session(session_id, |session| {
            commit(png.as_slice())?;
            session.pending_output = Some(PendingOutput {
                png,
                pixel_width: width,
                pixel_height: height,
                clipboard_written: false,
            });
            Ok(SaveDisposition::Saved)
        })
    }

    fn set_overlay_visible(&self, session_id: &str, visible: bool) -> Result<(), PlatformError> {
        self.with_session(session_id, |session| {
            if visible {
                self.shell()?.show_overlay_for(&session.descriptor)
            } else {
                self.shell()?.hide_overlay(session_id)
            }
        })
    }
}

pub(crate) struct NativeSessionBackend {
    bridge: Arc<NativeBridge>,
}

impl NativeSessionBackend {
    pub(crate) fn new(bridge: Arc<NativeBridge>) -> Self {
        Self { bridge }
    }
}

impl SessionBackend for NativeSessionBackend {
    fn run(&self, request: CaptureRequest, control: SessionControl) -> SessionTerminal {
        if control.is_canceled() {
            return SessionTerminal::Canceled { by_user: false };
        }
        let shell = match self.bridge.shell() {
            Ok(shell) => shell,
            Err(error) => return failed(error),
        };
        let snapshot = match shell.capture_snapshot() {
            Ok(snapshot) => snapshot,
            Err(error) => return failed(error),
        };
        let platform_session_id = snapshot.descriptor().session_id.clone();
        if control.is_canceled() {
            let _ = shell.finish_session(&platform_session_id);
            return SessionTerminal::Canceled { by_user: false };
        }
        let terminal = match self.bridge.start(snapshot, request.clipboard_enabled) {
            Ok(terminal) => terminal,
            Err(error) => {
                let _ = shell.finish_session(&platform_session_id);
                return failed(error);
            }
        };
        if let Err(error) = shell.reload_overlay() {
            self.bridge.finish();
            return failed(error);
        }
        loop {
            match terminal.recv_timeout(TERMINAL_POLL) {
                Ok(result) => {
                    self.bridge.finish();
                    return result;
                }
                Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => {
                    self.bridge.finish();
                    return failed(PlatformError::InternalState);
                }
                Err(std::sync::mpsc::RecvTimeoutError::Timeout) if control.is_canceled() => {
                    let _ = shell.hide_overlay(&platform_session_id);
                    self.bridge.finish();
                    return SessionTerminal::Canceled { by_user: false };
                }
                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {}
            }
        }
    }

    fn capture_permission(&self) -> Result<CapturePermission, SessionFailure> {
        #[cfg(target_os = "macos")]
        {
            Ok(
                if snaploom_platform_macos::MacPlatform::new().permission_granted() {
                    CapturePermission::Granted
                } else {
                    CapturePermission::NotGranted
                },
            )
        }
        #[cfg(not(target_os = "macos"))]
        {
            Ok(CapturePermission::NotApplicable)
        }
    }

    fn request_capture_permission(&self) -> Result<CapturePermission, SessionFailure> {
        #[cfg(target_os = "macos")]
        {
            snaploom_platform_macos::MacPlatform::new()
                .request_permission()
                .map(map_permission)
                .map_err(permission_failure)
        }
        #[cfg(not(target_os = "macos"))]
        {
            Ok(CapturePermission::NotApplicable)
        }
    }

    fn open_capture_permission_settings(&self) -> Result<(), SessionFailure> {
        #[cfg(target_os = "macos")]
        {
            snaploom_platform_macos::MacPlatform::new()
                .open_permission_settings()
                .map_err(permission_failure)
        }
        #[cfg(not(target_os = "macos"))]
        {
            Err(SessionFailure::PlatformUnavailable)
        }
    }
}

#[cfg(target_os = "macos")]
const fn map_permission(permission: CapturePermissionState) -> CapturePermission {
    match permission {
        CapturePermissionState::Granted => CapturePermission::Granted,
        CapturePermissionState::NotGranted => CapturePermission::NotGranted,
        CapturePermissionState::RestartRequired => CapturePermission::RestartRequired,
    }
}

#[cfg(target_os = "macos")]
const fn permission_failure(error: PlatformError) -> SessionFailure {
    match error {
        PlatformError::PlatformUnavailable => SessionFailure::PlatformUnavailable,
        PlatformError::PermissionNotGranted => SessionFailure::PermissionNotGranted,
        PlatformError::PermissionRevoked => SessionFailure::PermissionRevoked,
        _ => SessionFailure::Internal,
    }
}

fn failed(error: PlatformError) -> SessionTerminal {
    SessionTerminal::Failed {
        failure: match error {
            PlatformError::PlatformUnavailable => SessionFailure::PlatformUnavailable,
            PlatformError::PermissionNotGranted => SessionFailure::PermissionNotGranted,
            PlatformError::PermissionRevoked => SessionFailure::PermissionRevoked,
            PlatformError::DisplayUnavailable => SessionFailure::DisplayUnavailable,
            PlatformError::CaptureUnavailable => SessionFailure::CaptureUnavailable,
            PlatformError::FrameTimeout => SessionFailure::CaptureTimeout,
            PlatformError::PixelConversionFailed => SessionFailure::PixelConversionFailed,
            _ => SessionFailure::Internal,
        },
        retryable: !matches!(
            error,
            PlatformError::PlatformUnavailable
                | PlatformError::PermissionNotGranted
                | PlatformError::PermissionRevoked
        ),
    }
}

fn stable_error(error: PlatformError) -> String {
    match error {
        PlatformError::ClipboardBusy => "clipboard-busy",
        PlatformError::ClipboardWriteFailed => "clipboard-write-failed",
        PlatformError::SaveDialogFailed => "save-dialog-failed",
        PlatformError::FileWriteFailed => "file-write-failed",
        PlatformError::OverlayFailed => "overlay-failed",
        PlatformError::SessionMismatch => "session-mismatch",
        _ => "native-operation-failed",
    }
    .to_owned()
}

fn raw_body(request: &Request<'_>) -> Result<SensitiveBytes, String> {
    match request.body() {
        InvokeBody::Raw(bytes) => copy_bounded(bytes, MAX_OUTPUT_PNG_BYTES),
        InvokeBody::Json(_) => Err("raw-body-required".to_owned()),
    }
}

fn copy_bounded(bytes: &[u8], maximum: usize) -> Result<SensitiveBytes, String> {
    if bytes.len() > maximum {
        Err("png-too-large".to_owned())
    } else {
        Ok(SensitiveBytes(bytes.to_vec()))
    }
}

fn session_id(request: &Request<'_>) -> Result<String, String> {
    request
        .headers()
        .get(SESSION_ID_HEADER)
        .and_then(|value| value.to_str().ok())
        .filter(|value| !value.is_empty())
        .map(ToOwned::to_owned)
        .ok_or_else(|| "session-mismatch".to_owned())
}

fn png_dimensions(png: &[u8]) -> Result<(u32, u32), PlatformError> {
    let (width, height) = validate_png_chunks(png)?;
    let expected_bytes = usize::try_from(width)
        .ok()
        .and_then(|width| {
            usize::try_from(height)
                .ok()
                .and_then(|height| width.checked_mul(height))
        })
        .and_then(|pixels| pixels.checked_mul(4))
        .filter(|bytes| *bytes <= MAX_CAPTURE_FRAME_BYTES)
        .ok_or(PlatformError::ClipboardWriteFailed)?;

    let mut decoder = png::Decoder::new(Cursor::new(png));
    decoder.set_limits(png::Limits {
        bytes: MAX_CAPTURE_FRAME_BYTES + MAX_OUTPUT_PNG_BYTES,
    });
    let mut reader = decoder
        .read_info()
        .map_err(|_| PlatformError::ClipboardWriteFailed)?;
    if reader.info().width != width
        || reader.info().height != height
        || reader.info().bit_depth != png::BitDepth::Eight
        || reader.info().color_type != png::ColorType::Rgba
        || reader.output_buffer_size() != Some(expected_bytes)
    {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    let mut decoded = zeroize::Zeroizing::new(vec![0_u8; expected_bytes]);
    let output = reader
        .next_frame(&mut decoded)
        .map_err(|_| PlatformError::ClipboardWriteFailed)?;
    if output.width != width
        || output.height != height
        || output.bit_depth != png::BitDepth::Eight
        || output.color_type != png::ColorType::Rgba
        || output.buffer_size() != expected_bytes
    {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    Ok((width, height))
}

fn validate_png_chunks(png: &[u8]) -> Result<(u32, u32), PlatformError> {
    if !png.starts_with(b"\x89PNG\r\n\x1a\n") {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    let mut offset = 8_usize;
    let mut dimensions = None;
    let mut saw_idat = false;
    loop {
        let header_end = offset
            .checked_add(8)
            .filter(|end| *end <= png.len())
            .ok_or(PlatformError::ClipboardWriteFailed)?;
        let length = u32::from_be_bytes(
            png[offset..offset + 4]
                .try_into()
                .expect("fixed PNG chunk length"),
        ) as usize;
        let chunk_type = &png[offset + 4..header_end];
        let data_start = header_end;
        let data_end = data_start
            .checked_add(length)
            .filter(|end| {
                end.checked_add(4)
                    .is_some_and(|crc_end| crc_end <= png.len())
            })
            .ok_or(PlatformError::ClipboardWriteFailed)?;
        let crc_end = data_end + 4;
        match chunk_type {
            b"IHDR" if offset == 8 && length == 13 && dimensions.is_none() => {
                let data = &png[data_start..data_end];
                let width = u32::from_be_bytes(data[..4].try_into().expect("PNG IHDR width"));
                let height = u32::from_be_bytes(data[4..8].try_into().expect("PNG IHDR height"));
                if width == 0
                    || height == 0
                    || width > 32_768
                    || height > 32_768
                    || data[8] != 8
                    || data[9] != 6
                    || data[10] != 0
                    || data[11] != 0
                    || data[12] > 1
                {
                    return Err(PlatformError::ClipboardWriteFailed);
                }
                dimensions = Some((width, height));
            }
            b"sRGB" if dimensions.is_some() && !saw_idat && length == 1 => {}
            b"IDAT" if dimensions.is_some() && length > 0 => saw_idat = true,
            b"IEND" if saw_idat && length == 0 && crc_end == png.len() => {
                return dimensions.ok_or(PlatformError::ClipboardWriteFailed);
            }
            _ => return Err(PlatformError::ClipboardWriteFailed),
        }
        offset = crc_end;
    }
}

#[tauri::command]
pub(crate) fn capture_descriptor(
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<CaptureSnapshotDescriptor, String> {
    bridge.descriptor().map_err(stable_error)
}

#[tauri::command]
pub(crate) fn capture_frame(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<Response, String> {
    let session_id = session_id(&request)?;
    bridge
        .take_frame(&session_id)
        .map(Response::new)
        .map_err(stable_error)
}

#[tauri::command]
pub(crate) async fn write_png_clipboard(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<(), String> {
    let session_id = session_id(&request)?;
    let png = raw_body(&request)?;
    let bridge = bridge.inner().clone();
    tauri::async_runtime::spawn_blocking(move || bridge.write_clipboard(&session_id, png))
        .await
        .map_err(|_| "native-operation-failed".to_owned())?
        .map_err(stable_error)
}

#[tauri::command]
pub(crate) async fn save_png(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<&'static str, String> {
    let session_id = session_id(&request)?;
    let png = raw_body(&request)?;
    let suggested_name = request
        .headers()
        .get(SUGGESTED_NAME_HEADER)
        .and_then(|value| value.to_str().ok())
        .unwrap_or("Snaploom.png")
        .to_owned();
    let bridge = bridge.inner().clone();
    let disposition = tauri::async_runtime::spawn_blocking(move || {
        bridge.save_png(&session_id, &suggested_name, png)
    })
    .await
    .map_err(|_| "native-operation-failed".to_owned())?
    .map_err(stable_error)?;
    Ok(match disposition {
        SaveDisposition::Saved => "saved",
        SaveDisposition::Cancelled => "cancelled",
    })
}

#[tauri::command]
pub(crate) fn set_overlay_visible(
    request: Request<'_>,
    visible: bool,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<(), String> {
    let session_id = session_id(&request)?;
    bridge
        .set_overlay_visible(&session_id, visible)
        .map_err(stable_error)
}

#[tauri::command]
pub(crate) fn overlay_ready(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<(), String> {
    let session_id = session_id(&request)?;
    bridge
        .set_overlay_visible(&session_id, true)
        .map_err(stable_error)
}

#[tauri::command]
pub(crate) fn close_overlay(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<(), String> {
    let session_id = session_id(&request)?;
    bridge.complete(&session_id).map_err(stable_error)
}

#[tauri::command]
pub(crate) fn cancel_overlay(
    request: Request<'_>,
    bridge: State<'_, Arc<NativeBridge>>,
) -> Result<(), String> {
    let session_id = session_id(&request)?;
    bridge.cancel(&session_id, true).map_err(stable_error)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn encoded_png(width: u32, height: u32) -> Vec<u8> {
        let mut bytes = Vec::new();
        {
            let mut encoder = png::Encoder::new(&mut bytes, width, height);
            encoder.set_color(png::ColorType::Rgba);
            encoder.set_depth(png::BitDepth::Eight);
            let mut writer = encoder.write_header().unwrap();
            writer
                .write_image_data(&vec![0_u8; width as usize * height as usize * 4])
                .unwrap();
        }
        bytes
    }

    fn bridge_with_session(session_id: &str) -> NativeBridge {
        use snaploom_platform_contract::{LogicalRect, LogicalSize, PhysicalPoint, PhysicalSize};

        let bridge = NativeBridge::new();
        let (terminal, _receiver) = sync_channel(1);
        *bridge.active.lock().unwrap() = Some(ActiveSession {
            descriptor: CaptureSnapshotDescriptor {
                session_id: session_id.to_owned(),
                display_id: None,
                physical_size: PhysicalSize {
                    width: 1,
                    height: 1,
                },
                logical_size: LogicalSize {
                    width: 1.0,
                    height: 1.0,
                },
                global_origin: Some(PhysicalPoint { x: 0, y: 0 }),
                pointer_physical: PhysicalPoint { x: 0, y: 0 },
                work_area_logical: LogicalRect {
                    x: 0.0,
                    y: 0.0,
                    width: 1.0,
                    height: 1.0,
                },
                stride: 4,
                windows: Vec::new(),
            },
            frame: Some(vec![1, 2, 3, 4]),
            pending_output: None,
            clipboard_enabled: false,
            terminal,
        });
        bridge
    }

    #[test]
    fn accepts_only_complete_decodable_rgba8_png_output() {
        let png = encoded_png(640, 360);

        assert_eq!(png_dimensions(&png), Ok((640, 360)));
    }

    #[test]
    fn rejects_malformed_or_unbounded_png_dimensions() {
        assert_eq!(
            png_dimensions(b"not a png"),
            Err(PlatformError::ClipboardWriteFailed)
        );
        let mut png = encoded_png(1, 1);
        png.truncate(png.len() - 1);
        assert_eq!(
            png_dimensions(&png),
            Err(PlatformError::ClipboardWriteFailed)
        );

        let mut invalid_crc = encoded_png(1, 1);
        let idat = invalid_crc
            .windows(4)
            .position(|bytes| bytes == b"IDAT")
            .unwrap();
        invalid_crc[idat + 4] ^= 0xff;
        assert_eq!(
            png_dimensions(&invalid_crc),
            Err(PlatformError::ClipboardWriteFailed)
        );
    }

    #[test]
    fn stale_session_cannot_consume_the_active_frame() {
        let bridge = bridge_with_session("current");

        assert_eq!(
            bridge.take_frame("stale"),
            Err(PlatformError::SessionMismatch)
        );
        assert_eq!(bridge.take_frame("current"), Ok(vec![1, 2, 3, 4]));
    }

    #[test]
    fn canceled_session_cannot_commit_a_destination_selected_later() {
        use std::sync::atomic::{AtomicBool, Ordering};

        let bridge = bridge_with_session("current");
        bridge.finish();
        let wrote_file = AtomicBool::new(false);

        let result = bridge.commit_save("current", SensitiveBytes(vec![1, 2, 3]), 1, 1, |_| {
            wrote_file.store(true, Ordering::SeqCst);
            Ok(())
        });

        assert_eq!(result, Err(PlatformError::SessionMismatch));
        assert!(!wrote_file.load(Ordering::SeqCst));
    }

    #[test]
    fn bounds_binary_before_copying_it() {
        assert_eq!(copy_bounded(&[1, 2, 3], 2).unwrap_err(), "png-too-large");
        assert_eq!(copy_bounded(&[1, 2], 2).unwrap().as_slice(), [1, 2]);
    }

    #[test]
    fn capture_failures_keep_their_stable_categories() {
        for (platform, expected) in [
            (
                PlatformError::PermissionNotGranted,
                SessionFailure::PermissionNotGranted,
            ),
            (
                PlatformError::PermissionRevoked,
                SessionFailure::PermissionRevoked,
            ),
            (
                PlatformError::DisplayUnavailable,
                SessionFailure::DisplayUnavailable,
            ),
            (
                PlatformError::CaptureUnavailable,
                SessionFailure::CaptureUnavailable,
            ),
            (PlatformError::FrameTimeout, SessionFailure::CaptureTimeout),
            (
                PlatformError::PixelConversionFailed,
                SessionFailure::PixelConversionFailed,
            ),
        ] {
            let SessionTerminal::Failed { failure, .. } = failed(platform) else {
                panic!("expected failed terminal");
            };
            assert_eq!(failure, expected);
        }
    }
}
