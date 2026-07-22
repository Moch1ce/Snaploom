#![cfg(unix)]

use std::fs;
use std::os::fd::AsRawFd;
use std::os::unix::fs::{DirBuilderExt, MetadataExt, PermissionsExt};
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::thread;
use std::time::{Duration, Instant};

use snaploom_capture_client::local_transport::{
    EndpointPaths, LeaderOutcome, TransportSecurityError, UnixLeader, connect_authenticated,
    verify_peer_uid,
};

static NEXT_DIRECTORY: AtomicU64 = AtomicU64::new(1);

fn test_base() -> PathBuf {
    let path = std::env::temp_dir().join(format!(
        "snaploom-ipc-test-{}-{}",
        std::process::id(),
        NEXT_DIRECTORY.fetch_add(1, Ordering::Relaxed)
    ));
    let mut builder = fs::DirBuilder::new();
    builder.mode(0o700).create(&path).unwrap();
    path
}

#[test]
fn unix_endpoint_is_private_and_authenticates_both_peers() {
    let base = test_base();
    let uid = unsafe { libc::geteuid() };
    let paths = EndpointPaths::under(&base, uid, 42, 1);
    let LeaderOutcome::Leader(mut leader) = UnixLeader::try_bind(&paths).unwrap() else {
        panic!("first endpoint owner must become leader");
    };
    assert_eq!(
        fs::metadata(&paths.directory).unwrap().mode() & 0o777,
        0o700
    );
    assert_eq!(fs::metadata(&paths.socket).unwrap().mode() & 0o777, 0o600);

    let client_paths = paths.clone();
    let client = thread::spawn(move || connect_authenticated(&client_paths).unwrap());
    let server_stream = leader.accept_authenticated().unwrap();
    let client_stream = client.join().unwrap();
    drop((server_stream, client_stream, leader));
    fs::remove_dir_all(base).unwrap();
}

#[test]
fn authenticated_stream_is_blocking_when_listener_is_nonblocking() {
    let base = test_base();
    let uid = unsafe { libc::geteuid() };
    let paths = EndpointPaths::under(&base, uid, 45, 1);
    let LeaderOutcome::Leader(mut leader) = UnixLeader::try_bind(&paths).unwrap() else {
        panic!("first endpoint owner must become leader");
    };
    leader.set_nonblocking(true).unwrap();

    let client_paths = paths.clone();
    let client = thread::spawn(move || connect_authenticated(&client_paths).unwrap());
    let accept_deadline = Instant::now() + Duration::from_secs(2);
    let server_stream = loop {
        match leader.accept_authenticated() {
            Ok(stream) => break stream,
            Err(TransportSecurityError::Io(std::io::ErrorKind::WouldBlock))
                if Instant::now() < accept_deadline =>
            {
                thread::yield_now();
            }
            Err(TransportSecurityError::Io(std::io::ErrorKind::WouldBlock)) => {
                if let Err(panic) = client.join() {
                    std::panic::resume_unwind(panic);
                }
                panic!("timed out waiting for the authenticated connection");
            }
            Err(error) => panic!("accept failed: {error}"),
        }
    };
    let flags = unsafe { libc::fcntl(server_stream.as_raw_fd(), libc::F_GETFL) };
    assert_ne!(flags, -1);
    assert_eq!(flags & libc::O_NONBLOCK, 0);

    let client_stream = client.join().unwrap();
    drop((server_stream, client_stream, leader));
    fs::remove_dir_all(base).unwrap();
}

#[test]
fn endpoint_fails_closed_for_insecure_directory_and_symlink_lock() {
    let base = test_base();
    let uid = unsafe { libc::geteuid() };
    let paths = EndpointPaths::under(&base, uid, 43, 1);
    fs::create_dir(&paths.directory).unwrap();
    fs::set_permissions(&paths.directory, fs::Permissions::from_mode(0o755)).unwrap();
    assert!(matches!(
        UnixLeader::try_bind(&paths),
        Err(TransportSecurityError::InsecureMode)
    ));
    fs::set_permissions(&paths.directory, fs::Permissions::from_mode(0o700)).unwrap();
    std::os::unix::fs::symlink("missing", &paths.leader_lock).unwrap();
    assert!(matches!(
        UnixLeader::try_bind(&paths),
        Err(TransportSecurityError::Symlink)
    ));
    fs::remove_dir_all(base).unwrap();
}

#[test]
fn wrong_peer_identity_fails_closed() {
    assert_eq!(verify_peer_uid(501, 501), Ok(()));
    assert_eq!(
        verify_peer_uid(501, 502),
        Err(TransportSecurityError::PeerAuthentication)
    );
}

#[test]
fn twenty_processes_elect_exactly_one_endpoint_leader() {
    const CHILD_BASE: &str = "SNAPLOOM_LEADER_TEST_BASE";
    if let Some(base) = std::env::var_os(CHILD_BASE) {
        let uid = unsafe { libc::geteuid() };
        let paths = EndpointPaths::under(&PathBuf::from(base), uid, 44, 1);
        match UnixLeader::try_bind(&paths).unwrap() {
            LeaderOutcome::Leader(_leader) => {
                println!("SNAPLOOM_ROLE_LEADER");
                thread::sleep(std::time::Duration::from_millis(750));
            }
            LeaderOutcome::Existing => println!("SNAPLOOM_ROLE_EXISTING"),
        }
        return;
    }

    let base = test_base();
    let executable = std::env::current_exe().unwrap();
    let children = (0..20)
        .map(|_| {
            std::process::Command::new(&executable)
                .args([
                    "--exact",
                    "twenty_processes_elect_exactly_one_endpoint_leader",
                    "--nocapture",
                ])
                .env(CHILD_BASE, &base)
                .stdout(std::process::Stdio::piped())
                .spawn()
                .unwrap()
        })
        .collect::<Vec<_>>();
    let outputs = children
        .into_iter()
        .map(|child| child.wait_with_output().unwrap())
        .collect::<Vec<_>>();
    assert!(outputs.iter().all(|output| output.status.success()));
    assert_eq!(
        outputs
            .iter()
            .filter(|output| {
                String::from_utf8_lossy(&output.stdout).contains("SNAPLOOM_ROLE_LEADER")
            })
            .count(),
        1
    );
    fs::remove_dir_all(base).unwrap();
}
