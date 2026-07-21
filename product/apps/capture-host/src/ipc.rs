use std::io;
#[cfg(feature = "tauri-runtime")]
use std::io::{Read, Write};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, mpsc};
use std::thread::{self, JoinHandle};
use std::time::{Duration, Instant};

use prost::Message;
#[cfg(feature = "tauri-runtime")]
use snaploom_capture_client::local_transport::connect_authenticated;
use snaploom_capture_client::local_transport::{
    EndpointPaths, LeaderOutcome, LocalStream, TransportSecurityError, UnixLeader,
};
use snaploom_capture_protocol::envelope::Body;
use snaploom_capture_protocol::{
    CancelAcknowledged, CancelSource, CaptureAccepted, CaptureCanceled, CaptureFailed,
    CaptureOrigin, CaptureRejected, ClipboardMode, ClipboardOutcome, Envelope, FrameType,
    FramedReader, Hello, ResultBegin, ResultChunk, ResultEnd, StableError, StreamError, Welcome,
    write_frame_payload,
};
use snaploom_capture_session::{
    BeginError, CaptureRequest, CaptureSessionGate, ClientId, HostLifecycle, SessionBackend,
    SessionFailure, SessionLease, SessionOrigin, SessionPhase, SessionTerminal,
};
use zeroize::{Zeroize, Zeroizing};

const CONNECTION_POLL: Duration = Duration::from_millis(25);
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(2);
const RESULT_CHUNK_BYTES: usize = snaploom_capture_protocol::MAX_RESULT_CHUNK_BYTES;
const MAX_AUTHENTICATED_CONNECTIONS: usize = 32;

#[derive(Debug)]
pub enum HostServerError {
    Transport(TransportSecurityError),
    Io(io::ErrorKind),
    Thread,
}

impl From<TransportSecurityError> for HostServerError {
    fn from(error: TransportSecurityError) -> Self {
        Self::Transport(error)
    }
}

pub enum HostServerOutcome {
    Leader(HostServer),
    Existing(EndpointPaths),
}

pub struct HostServer {
    stop: Arc<AtomicBool>,
    idle_exit: Arc<AtomicBool>,
    thread: Option<JoinHandle<()>>,
    paths: EndpointPaths,
    gate: CaptureSessionGate,
}

impl HostServer {
    pub fn start(backend: Arc<dyn SessionBackend>) -> Result<HostServerOutcome, HostServerError> {
        let paths = EndpointPaths::for_current_session(1)?;
        Self::start_at(paths, backend)
    }

    #[doc(hidden)]
    pub fn start_at(
        paths: EndpointPaths,
        backend: Arc<dyn SessionBackend>,
    ) -> Result<HostServerOutcome, HostServerError> {
        let mut leader = match UnixLeader::try_bind(&paths)? {
            LeaderOutcome::Leader(leader) => leader,
            LeaderOutcome::Existing => return Ok(HostServerOutcome::Existing(paths)),
        };
        leader
            .set_nonblocking(true)
            .map_err(|error| HostServerError::Io(error.kind()))?;
        let stop = Arc::new(AtomicBool::new(false));
        let thread_stop = stop.clone();
        let idle_exit = Arc::new(AtomicBool::new(false));
        let thread_idle_exit = idle_exit.clone();
        let gate = CaptureSessionGate::default();
        let thread_gate = gate.clone();
        let lifecycle = Arc::new(Mutex::new(HostLifecycle::default()));
        let active_connections = Arc::new(AtomicUsize::new(0));
        let started = Instant::now();
        let thread = thread::Builder::new()
            .name("snaploom-host-ipc".into())
            .spawn(move || {
                while !thread_stop.load(Ordering::Acquire) {
                    if lifecycle
                        .lock()
                        .unwrap_or_else(|error| error.into_inner())
                        .should_exit(started.elapsed())
                    {
                        thread_idle_exit.store(true, Ordering::Release);
                        break;
                    }
                    match leader.accept_authenticated() {
                        Ok(stream) => {
                            if active_connections.fetch_add(1, Ordering::AcqRel)
                                >= MAX_AUTHENTICATED_CONNECTIONS
                            {
                                active_connections.fetch_sub(1, Ordering::AcqRel);
                                drop(stream);
                                continue;
                            }
                            lifecycle
                                .lock()
                                .unwrap_or_else(|error| error.into_inner())
                                .client_connected(started.elapsed());
                            let connection_gate = thread_gate.clone();
                            let connection_backend = backend.clone();
                            let connection_lifecycle = lifecycle.clone();
                            let connection_count = active_connections.clone();
                            let recovery_lifecycle = lifecycle.clone();
                            let recovery_count = active_connections.clone();
                            if thread::Builder::new()
                                .name("snaploom-host-connection".into())
                                .spawn(move || {
                                    let _ = serve_connection(
                                        stream,
                                        connection_gate,
                                        connection_backend,
                                    );
                                    connection_lifecycle
                                        .lock()
                                        .unwrap_or_else(|error| error.into_inner())
                                        .client_disconnected(started.elapsed());
                                    connection_count.fetch_sub(1, Ordering::AcqRel);
                                })
                                .is_err()
                            {
                                recovery_lifecycle
                                    .lock()
                                    .unwrap_or_else(|error| error.into_inner())
                                    .client_disconnected(started.elapsed());
                                recovery_count.fetch_sub(1, Ordering::AcqRel);
                            }
                        }
                        Err(TransportSecurityError::Io(io::ErrorKind::WouldBlock)) => {
                            thread::sleep(Duration::from_millis(5));
                        }
                        Err(_) => thread::sleep(Duration::from_millis(5)),
                    }
                }
            })
            .map_err(|_| HostServerError::Thread)?;
        Ok(HostServerOutcome::Leader(Self {
            stop,
            idle_exit,
            thread: Some(thread),
            paths,
            gate,
        }))
    }

    #[must_use]
    pub fn paths(&self) -> &EndpointPaths {
        &self.paths
    }

    #[must_use]
    pub fn gate(&self) -> &CaptureSessionGate {
        &self.gate
    }

    #[must_use]
    pub fn idle_exit_signal(&self) -> Arc<AtomicBool> {
        self.idle_exit.clone()
    }
}

impl Drop for HostServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn serve_connection(
    stream: LocalStream,
    gate: CaptureSessionGate,
    backend: Arc<dyn SessionBackend>,
) -> Result<(), StableError> {
    stream
        .set_read_timeout(Some(HANDSHAKE_TIMEOUT))
        .map_err(|_| StableError::TransportFailed)?;
    stream
        .set_write_timeout(Some(HANDSHAKE_TIMEOUT))
        .map_err(|_| StableError::TransportFailed)?;
    let mut writer = stream
        .try_clone()
        .map_err(|_| StableError::TransportFailed)?;
    let mut reader = FramedReader::new(stream);
    let (frame_type, hello_envelope) = read_envelope(&mut reader)?;
    let Some(Body::Hello(Hello {
        protocol_major,
        min_protocol_minor,
        max_protocol_minor: _,
        client_nonce,
        requested_capabilities,
        ..
    })) = hello_envelope.body
    else {
        return Err(StableError::ProtocolError);
    };
    if frame_type != FrameType::Hello
        || !hello_envelope.connection_id.is_empty()
        || hello_envelope.sequence != 1
        || protocol_major != 1
        || min_protocol_minor > 0
        || client_nonce.len() != 32
        || requested_capabilities != 0
    {
        return Err(StableError::ProtocolIncompatible);
    }
    let mut connection_id = [0_u8; 16];
    getrandom::fill(&mut connection_id).map_err(|_| StableError::Internal)?;
    let connection_id = connection_id.to_vec();
    write_envelope(
        &mut writer,
        FrameType::Welcome,
        &connection_id,
        1,
        Body::Welcome(Welcome {
            protocol_major: 1,
            negotiated_protocol_minor: 0,
            echoed_client_nonce: client_nonce,
            connection_id: connection_id.clone(),
            host_semver: env!("CARGO_PKG_VERSION").into(),
            capabilities: 0,
            max_frame_bytes: snaploom_capture_protocol::MAX_FRAME_BYTES as u32,
            max_png_bytes: snaploom_capture_protocol::MAX_PNG_BYTES,
        }),
    )?;
    reader
        .get_ref()
        .set_read_timeout(Some(CONNECTION_POLL))
        .map_err(|_| StableError::TransportFailed)?;
    let mut expected_incoming_sequence = 2_u64;
    let mut outgoing_sequence = 2_u64;
    let mut active: Option<ActiveSession> = None;

    loop {
        if let Some(active_session) = &mut active {
            match active_session.terminal.try_recv() {
                Ok(completion) => {
                    let sent = send_terminal(
                        &mut writer,
                        &connection_id,
                        &mut outgoing_sequence,
                        &active_session.request_id,
                        completion.terminal,
                    );
                    completion.lease.finish_cleanup();
                    sent?;
                    return Ok(());
                }
                Err(mpsc::TryRecvError::Disconnected) => return Err(StableError::Internal),
                Err(mpsc::TryRecvError::Empty) => {}
            }
        }
        let (frame_type, envelope) = match read_envelope_poll(&mut reader) {
            Ok(envelope) => envelope,
            Err(ReadPollError::Timeout) => continue,
            Err(ReadPollError::Eof) => {
                if let Some(active_session) = &active {
                    active_session.control.cancel();
                }
                return Ok(());
            }
            Err(ReadPollError::Failure(error)) => return Err(error),
        };
        if envelope.connection_id != connection_id
            || envelope.sequence != expected_incoming_sequence
        {
            return Err(StableError::ProtocolError);
        }
        expected_incoming_sequence = expected_incoming_sequence
            .checked_add(1)
            .ok_or(StableError::ProtocolError)?;
        match (frame_type, envelope.body) {
            (FrameType::StartCapture, Some(Body::StartCapture(message))) if active.is_none() => {
                if message.request_id.len() != 16 {
                    return Err(StableError::ProtocolError);
                }
                let origin = match CaptureOrigin::try_from(message.origin) {
                    Ok(CaptureOrigin::App) => SessionOrigin::App,
                    Ok(CaptureOrigin::Sdk) => SessionOrigin::Sdk,
                    _ => return Err(StableError::ProtocolError),
                };
                let client_id = ClientId::from_bytes(
                    connection_id
                        .as_slice()
                        .try_into()
                        .map_err(|_| StableError::ProtocolError)?,
                );
                let mut lease = match gate.try_begin(client_id, origin) {
                    Ok(lease) => lease,
                    Err(BeginError::Busy) => {
                        write_envelope(
                            &mut writer,
                            FrameType::CaptureRejected,
                            &connection_id,
                            outgoing_sequence,
                            Body::CaptureRejected(CaptureRejected {
                                request_id: message.request_id,
                                error: StableError::Busy as i32,
                            }),
                        )?;
                        return Ok(());
                    }
                };
                write_envelope(
                    &mut writer,
                    FrameType::CaptureAccepted,
                    &connection_id,
                    outgoing_sequence,
                    Body::CaptureAccepted(CaptureAccepted {
                        request_id: message.request_id.clone(),
                    }),
                )?;
                outgoing_sequence += 1;
                lease.set_phase(SessionPhase::Interactive);
                let control = lease.control();
                let worker_control = control.clone();
                let request = CaptureRequest {
                    origin,
                    clipboard_enabled: message.clipboard_mode != ClipboardMode::Disabled as i32,
                };
                let (terminal_sender, terminal) = mpsc::sync_channel(1);
                let session_backend = backend.clone();
                thread::Builder::new()
                    .name("snaploom-capture-session".into())
                    .spawn(move || {
                        let terminal = session_backend.run(request, worker_control);
                        lease.set_phase(SessionPhase::CleaningUp);
                        let _ = terminal_sender.send(BackendCompletion { terminal, lease });
                    })
                    .map_err(|_| StableError::OutOfMemory)?;
                active = Some(ActiveSession {
                    request_id: message.request_id,
                    control,
                    terminal,
                });
            }
            (FrameType::CancelCapture, Some(Body::CancelCapture(message))) => {
                let Some(active_session) = &active else {
                    return Err(StableError::ProtocolError);
                };
                if message.request_id != active_session.request_id {
                    return Err(StableError::ProtocolError);
                }
                active_session.control.cancel();
                write_envelope(
                    &mut writer,
                    FrameType::CancelAcknowledged,
                    &connection_id,
                    outgoing_sequence,
                    Body::CancelAcknowledged(CancelAcknowledged {
                        request_id: message.request_id,
                    }),
                )?;
                outgoing_sequence += 1;
            }
            _ => return Err(StableError::ProtocolError),
        }
    }
}

struct ActiveSession {
    request_id: Vec<u8>,
    control: snaploom_capture_session::SessionControl,
    terminal: mpsc::Receiver<BackendCompletion>,
}

struct BackendCompletion {
    terminal: SessionTerminal,
    lease: SessionLease,
}

fn send_terminal(
    writer: &mut LocalStream,
    connection_id: &[u8],
    sequence: &mut u64,
    request_id: &[u8],
    terminal: SessionTerminal,
) -> Result<(), StableError> {
    match terminal {
        SessionTerminal::Completed {
            png,
            pixel_width,
            pixel_height,
            clipboard_written,
        } => {
            write_envelope(
                writer,
                FrameType::ResultBegin,
                connection_id,
                *sequence,
                Body::ResultBegin(ResultBegin {
                    request_id: request_id.to_vec(),
                    png_length: png.as_slice().len() as u64,
                    pixel_width,
                    pixel_height,
                    clipboard: if clipboard_written {
                        ClipboardOutcome::Written as i32
                    } else {
                        ClipboardOutcome::Disabled as i32
                    },
                }),
            )?;
            *sequence += 1;
            for (index, chunk) in png.as_slice().chunks(RESULT_CHUNK_BYTES).enumerate() {
                write_envelope(
                    writer,
                    FrameType::ResultChunk,
                    connection_id,
                    *sequence,
                    Body::ResultChunk(ResultChunk {
                        request_id: request_id.to_vec(),
                        offset: (index * RESULT_CHUNK_BYTES) as u64,
                        data: chunk.to_vec(),
                    }),
                )?;
                *sequence += 1;
            }
            write_envelope(
                writer,
                FrameType::ResultEnd,
                connection_id,
                *sequence,
                Body::ResultEnd(ResultEnd {
                    request_id: request_id.to_vec(),
                }),
            )?;
        }
        SessionTerminal::Canceled { by_user } => {
            write_envelope(
                writer,
                FrameType::CaptureCanceled,
                connection_id,
                *sequence,
                Body::CaptureCanceled(CaptureCanceled {
                    request_id: request_id.to_vec(),
                    source: if by_user {
                        CancelSource::User as i32
                    } else {
                        CancelSource::Caller as i32
                    },
                }),
            )?;
        }
        SessionTerminal::Failed { failure, retryable } => {
            let error = match failure {
                SessionFailure::PlatformUnavailable => StableError::PlatformUnavailable,
                SessionFailure::PermissionNotGranted => StableError::PermissionNotGranted,
                SessionFailure::PermissionRevoked => StableError::PermissionRevoked,
                SessionFailure::DisplayUnavailable => StableError::DisplayUnavailable,
                SessionFailure::CaptureUnavailable => StableError::CaptureUnavailable,
                SessionFailure::CaptureTimeout => StableError::CaptureTimeout,
                SessionFailure::PixelConversionFailed => StableError::PixelConversionFailed,
                SessionFailure::Internal => StableError::Internal,
            };
            write_envelope(
                writer,
                FrameType::CaptureFailed,
                connection_id,
                *sequence,
                Body::CaptureFailed(CaptureFailed {
                    request_id: request_id.to_vec(),
                    error: error as i32,
                    retryable,
                }),
            )?;
        }
    }
    Ok(())
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
    let mut envelope = envelope;
    let payload = Zeroizing::new(envelope.encode_to_vec());
    if let Some(Body::ResultChunk(chunk)) = &mut envelope.body {
        chunk.data.zeroize();
    }
    write_frame_payload(writer, frame_type, payload.as_slice()).map_err(|error| match error {
        StreamError::Wire(_) => StableError::ProtocolError,
        StreamError::Io(_) | StreamError::Eof => StableError::TransportFailed,
    })
}

fn read_envelope(
    reader: &mut FramedReader<LocalStream>,
) -> Result<(FrameType, Envelope), StableError> {
    let frame = reader.read_frame().map_err(|error| match error {
        StreamError::Eof => StableError::HostCrashed,
        StreamError::Wire(_) => StableError::ProtocolError,
        StreamError::Io(_) => StableError::TransportFailed,
    })?;
    let envelope =
        Envelope::decode(frame.payload.as_slice()).map_err(|_| StableError::ProtocolError)?;
    Ok((frame.frame_type, envelope))
}

enum ReadPollError {
    Timeout,
    Eof,
    Failure(StableError),
}

fn read_envelope_poll(
    reader: &mut FramedReader<LocalStream>,
) -> Result<(FrameType, Envelope), ReadPollError> {
    let frame = reader.read_frame().map_err(|error| match error {
        StreamError::Eof => ReadPollError::Eof,
        StreamError::Wire(_) => ReadPollError::Failure(StableError::ProtocolError),
        StreamError::Io(error)
            if matches!(
                error.kind(),
                io::ErrorKind::WouldBlock | io::ErrorKind::TimedOut
            ) =>
        {
            ReadPollError::Timeout
        }
        StreamError::Io(_) => ReadPollError::Failure(StableError::TransportFailed),
    })?;
    let envelope = Envelope::decode(frame.payload.as_slice())
        .map_err(|_| ReadPollError::Failure(StableError::ProtocolError))?;
    Ok((frame.frame_type, envelope))
}

#[cfg(feature = "tauri-runtime")]
pub fn read_bootstrap_challenge() -> Result<Option<[u8; 32]>, HostServerError> {
    if !std::env::args().any(|argument| argument == "--snaploom-bootstrap-stdio-v1") {
        return Ok(None);
    }
    let mut request = [0_u8; 36];
    io::stdin()
        .read_exact(&mut request)
        .map_err(|error| HostServerError::Io(error.kind()))?;
    if &request[..4] != b"SLBR" {
        return Err(HostServerError::Io(io::ErrorKind::InvalidData));
    }
    Ok(Some(request[4..].try_into().map_err(|_| {
        HostServerError::Io(io::ErrorKind::InvalidData)
    })?))
}

#[cfg(feature = "tauri-runtime")]
pub fn signal_bootstrap_ready(challenge: Option<[u8; 32]>) -> Result<(), HostServerError> {
    let Some(challenge) = challenge else {
        return Ok(());
    };
    let mut instance_id = [0_u8; 16];
    getrandom::fill(&mut instance_id).map_err(|_| HostServerError::Io(io::ErrorKind::Other))?;
    let mut response = Vec::with_capacity(58);
    response.extend_from_slice(b"SLRD");
    response.extend_from_slice(&challenge);
    response.extend_from_slice(&std::process::id().to_le_bytes());
    response.extend_from_slice(&1_u16.to_le_bytes());
    response.extend_from_slice(&instance_id);
    io::stdout()
        .write_all(&response)
        .and_then(|()| io::stdout().flush())
        .map_err(|error| HostServerError::Io(error.kind()))
}

#[cfg(feature = "tauri-runtime")]
pub fn verify_existing_leader(paths: &EndpointPaths) -> Result<(), HostServerError> {
    connect_authenticated(paths).map(drop).map_err(Into::into)
}
