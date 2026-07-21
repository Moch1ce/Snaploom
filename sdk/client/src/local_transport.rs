#![cfg(unix)]

use std::fmt;
use std::fs::{self, File, OpenOptions};
use std::io;
use std::os::fd::AsRawFd;
use std::os::unix::fs::{DirBuilderExt, FileTypeExt, MetadataExt, OpenOptionsExt, PermissionsExt};
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::{Path, PathBuf};

pub type LocalStream = UnixStream;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EndpointPaths {
    pub directory: PathBuf,
    pub socket: PathBuf,
    pub leader_lock: PathBuf,
    expected_uid: u32,
}

impl EndpointPaths {
    pub fn for_current_session(protocol_major: u16) -> Result<Self, TransportSecurityError> {
        let uid = unsafe { libc::geteuid() };
        let security_session = current_security_session()?;
        Ok(Self::under(
            Path::new("/tmp"),
            uid,
            security_session,
            protocol_major,
        ))
    }

    #[doc(hidden)]
    #[must_use]
    pub fn under(base: &Path, uid: u32, security_session: u32, protocol_major: u16) -> Self {
        let directory = base.join(format!("slcap-{uid}-{security_session}-p{protocol_major}"));
        Self {
            socket: directory.join("host.sock"),
            leader_lock: directory.join("leader.lock"),
            directory,
            expected_uid: uid,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TransportSecurityError {
    Io(io::ErrorKind),
    WrongOwner,
    InsecureMode,
    UnexpectedFileType,
    Symlink,
    PathTooLong,
    PeerAuthentication,
}

impl fmt::Display for TransportSecurityError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{self:?}")
    }
}

impl std::error::Error for TransportSecurityError {}

impl From<io::Error> for TransportSecurityError {
    fn from(error: io::Error) -> Self {
        Self::Io(error.kind())
    }
}

pub enum LeaderOutcome {
    Leader(UnixLeader),
    Existing,
}

pub struct UnixLeader {
    listener: UnixListener,
    _lock: File,
    paths: EndpointPaths,
}

impl UnixLeader {
    pub fn try_bind(paths: &EndpointPaths) -> Result<LeaderOutcome, TransportSecurityError> {
        ensure_private_directory(paths)?;
        validate_socket_path(&paths.socket)?;
        let lock = open_and_validate_lock(paths)?;
        let lock_result = unsafe { libc::flock(lock.as_raw_fd(), libc::LOCK_EX | libc::LOCK_NB) };
        if lock_result != 0 {
            return if io::Error::last_os_error().kind() == io::ErrorKind::WouldBlock {
                Ok(LeaderOutcome::Existing)
            } else {
                Err(io::Error::last_os_error().into())
            };
        }
        clean_stale_socket(paths)?;
        let listener = UnixListener::bind(&paths.socket)?;
        fs::set_permissions(&paths.socket, fs::Permissions::from_mode(0o600))?;
        verify_socket(paths)?;
        Ok(LeaderOutcome::Leader(Self {
            listener,
            _lock: lock,
            paths: paths.clone(),
        }))
    }

    pub fn accept_authenticated(&mut self) -> Result<UnixStream, TransportSecurityError> {
        let (stream, _) = self.listener.accept()?;
        authenticate_peer(&stream, self.paths.expected_uid)?;
        Ok(stream)
    }

    pub fn set_nonblocking(&self, nonblocking: bool) -> io::Result<()> {
        self.listener.set_nonblocking(nonblocking)
    }

    #[must_use]
    pub fn paths(&self) -> &EndpointPaths {
        &self.paths
    }
}

impl Drop for UnixLeader {
    fn drop(&mut self) {
        if let Ok(metadata) = fs::symlink_metadata(&self.paths.socket)
            && metadata.file_type().is_socket()
            && metadata.uid() == self.paths.expected_uid
        {
            let _ = fs::remove_file(&self.paths.socket);
        }
    }
}

pub fn connect_authenticated(paths: &EndpointPaths) -> Result<UnixStream, TransportSecurityError> {
    verify_private_directory(paths)?;
    verify_socket(paths)?;
    let stream = UnixStream::connect(&paths.socket)?;
    authenticate_peer(&stream, paths.expected_uid)?;
    Ok(stream)
}

fn ensure_private_directory(paths: &EndpointPaths) -> Result<(), TransportSecurityError> {
    match fs::symlink_metadata(&paths.directory) {
        Ok(_) => verify_private_directory(paths),
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            let mut builder = fs::DirBuilder::new();
            match builder.mode(0o700).create(&paths.directory) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
                Err(error) => return Err(error.into()),
            }
            verify_private_directory(paths)
        }
        Err(error) => Err(error.into()),
    }
}

fn verify_private_directory(paths: &EndpointPaths) -> Result<(), TransportSecurityError> {
    let metadata = fs::symlink_metadata(&paths.directory)?;
    if metadata.file_type().is_symlink() {
        return Err(TransportSecurityError::Symlink);
    }
    if !metadata.is_dir() {
        return Err(TransportSecurityError::UnexpectedFileType);
    }
    if metadata.uid() != paths.expected_uid {
        return Err(TransportSecurityError::WrongOwner);
    }
    if metadata.mode() & 0o077 != 0 {
        return Err(TransportSecurityError::InsecureMode);
    }
    Ok(())
}

fn open_and_validate_lock(paths: &EndpointPaths) -> Result<File, TransportSecurityError> {
    let lock = OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .mode(0o600)
        .custom_flags(libc::O_CLOEXEC | libc::O_NOFOLLOW)
        .open(&paths.leader_lock)
        .map_err(|error| {
            if error.raw_os_error() == Some(libc::ELOOP) {
                TransportSecurityError::Symlink
            } else {
                error.into()
            }
        })?;
    let metadata = lock.metadata()?;
    if !metadata.is_file() {
        return Err(TransportSecurityError::UnexpectedFileType);
    }
    if metadata.uid() != paths.expected_uid {
        return Err(TransportSecurityError::WrongOwner);
    }
    if metadata.mode() & 0o077 != 0 {
        return Err(TransportSecurityError::InsecureMode);
    }
    Ok(lock)
}

fn clean_stale_socket(paths: &EndpointPaths) -> Result<(), TransportSecurityError> {
    let metadata = match fs::symlink_metadata(&paths.socket) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error.into()),
    };
    validate_socket_metadata(&metadata, paths.expected_uid)?;
    if UnixStream::connect(&paths.socket).is_ok() {
        return Err(TransportSecurityError::Io(io::ErrorKind::AddrInUse));
    }
    fs::remove_file(&paths.socket)?;
    Ok(())
}

fn verify_socket(paths: &EndpointPaths) -> Result<(), TransportSecurityError> {
    let metadata = fs::symlink_metadata(&paths.socket)?;
    validate_socket_metadata(&metadata, paths.expected_uid)
}

fn validate_socket_metadata(
    metadata: &fs::Metadata,
    expected_uid: u32,
) -> Result<(), TransportSecurityError> {
    if metadata.file_type().is_symlink() {
        return Err(TransportSecurityError::Symlink);
    }
    if !metadata.file_type().is_socket() {
        return Err(TransportSecurityError::UnexpectedFileType);
    }
    if metadata.uid() != expected_uid {
        return Err(TransportSecurityError::WrongOwner);
    }
    if metadata.mode() & 0o077 != 0 {
        return Err(TransportSecurityError::InsecureMode);
    }
    Ok(())
}

fn validate_socket_path(path: &Path) -> Result<(), TransportSecurityError> {
    let bytes = path.as_os_str().as_encoded_bytes();
    if bytes.contains(&0) || bytes.len() >= 104 {
        return Err(TransportSecurityError::PathTooLong);
    }
    Ok(())
}

fn authenticate_peer(stream: &UnixStream, expected_uid: u32) -> Result<(), TransportSecurityError> {
    let peer_uid = peer_uid(stream)?;
    verify_peer_uid(expected_uid, peer_uid)
}

#[doc(hidden)]
pub fn verify_peer_uid(expected_uid: u32, actual_uid: u32) -> Result<(), TransportSecurityError> {
    if actual_uid == expected_uid {
        Ok(())
    } else {
        Err(TransportSecurityError::PeerAuthentication)
    }
}

#[cfg(target_os = "macos")]
fn peer_uid(stream: &UnixStream) -> Result<u32, TransportSecurityError> {
    let mut uid = 0;
    let mut gid = 0;
    let result = unsafe { libc::getpeereid(stream.as_raw_fd(), &mut uid, &mut gid) };
    if result == 0 {
        Ok(uid)
    } else {
        Err(TransportSecurityError::PeerAuthentication)
    }
}

#[cfg(not(target_os = "macos"))]
fn peer_uid(stream: &UnixStream) -> Result<u32, TransportSecurityError> {
    let mut credentials = libc::ucred {
        pid: 0,
        uid: 0,
        gid: 0,
    };
    let mut length = std::mem::size_of::<libc::ucred>() as libc::socklen_t;
    let result = unsafe {
        libc::getsockopt(
            stream.as_raw_fd(),
            libc::SOL_SOCKET,
            libc::SO_PEERCRED,
            (&raw mut credentials).cast(),
            &raw mut length,
        )
    };
    if result == 0 {
        Ok(credentials.uid)
    } else {
        Err(TransportSecurityError::PeerAuthentication)
    }
}

#[cfg(target_os = "macos")]
fn current_security_session() -> Result<u32, TransportSecurityError> {
    #[link(name = "Security", kind = "framework")]
    unsafe extern "C" {
        fn SessionGetInfo(session: u32, session_id: *mut u32, attributes: *mut u32) -> i32;
    }
    let mut session_id = 0;
    let mut attributes = 0;
    let status = unsafe { SessionGetInfo(0, &mut session_id, &mut attributes) };
    if status == 0 {
        Ok(session_id)
    } else {
        Err(TransportSecurityError::PeerAuthentication)
    }
}

#[cfg(not(target_os = "macos"))]
fn current_security_session() -> Result<u32, TransportSecurityError> {
    Ok(0)
}
