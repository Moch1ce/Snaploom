#![cfg(any(unix, windows))]

use std::fs;
use std::io::{Read, Write};
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicU8, Ordering};
use std::sync::mpsc;
use std::thread;
use std::time::{Duration, Instant};

use prost::Message;
use snaploom_capture_protocol::envelope::Body;
use snaploom_capture_protocol::{
    CAPABILITY_PERMISSION_CONTROL, CancelCapture, CancelSource, CaptureOrigin, ClipboardMode,
    ClipboardOutcome, Envelope, Frame, FrameType, FramedReader, Hello, PROTOCOL_MINOR,
    PermissionAction, PermissionCommand, PermissionResult, PermissionState, PngAccumulator,
    StableError, StartCapture, StreamError, UiLanguage, Welcome, write_frame,
};

use crate::host_locator::resolve_host_executable;
use crate::local_transport::{
    EndpointPaths, LocalStream, TransportSecurityError, connect_authenticated,
};
use crate::{CaptureDriver, CaptureLanguage, CapturePermission, DriverRequest, Terminal};

const BOOTSTRAP_REQUEST_MAGIC: &[u8; 4] = b"SLBR";
const BOOTSTRAP_READY_MAGIC: &[u8; 4] = b"SLRD";
const BOOTSTRAP_RESPONSE_BYTES: usize = 4 + 32 + 4 + 2 + 16;
const READ_POLL: Duration = Duration::from_millis(25);

#[derive(Debug, Clone)]
pub struct IpcClientConfig {
    pub host_executable_override: Option<PathBuf>,
    pub endpoint_override: Option<EndpointPaths>,
    pub launch_timeout: Duration,
    pub handshake_timeout: Duration,
    pub origin: CaptureOrigin,
    pub language: CaptureLanguage,
}

impl Default for IpcClientConfig {
    fn default() -> Self {
        Self {
            host_executable_override: None,
            endpoint_override: None,
            launch_timeout: Duration::from_secs(5),
            handshake_timeout: Duration::from_secs(2),
            origin: CaptureOrigin::Sdk,
            language: CaptureLanguage::System,
        }
    }
}

#[derive(Debug)]
pub struct IpcDriver {
    config: IpcClientConfig,
    language: AtomicU8,
}

impl IpcDriver {
    #[must_use]
    pub const fn new(config: IpcClientConfig) -> Self {
        let language = config.language as u8;
        Self {
            config,
            language: AtomicU8::new(language),
        }
    }

    pub fn set_capture_language(&self, language: CaptureLanguage) {
        self.language.store(language as u8, Ordering::Release);
    }

    fn capture_inner(&self, request: &DriverRequest) -> Result<Terminal, StableError> {
        let paths = self.config.endpoint_override.clone().map_or_else(
            || EndpointPaths::for_current_session(1).map_err(map_transport_error),
            Ok,
        )?;
        let stream = self.connect_or_launch(&paths)?;
        stream
            .set_read_timeout(Some(self.config.handshake_timeout))
            .map_err(|_| StableError::TransportFailed)?;
        stream
            .set_write_timeout(Some(self.config.handshake_timeout))
            .map_err(|_| StableError::TransportFailed)?;
        let writer = stream
            .try_clone()
            .map_err(|_| StableError::TransportFailed)?;
        let mut reader = FramedReader::new(stream);
        let mut writer = writer;
        let handshake = handshake(&mut reader, &mut writer, 0, PROTOCOL_MINOR, 0)?;
        let connection_id = handshake.connection_id;
        reader
            .get_ref()
            .set_read_timeout(Some(READ_POLL))
            .map_err(|_| StableError::TransportFailed)?;

        let mut wire_request_id = [0_u8; 16];
        getrandom::fill(&mut wire_request_id).map_err(|_| StableError::Internal)?;
        let wire_request_id = wire_request_id.to_vec();
        let mut outgoing_sequence = 2_u64;
        write_envelope(
            &mut writer,
            FrameType::StartCapture,
            &connection_id,
            outgoing_sequence,
            Body::StartCapture(StartCapture {
                request_id: wire_request_id.clone(),
                clipboard_mode: if request.options.disable_clipboard {
                    ClipboardMode::Disabled as i32
                } else {
                    ClipboardMode::DefaultEnabled as i32
                },
                interaction_timeout_ms: request
                    .options
                    .interaction_timeout
                    .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64),
                origin: self.config.origin as i32,
                language: match self.language.load(Ordering::Acquire) {
                    value if value == CaptureLanguage::ZhCn as u8 => UiLanguage::ZhCn as i32,
                    value if value == CaptureLanguage::En as u8 => UiLanguage::En as i32,
                    _ => UiLanguage::System as i32,
                },
            }),
        )?;
        let mut expected_incoming_sequence = 2_u64;
        let mut cancel_sent = false;
        let mut png = None;
        let mut clipboard_written = false;

        loop {
            if request.is_canceled() && !cancel_sent {
                outgoing_sequence = outgoing_sequence
                    .checked_add(1)
                    .ok_or(StableError::ProtocolError)?;
                write_envelope(
                    &mut writer,
                    FrameType::CancelCapture,
                    &connection_id,
                    outgoing_sequence,
                    Body::CancelCapture(CancelCapture {
                        request_id: wire_request_id.clone(),
                    }),
                )?;
                cancel_sent = true;
            }
            let envelope = match read_envelope(&mut reader) {
                Ok((frame_type, envelope)) => {
                    if envelope.connection_id != connection_id
                        || envelope.sequence != expected_incoming_sequence
                    {
                        return Err(StableError::ProtocolError);
                    }
                    expected_incoming_sequence = expected_incoming_sequence
                        .checked_add(1)
                        .ok_or(StableError::ProtocolError)?;
                    (frame_type, envelope)
                }
                Err(IpcReadError::Timeout) => continue,
                Err(IpcReadError::Eof) => return Err(StableError::HostCrashed),
                Err(IpcReadError::Protocol) => return Err(StableError::ProtocolError),
                Err(IpcReadError::Transport) => return Err(StableError::TransportFailed),
            };
            match (envelope.0, envelope.1.body) {
                (FrameType::CaptureAccepted, Some(Body::CaptureAccepted(message)))
                    if message.request_id == wire_request_id => {}
                (FrameType::CaptureRejected, Some(Body::CaptureRejected(message)))
                    if message.request_id == wire_request_id =>
                {
                    let error = StableError::try_from(message.error)
                        .map_err(|_| StableError::ProtocolError)?;
                    return Ok(Terminal::Failed {
                        error,
                        retryable: error == StableError::Busy,
                    });
                }
                (FrameType::CancelAcknowledged, Some(Body::CancelAcknowledged(message)))
                    if message.request_id == wire_request_id => {}
                (FrameType::ResultBegin, Some(Body::ResultBegin(message)))
                    if message.request_id == wire_request_id && png.is_none() =>
                {
                    clipboard_written = message.clipboard == ClipboardOutcome::Written as i32;
                    png = Some(
                        PngAccumulator::begin(
                            message.png_length,
                            message.pixel_width,
                            message.pixel_height,
                        )
                        .map_err(|error| match error {
                            snaploom_capture_protocol::WireError::PngTooLarge => {
                                StableError::ResultTooLarge
                            }
                            _ => StableError::InvalidResult,
                        })?,
                    );
                }
                (FrameType::ResultChunk, Some(Body::ResultChunk(message)))
                    if message.request_id == wire_request_id =>
                {
                    png.as_mut()
                        .ok_or(StableError::ProtocolError)?
                        .push(message.offset, &message.data)
                        .map_err(|_| StableError::InvalidResult)?;
                }
                (FrameType::ResultEnd, Some(Body::ResultEnd(message)))
                    if message.request_id == wire_request_id =>
                {
                    let png = png
                        .take()
                        .ok_or(StableError::ProtocolError)?
                        .finish()
                        .map_err(|_| StableError::InvalidResult)?;
                    return Ok(Terminal::Completed {
                        png,
                        clipboard_written,
                    });
                }
                (FrameType::CaptureCanceled, Some(Body::CaptureCanceled(message)))
                    if message.request_id == wire_request_id =>
                {
                    let source = CancelSource::try_from(message.source)
                        .map_err(|_| StableError::ProtocolError)?;
                    return Ok(Terminal::Canceled { source });
                }
                (FrameType::CaptureFailed, Some(Body::CaptureFailed(message)))
                    if message.request_id == wire_request_id =>
                {
                    return Ok(Terminal::Failed {
                        error: StableError::try_from(message.error)
                            .map_err(|_| StableError::ProtocolError)?,
                        retryable: message.retryable,
                    });
                }
                _ => return Err(StableError::ProtocolError),
            }
        }
    }

    pub fn capture_permission(&self) -> Result<CapturePermission, StableError> {
        self.permission_inner(PermissionAction::Query)
    }

    /// May prompt the user and must only be called from an explicit UI action.
    pub fn request_capture_permission(&self) -> Result<CapturePermission, StableError> {
        self.permission_inner(PermissionAction::Request)
    }

    pub fn open_capture_permission_settings(&self) -> Result<(), StableError> {
        self.permission_inner(PermissionAction::OpenSettings)
            .map(|_| ())
    }

    fn permission_inner(&self, action: PermissionAction) -> Result<CapturePermission, StableError> {
        let paths = self.config.endpoint_override.clone().map_or_else(
            || EndpointPaths::for_current_session(1).map_err(map_transport_error),
            Ok,
        )?;
        let stream = self.connect_or_launch(&paths)?;
        stream
            .set_read_timeout(Some(self.config.handshake_timeout))
            .map_err(|_| StableError::TransportFailed)?;
        stream
            .set_write_timeout(Some(self.config.handshake_timeout))
            .map_err(|_| StableError::TransportFailed)?;
        let writer = stream
            .try_clone()
            .map_err(|_| StableError::TransportFailed)?;
        let mut reader = FramedReader::new(stream);
        let mut writer = writer;
        let handshake = handshake(
            &mut reader,
            &mut writer,
            1,
            PROTOCOL_MINOR,
            CAPABILITY_PERMISSION_CONTROL,
        )?;
        write_envelope(
            &mut writer,
            FrameType::PermissionCommand,
            &handshake.connection_id,
            2,
            Body::PermissionCommand(PermissionCommand {
                action: action as i32,
            }),
        )?;
        let (frame_type, envelope) = read_envelope(&mut reader).map_err(|error| match error {
            IpcReadError::Timeout => StableError::HandshakeTimeout,
            IpcReadError::Eof => StableError::HostCrashed,
            IpcReadError::Transport => StableError::TransportFailed,
            IpcReadError::Protocol => StableError::ProtocolError,
        })?;
        let Some(Body::PermissionResult(PermissionResult { state, error })) = envelope.body else {
            return Err(StableError::ProtocolError);
        };
        if frame_type != FrameType::PermissionResult
            || envelope.connection_id != handshake.connection_id
            || envelope.sequence != 2
        {
            return Err(StableError::ProtocolError);
        }
        let error = StableError::try_from(error).map_err(|_| StableError::ProtocolError)?;
        if error != StableError::None {
            return Err(error);
        }
        match PermissionState::try_from(state).map_err(|_| StableError::ProtocolError)? {
            PermissionState::Granted => Ok(CapturePermission::Granted),
            PermissionState::NotGranted => Ok(CapturePermission::NotGranted),
            PermissionState::RestartRequired => Ok(CapturePermission::RestartRequired),
            PermissionState::NotApplicable => Ok(CapturePermission::NotApplicable),
            PermissionState::Unspecified => Err(StableError::ProtocolError),
        }
    }

    fn connect_or_launch(&self, paths: &EndpointPaths) -> Result<LocalStream, StableError> {
        let connect_deadline = Instant::now() + self.config.handshake_timeout;
        loop {
            match connect_authenticated(paths) {
                Ok(stream) => return Ok(stream),
                Err(TransportSecurityError::Io(
                    std::io::ErrorKind::NotFound | std::io::ErrorKind::ConnectionRefused,
                )) => break,
                Err(TransportSecurityError::Io(kind))
                    if transient_connect_error(kind) && Instant::now() < connect_deadline =>
                {
                    thread::sleep(Duration::from_millis(10));
                }
                Err(error) => return Err(map_transport_error(error)),
            }
        }
        let executable = resolve_host_executable(self.config.host_executable_override.as_deref())?;
        launch_host(&executable, self.config.launch_timeout)?;
        let deadline = Instant::now() + self.config.launch_timeout;
        loop {
            match connect_authenticated(paths) {
                Ok(stream) => return Ok(stream),
                Err(TransportSecurityError::Io(kind))
                    if matches!(
                        kind,
                        std::io::ErrorKind::NotFound | std::io::ErrorKind::ConnectionRefused
                    ) && Instant::now() < deadline =>
                {
                    thread::sleep(Duration::from_millis(10));
                }
                Err(error) => return Err(map_transport_error(error)),
            }
        }
    }
}

fn transient_connect_error(kind: std::io::ErrorKind) -> bool {
    matches!(
        kind,
        std::io::ErrorKind::WouldBlock
            | std::io::ErrorKind::TimedOut
            | std::io::ErrorKind::ResourceBusy
            | std::io::ErrorKind::Interrupted
    )
}

impl CaptureDriver for IpcDriver {
    fn capture(&self, request: DriverRequest) -> Terminal {
        self.capture_inner(&request)
            .unwrap_or_else(|error| Terminal::Failed {
                retryable: matches!(
                    error,
                    StableError::HostCrashed
                        | StableError::TransportFailed
                        | StableError::HostStartTimeout
                ),
                error,
            })
    }
}

struct Handshake {
    connection_id: Vec<u8>,
}

fn handshake(
    reader: &mut FramedReader<LocalStream>,
    writer: &mut LocalStream,
    min_minor: u16,
    max_minor: u16,
    requested_capabilities: u64,
) -> Result<Handshake, StableError> {
    let mut nonce = [0_u8; 32];
    getrandom::fill(&mut nonce).map_err(|_| StableError::Internal)?;
    write_envelope(
        writer,
        FrameType::Hello,
        &[],
        1,
        Body::Hello(Hello {
            protocol_major: 1,
            min_protocol_minor: u32::from(min_minor),
            max_protocol_minor: u32::from(max_minor),
            client_nonce: nonce.to_vec(),
            sdk_semver: env!("CARGO_PKG_VERSION").into(),
            requested_capabilities,
        }),
    )?;
    let (frame_type, envelope) = read_envelope(reader).map_err(|error| match error {
        IpcReadError::Timeout => StableError::HandshakeTimeout,
        IpcReadError::Eof => StableError::HostCrashed,
        IpcReadError::Transport => StableError::TransportFailed,
        IpcReadError::Protocol => StableError::ProtocolError,
    })?;
    let Some(Body::Welcome(Welcome {
        protocol_major,
        negotiated_protocol_minor,
        echoed_client_nonce,
        connection_id,
        max_frame_bytes,
        max_png_bytes,
        capabilities,
        ..
    })) = envelope.body
    else {
        return Err(StableError::ProtocolError);
    };
    if frame_type != FrameType::Welcome
        || envelope.sequence != 1
        || envelope.connection_id != connection_id
        || protocol_major != 1
        || negotiated_protocol_minor < u32::from(min_minor)
        || negotiated_protocol_minor > u32::from(max_minor)
        || echoed_client_nonce != nonce
        || connection_id.len() != 16
        || max_frame_bytes as usize != snaploom_capture_protocol::MAX_FRAME_BYTES
        || max_png_bytes != snaploom_capture_protocol::MAX_PNG_BYTES
        || capabilities & requested_capabilities != requested_capabilities
    {
        return Err(StableError::ProtocolIncompatible);
    }
    Ok(Handshake { connection_id })
}

fn write_envelope(
    writer: &mut LocalStream,
    frame_type: FrameType,
    connection_id: &[u8],
    sequence: u64,
    body: Body,
) -> Result<(), StableError> {
    let envelope = Envelope {
        connection_id: connection_id.to_vec(),
        sequence,
        body: Some(body),
    };
    let frame =
        Frame::new(frame_type, envelope.encode_to_vec()).map_err(|_| StableError::ProtocolError)?;
    write_frame(writer, &frame).map_err(|_| StableError::TransportFailed)
}

enum IpcReadError {
    Timeout,
    Eof,
    Protocol,
    Transport,
}

fn read_envelope(
    reader: &mut FramedReader<LocalStream>,
) -> Result<(FrameType, Envelope), IpcReadError> {
    let frame = reader.read_frame().map_err(|error| match error {
        StreamError::Eof => IpcReadError::Eof,
        StreamError::Wire(_) => IpcReadError::Protocol,
        StreamError::Io(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::WouldBlock | std::io::ErrorKind::TimedOut
            ) =>
        {
            IpcReadError::Timeout
        }
        StreamError::Io(_) => IpcReadError::Transport,
    })?;
    let envelope =
        Envelope::decode(frame.payload.as_slice()).map_err(|_| IpcReadError::Protocol)?;
    Ok((frame.frame_type, envelope))
}

fn map_transport_error(error: TransportSecurityError) -> StableError {
    match error {
        TransportSecurityError::Io(std::io::ErrorKind::NotFound) => StableError::HostNotFound,
        TransportSecurityError::WrongOwner
        | TransportSecurityError::InsecureMode
        | TransportSecurityError::UnexpectedFileType
        | TransportSecurityError::Symlink
        | TransportSecurityError::PathTooLong
        | TransportSecurityError::PeerAuthentication => StableError::AuthenticationFailed,
        TransportSecurityError::Io(_) => StableError::TransportFailed,
    }
}

fn launch_host(executable: &PathBuf, timeout: Duration) -> Result<(), StableError> {
    let metadata = fs::symlink_metadata(executable).map_err(|_| StableError::HostNotFound)?;
    if !secure_executable(executable, &metadata) {
        return Err(StableError::HostStartFailed);
    }
    let mut command = Command::new(executable);
    command
        .arg("--snaploom-bootstrap-stdio-v1")
        .env_clear()
        .current_dir(executable.parent().ok_or(StableError::HostStartFailed)?)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null());
    for name in ["HOME", "LANG", "LC_ALL", "TMPDIR"] {
        if let Some(value) = std::env::var_os(name) {
            command.env(name, value);
        }
    }
    let mut child = command.spawn().map_err(|_| StableError::HostStartFailed)?;
    let mut challenge = [0_u8; 32];
    getrandom::fill(&mut challenge).map_err(|_| StableError::Internal)?;
    let Some(mut stdin) = child.stdin.take() else {
        return Err(StableError::HostStartFailed);
    };
    stdin
        .write_all(&[BOOTSTRAP_REQUEST_MAGIC.as_slice(), &challenge].concat())
        .map_err(|_| StableError::HostStartFailed)?;
    drop(stdin);
    let Some(mut stdout) = child.stdout.take() else {
        return Err(StableError::HostStartFailed);
    };
    let (sender, receiver) = mpsc::sync_channel(1);
    thread::Builder::new()
        .name("snaploom-host-bootstrap".into())
        .spawn(move || {
            let mut response = [0_u8; BOOTSTRAP_RESPONSE_BYTES];
            let result = stdout.read_exact(&mut response).map(|()| response);
            let _ = sender.send(result);
        })
        .map_err(|_| StableError::OutOfMemory)?;
    let response = match receiver.recv_timeout(timeout) {
        Ok(Ok(response)) => response,
        Ok(Err(_)) => return Err(StableError::HostStartFailed),
        Err(_) => {
            let _ = child.kill();
            let _ = child.wait();
            return Err(StableError::HostStartTimeout);
        }
    };
    if &response[..4] != BOOTSTRAP_READY_MAGIC
        || response[4..36] != challenge
        || u32::from_le_bytes(response[36..40].try_into().expect("four-byte PID")) == 0
        || u16::from_le_bytes([response[40], response[41]]) != 1
        || response[42..].iter().all(|byte| *byte == 0)
    {
        let _ = child.kill();
        let _ = child.wait();
        return Err(StableError::AuthenticationFailed);
    }
    Ok(())
}

#[cfg(unix)]
fn secure_executable(executable: &std::path::Path, metadata: &fs::Metadata) -> bool {
    use std::os::unix::fs::MetadataExt;

    executable.is_absolute()
        && metadata.is_file()
        && !metadata.file_type().is_symlink()
        && metadata.mode() & 0o002 == 0
}

#[cfg(windows)]
fn secure_executable(executable: &std::path::Path, metadata: &fs::Metadata) -> bool {
    executable.is_absolute()
        && metadata.is_file()
        && !metadata.file_type().is_symlink()
        && !executable.as_os_str().to_string_lossy().starts_with(r"\\")
}
