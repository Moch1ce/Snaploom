#[cfg(unix)]
use std::fs;
#[cfg(unix)]
use std::os::unix::fs::DirBuilderExt;
#[cfg(unix)]
use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, mpsc};
use std::thread;
use std::time::Duration;

use snaploom_capture_client::ipc::{IpcClientConfig, IpcDriver};
use snaploom_capture_client::local_transport::EndpointPaths;
use snaploom_capture_client::{
    CaptureClient, CaptureLanguage as ClientLanguage, CaptureOptions, StableError, Terminal,
};
use snaploom_capture_host_lib::{HostServer, HostServerOutcome};
use snaploom_capture_protocol::CaptureOrigin;
use snaploom_capture_session::{
    CaptureLanguage as SessionLanguage, CapturePermission, CaptureRequest, GateSnapshot,
    SessionBackend, SessionControl, SessionFailure, SessionTerminal,
};

struct ScriptedBackend {
    calls: AtomicUsize,
    first_entered: mpsc::SyncSender<()>,
}

struct LanguageBackend {
    captured: mpsc::SyncSender<SessionLanguage>,
}

impl SessionBackend for LanguageBackend {
    fn run(&self, request: CaptureRequest, _control: SessionControl) -> SessionTerminal {
        self.captured.send(request.language).unwrap();
        SessionTerminal::Completed {
            png: tiny_png().into(),
            pixel_width: 1,
            pixel_height: 1,
            clipboard_written: true,
        }
    }
}

#[cfg(unix)]
static NEXT_TEST_PATH: AtomicUsize = AtomicUsize::new(1);

impl SessionBackend for ScriptedBackend {
    fn run(&self, _request: CaptureRequest, control: SessionControl) -> SessionTerminal {
        if self.calls.fetch_add(1, Ordering::SeqCst) == 0 {
            let _ = self.first_entered.send(());
            while !control.is_canceled() {
                thread::sleep(Duration::from_millis(1));
            }
            return SessionTerminal::Canceled { by_user: false };
        }
        SessionTerminal::Completed {
            png: tiny_png().into(),
            pixel_width: 1,
            pixel_height: 1,
            clipboard_written: true,
        }
    }

    fn capture_permission(&self) -> Result<CapturePermission, SessionFailure> {
        Ok(CapturePermission::NotGranted)
    }

    fn request_capture_permission(&self) -> Result<CapturePermission, SessionFailure> {
        Ok(CapturePermission::RestartRequired)
    }

    fn open_capture_permission_settings(&self) -> Result<(), SessionFailure> {
        Ok(())
    }
}

fn tiny_png() -> Vec<u8> {
    vec![
        0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, b'I', b'H', b'D', b'R', 0, 0,
        0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0, 0, 0, 0,
    ]
}

#[cfg(unix)]
fn test_paths() -> (Option<PathBuf>, EndpointPaths) {
    let nonce = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap()
        .subsec_nanos();
    let base = std::env::temp_dir().join(format!("slhe-{}-{nonce}", std::process::id()));
    let base = base.with_extension(NEXT_TEST_PATH.fetch_add(1, Ordering::Relaxed).to_string());
    let mut builder = fs::DirBuilder::new();
    builder.mode(0o700).create(&base).unwrap();
    let uid = unsafe { libc::geteuid() };
    let paths = EndpointPaths::under(&base, uid, 55, 1);
    (Some(base), paths)
}

#[cfg(windows)]
fn test_paths() -> (Option<std::path::PathBuf>, EndpointPaths) {
    (None, EndpointPaths::for_current_session(1).unwrap())
}

fn client(paths: &EndpointPaths, origin: CaptureOrigin) -> CaptureClient {
    CaptureClient::new(Arc::new(IpcDriver::new(IpcClientConfig {
        endpoint_override: Some(paths.clone()),
        origin,
        ..IpcClientConfig::default()
    })))
    .unwrap()
}

#[test]
fn real_local_transport_routes_app_and_sdk_through_one_gate() {
    let (_base, paths) = test_paths();
    let (entered_sender, entered_receiver) = mpsc::sync_channel(1);
    let backend = Arc::new(ScriptedBackend {
        calls: AtomicUsize::new(0),
        first_entered: entered_sender,
    });
    let HostServerOutcome::Leader(server) = HostServer::start_at(paths.clone(), backend).unwrap()
    else {
        panic!("isolated test endpoint must elect this process");
    };

    let sdk_client = client(&paths, CaptureOrigin::Sdk);
    let (first_sender, first_receiver) = mpsc::channel();
    let first_request = sdk_client
        .start(CaptureOptions::default(), move |terminal| {
            first_sender.send(terminal).unwrap();
        })
        .unwrap();
    entered_receiver
        .recv_timeout(Duration::from_secs(2))
        .unwrap();

    let app_client = client(&paths, CaptureOrigin::App);
    let (busy_sender, busy_receiver) = mpsc::channel();
    app_client
        .start(CaptureOptions::default(), move |terminal| {
            busy_sender.send(terminal).unwrap();
        })
        .unwrap();
    assert!(matches!(
        busy_receiver.recv_timeout(Duration::from_secs(2)).unwrap(),
        Terminal::Failed {
            error: StableError::Busy,
            ..
        }
    ));
    app_client.close().unwrap();

    sdk_client.cancel(first_request).unwrap();
    assert!(matches!(
        first_receiver.recv_timeout(Duration::from_secs(2)).unwrap(),
        Terminal::Canceled { .. }
    ));
    sdk_client.close().unwrap();

    let next_client = client(&paths, CaptureOrigin::Sdk);
    let (completed_sender, completed_receiver) = mpsc::channel();
    next_client
        .start(CaptureOptions::default(), move |terminal| {
            completed_sender.send(terminal).unwrap();
        })
        .unwrap();
    let completed = completed_receiver
        .recv_timeout(Duration::from_secs(2))
        .unwrap();
    assert!(
        matches!(
            completed,
            Terminal::Completed {
                clipboard_written: true,
                ..
            }
        ),
        "unexpected terminal after local transport reconnect: {completed:?}"
    );
    next_client.close().unwrap();
    assert_eq!(server.gate().snapshot(), GateSnapshot::Idle);

    drop(server);
    #[cfg(unix)]
    fs::remove_dir_all(_base.unwrap()).unwrap();
}

#[test]
fn authenticated_minor_one_connection_controls_host_permission_without_starting_a_session() {
    let (_base, paths) = test_paths();
    let (entered_sender, _entered_receiver) = mpsc::sync_channel(1);
    let backend = Arc::new(ScriptedBackend {
        calls: AtomicUsize::new(0),
        first_entered: entered_sender,
    });
    let HostServerOutcome::Leader(server) = HostServer::start_at(paths.clone(), backend).unwrap()
    else {
        panic!("isolated test endpoint must elect this process");
    };
    let driver = IpcDriver::new(IpcClientConfig {
        endpoint_override: Some(paths),
        origin: CaptureOrigin::App,
        ..IpcClientConfig::default()
    });

    assert_eq!(
        driver.capture_permission().unwrap(),
        snaploom_capture_client::CapturePermission::NotGranted
    );
    assert_eq!(
        driver.request_capture_permission().unwrap(),
        snaploom_capture_client::CapturePermission::RestartRequired
    );
    driver.open_capture_permission_settings().unwrap();
    assert_eq!(server.gate().snapshot(), GateSnapshot::Idle);

    drop(server);
    #[cfg(unix)]
    fs::remove_dir_all(_base.unwrap()).unwrap();
}

#[test]
fn capture_language_is_carried_by_each_authenticated_start_request() {
    let (_base, paths) = test_paths();
    let (captured_sender, captured_receiver) = mpsc::sync_channel(1);
    let backend = Arc::new(LanguageBackend {
        captured: captured_sender,
    });
    let HostServerOutcome::Leader(server) = HostServer::start_at(paths.clone(), backend).unwrap()
    else {
        panic!("isolated test endpoint must elect this process");
    };
    let driver = Arc::new(IpcDriver::new(IpcClientConfig {
        endpoint_override: Some(paths),
        origin: CaptureOrigin::App,
        language: ClientLanguage::ZhCn,
        ..IpcClientConfig::default()
    }));
    driver.set_capture_language(ClientLanguage::En);
    let capture_client = CaptureClient::new(driver).unwrap();
    let (terminal_sender, terminal_receiver) = mpsc::sync_channel(1);

    capture_client
        .start(CaptureOptions::default(), move |terminal| {
            terminal_sender.send(terminal).unwrap();
        })
        .unwrap();

    assert_eq!(
        captured_receiver
            .recv_timeout(Duration::from_secs(2))
            .unwrap(),
        SessionLanguage::En
    );
    assert!(matches!(
        terminal_receiver
            .recv_timeout(Duration::from_secs(2))
            .unwrap(),
        Terminal::Completed { .. }
    ));
    capture_client.close().unwrap();
    drop(server);
    #[cfg(unix)]
    fs::remove_dir_all(_base.unwrap()).unwrap();
}
