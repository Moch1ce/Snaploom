#![cfg(windows)]

use std::ffi::c_void;
use std::fmt;
use std::io::{self, Read, Write};
use std::ptr;
use std::slice;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_ACCESS_DENIED, ERROR_BROKEN_PIPE, ERROR_IO_PENDING, ERROR_PIPE_BUSY,
    ERROR_PIPE_CONNECTED, GetLastError, HANDLE, INVALID_HANDLE_VALUE, WAIT_OBJECT_0, WAIT_TIMEOUT,
};
use windows_sys::Win32::Security::{
    ACCESS_ALLOWED_ACE, ACL, ACL_REVISION, AddAccessAllowedAce, CreateWellKnownSid, GetLengthSid,
    GetTokenInformation, InitializeAcl, InitializeSecurityDescriptor, SECURITY_ATTRIBUTES,
    SECURITY_DESCRIPTOR, SetSecurityDescriptorDacl, TOKEN_GROUPS, TOKEN_QUERY, TOKEN_USER,
    TokenGroups, TokenUser, WinLocalSystemSid,
};
use windows_sys::Win32::Storage::FileSystem::{
    CreateFileW, FILE_ALL_ACCESS, FILE_ATTRIBUTE_NORMAL, FILE_FLAG_FIRST_PIPE_INSTANCE,
    FILE_FLAG_OVERLAPPED, FILE_READ_DATA, FILE_WRITE_DATA, OPEN_EXISTING, PIPE_ACCESS_DUPLEX,
    ReadFile, SYNCHRONIZE, WriteFile,
};
use windows_sys::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};
use windows_sys::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, GetNamedPipeClientProcessId,
    GetNamedPipeServerProcessId, PIPE_READMODE_BYTE, PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE,
    PIPE_UNLIMITED_INSTANCES, WaitNamedPipeW,
};
use windows_sys::Win32::System::Threading::{
    CreateEventW, GetCurrentProcess, OpenProcess, OpenProcessToken,
    PROCESS_QUERY_LIMITED_INFORMATION, WaitForSingleObject,
};

const SECURITY_DESCRIPTOR_REVISION: u32 = 1;
const SE_GROUP_LOGON_ID: u32 = 0xC000_0000;
const PIPE_BUFFER_BYTES: u32 = 64 * 1024;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EndpointPaths {
    pipe_name: Vec<u16>,
    expected_identity: ProcessIdentity,
}

impl EndpointPaths {
    pub fn for_current_session(protocol_major: u16) -> Result<Self, TransportSecurityError> {
        let expected_identity = current_identity()?;
        let mut hash = 0xcbf2_9ce4_8422_2325_u64;
        for byte in expected_identity
            .user_sid
            .iter()
            .chain(expected_identity.logon_sid.iter())
        {
            hash ^= u64::from(*byte);
            hash = hash.wrapping_mul(0x100_0000_01b3);
        }
        let name = format!(r"\\.\pipe\Snaploom.CaptureHost.p{protocol_major}.{hash:016x}");
        Ok(Self {
            pipe_name: name.encode_utf16().chain([0]).collect(),
            expected_identity,
        })
    }

    #[must_use]
    pub fn pipe_name(&self) -> &[u16] {
        &self.pipe_name
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
    paths: EndpointPaths,
    pending: Option<LocalStream>,
}

impl UnixLeader {
    pub fn try_bind(paths: &EndpointPaths) -> Result<LeaderOutcome, TransportSecurityError> {
        match create_server_instance(paths, true) {
            Ok(pending) => Ok(LeaderOutcome::Leader(Self {
                paths: paths.clone(),
                pending: Some(pending),
            })),
            Err(TransportSecurityError::Io(io::ErrorKind::PermissionDenied)) => {
                Ok(LeaderOutcome::Existing)
            }
            Err(error) => Err(error),
        }
    }

    pub fn accept_authenticated(&mut self) -> Result<LocalStream, TransportSecurityError> {
        let stream = self
            .pending
            .take()
            .ok_or(TransportSecurityError::Io(io::ErrorKind::ResourceBusy))?;
        if let Err(error) = connect_overlapped(stream.handle.raw()) {
            self.pending = Some(stream);
            return Err(error);
        }
        self.pending = Some(create_server_instance(&self.paths, false)?);
        authenticate_client(&stream, &self.paths.expected_identity)?;
        Ok(stream)
    }

    pub fn set_nonblocking(&self, _nonblocking: bool) -> io::Result<()> {
        Ok(())
    }

    #[must_use]
    pub fn paths(&self) -> &EndpointPaths {
        &self.paths
    }
}

struct PipeHandle {
    raw: HANDLE,
    server: bool,
}

unsafe impl Send for PipeHandle {}
unsafe impl Sync for PipeHandle {}

impl PipeHandle {
    fn raw(&self) -> HANDLE {
        self.raw
    }
}

impl Drop for PipeHandle {
    fn drop(&mut self) {
        if self.server {
            unsafe { DisconnectNamedPipe(self.raw) };
        }
        unsafe { CloseHandle(self.raw) };
    }
}

#[derive(Clone)]
pub struct LocalStream {
    handle: Arc<PipeHandle>,
    read_timeout: Arc<Mutex<Option<Duration>>>,
    write_timeout: Arc<Mutex<Option<Duration>>>,
}

impl LocalStream {
    pub fn try_clone(&self) -> io::Result<Self> {
        Ok(self.clone())
    }

    pub fn set_read_timeout(&self, timeout: Option<Duration>) -> io::Result<()> {
        *self
            .read_timeout
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = timeout;
        Ok(())
    }

    pub fn set_write_timeout(&self, timeout: Option<Duration>) -> io::Result<()> {
        *self
            .write_timeout
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = timeout;
        Ok(())
    }
}

impl Read for LocalStream {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        if buffer.is_empty() {
            return Ok(0);
        }
        let timeout = *self
            .read_timeout
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let count = buffer.len().min(u32::MAX as usize) as u32;
        let result = overlapped_operation(
            self.handle.raw(),
            timeout,
            |overlapped, transferred| unsafe {
                ReadFile(
                    self.handle.raw(),
                    buffer.as_mut_ptr(),
                    count,
                    transferred,
                    overlapped,
                )
            },
        );
        match result {
            Err(error) if error.raw_os_error() == Some(ERROR_BROKEN_PIPE as i32) => Ok(0),
            result => result.map(|count| count as usize),
        }
    }
}

impl Write for LocalStream {
    fn write(&mut self, buffer: &[u8]) -> io::Result<usize> {
        if buffer.is_empty() {
            return Ok(0);
        }
        let timeout = *self
            .write_timeout
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let count = buffer.len().min(u32::MAX as usize) as u32;
        overlapped_operation(
            self.handle.raw(),
            timeout,
            |overlapped, transferred| unsafe {
                WriteFile(
                    self.handle.raw(),
                    buffer.as_ptr(),
                    count,
                    transferred,
                    overlapped,
                )
            },
        )
        .map(|count| count as usize)
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

fn connect_overlapped(handle: HANDLE) -> Result<(), TransportSecurityError> {
    let event = unsafe { CreateEventW(ptr::null(), 1, 0, ptr::null()) };
    if event.is_null() {
        return Err(last_error(unsafe { GetLastError() }));
    }
    let mut overlapped = OVERLAPPED {
        hEvent: event,
        ..OVERLAPPED::default()
    };
    let connected = unsafe { ConnectNamedPipe(handle, &raw mut overlapped) };
    if connected != 0 {
        unsafe { CloseHandle(event) };
        return Ok(());
    }
    let error = unsafe { GetLastError() };
    if error == ERROR_PIPE_CONNECTED {
        unsafe { CloseHandle(event) };
        return Ok(());
    }
    if error != ERROR_IO_PENDING {
        unsafe { CloseHandle(event) };
        return Err(last_error(error));
    }
    let wait = unsafe { WaitForSingleObject(event, 0) };
    if wait == WAIT_TIMEOUT {
        unsafe {
            CancelIoEx(handle, &raw const overlapped);
            WaitForSingleObject(event, u32::MAX);
            CloseHandle(event);
        }
        return Err(TransportSecurityError::Io(io::ErrorKind::WouldBlock));
    }
    let result = if wait == WAIT_OBJECT_0 {
        let mut transferred = 0;
        if unsafe { GetOverlappedResult(handle, &overlapped, &mut transferred, 0) } != 0 {
            Ok(())
        } else {
            Err(last_error(unsafe { GetLastError() }))
        }
    } else {
        Err(TransportSecurityError::Io(io::ErrorKind::Other))
    };
    unsafe { CloseHandle(event) };
    result
}

fn overlapped_operation(
    handle: HANDLE,
    timeout: Option<Duration>,
    operation: impl FnOnce(*mut OVERLAPPED, *mut u32) -> i32,
) -> io::Result<u32> {
    let event = unsafe { CreateEventW(ptr::null(), 1, 0, ptr::null()) };
    if event.is_null() {
        return Err(io::Error::last_os_error());
    }
    let mut overlapped = OVERLAPPED {
        hEvent: event,
        ..OVERLAPPED::default()
    };
    let mut transferred = 0;
    let started = operation(&raw mut overlapped, &raw mut transferred);
    if started != 0 {
        unsafe { CloseHandle(event) };
        return Ok(transferred);
    }
    let error = unsafe { GetLastError() };
    if error != ERROR_IO_PENDING {
        unsafe { CloseHandle(event) };
        return Err(io::Error::from_raw_os_error(error as i32));
    }
    let timeout_ms = timeout.map_or(u32::MAX, |duration| {
        duration.as_millis().min(u128::from(u32::MAX - 1)) as u32
    });
    let wait = unsafe { WaitForSingleObject(event, timeout_ms) };
    if wait == WAIT_TIMEOUT {
        unsafe {
            CancelIoEx(handle, &raw const overlapped);
            WaitForSingleObject(event, u32::MAX);
            CloseHandle(event);
        }
        return Err(io::Error::new(
            io::ErrorKind::TimedOut,
            "named pipe operation timed out",
        ));
    }
    let result = if wait == WAIT_OBJECT_0
        && unsafe { GetOverlappedResult(handle, &overlapped, &mut transferred, 0) } != 0
    {
        Ok(transferred)
    } else {
        Err(io::Error::last_os_error())
    };
    unsafe { CloseHandle(event) };
    result
}

pub fn connect_authenticated(paths: &EndpointPaths) -> Result<LocalStream, TransportSecurityError> {
    let waited = unsafe { WaitNamedPipeW(paths.pipe_name.as_ptr(), 50) };
    if waited == 0 {
        let error = unsafe { GetLastError() };
        return Err(last_error(error));
    }
    let handle = unsafe {
        CreateFileW(
            paths.pipe_name.as_ptr(),
            FILE_READ_DATA | FILE_WRITE_DATA | SYNCHRONIZE,
            0,
            ptr::null(),
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED,
            ptr::null_mut(),
        )
    };
    if handle == INVALID_HANDLE_VALUE {
        return Err(last_error(unsafe { GetLastError() }));
    }
    let stream = stream(handle, false);
    authenticate_server(&stream, &paths.expected_identity)?;
    Ok(stream)
}

fn create_server_instance(
    paths: &EndpointPaths,
    first: bool,
) -> Result<LocalStream, TransportSecurityError> {
    let security = PipeSecurity::new(&paths.expected_identity)?;
    let open_mode = PIPE_ACCESS_DUPLEX
        | FILE_FLAG_OVERLAPPED
        | if first {
            FILE_FLAG_FIRST_PIPE_INSTANCE
        } else {
            0
        };
    let handle = unsafe {
        CreateNamedPipeW(
            paths.pipe_name.as_ptr(),
            open_mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_REJECT_REMOTE_CLIENTS,
            PIPE_UNLIMITED_INSTANCES,
            PIPE_BUFFER_BYTES,
            PIPE_BUFFER_BYTES,
            0,
            &security.attributes,
        )
    };
    if handle == INVALID_HANDLE_VALUE {
        let error = unsafe { GetLastError() };
        return if first && (error == ERROR_ACCESS_DENIED || error == ERROR_PIPE_BUSY) {
            Err(TransportSecurityError::Io(io::ErrorKind::PermissionDenied))
        } else {
            Err(last_error(error))
        };
    }
    Ok(stream(handle, true))
}

fn stream(handle: HANDLE, server: bool) -> LocalStream {
    LocalStream {
        handle: Arc::new(PipeHandle {
            raw: handle,
            server,
        }),
        read_timeout: Arc::new(Mutex::new(None)),
        write_timeout: Arc::new(Mutex::new(None)),
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct ProcessIdentity {
    user_sid: Vec<u8>,
    logon_sid: Vec<u8>,
}

fn current_identity() -> Result<ProcessIdentity, TransportSecurityError> {
    identity_for_process(unsafe { GetCurrentProcess() }, false)
}

fn identity_for_pid(pid: u32) -> Result<ProcessIdentity, TransportSecurityError> {
    let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
    if process.is_null() {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    let result = identity_for_process(process, true);
    unsafe { CloseHandle(process) };
    result
}

fn identity_for_process(
    process: HANDLE,
    _owned_process: bool,
) -> Result<ProcessIdentity, TransportSecurityError> {
    let mut token = ptr::null_mut();
    if unsafe { OpenProcessToken(process, TOKEN_QUERY, &mut token) } == 0 {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    let result = (|| {
        let user = token_information(token, TokenUser)?;
        let user = unsafe { &*(user.as_ptr().cast::<TOKEN_USER>()) };
        let user_sid = copy_sid(user.User.Sid)?;

        let groups = token_information(token, TokenGroups)?;
        let groups = unsafe { &*(groups.as_ptr().cast::<TOKEN_GROUPS>()) };
        let group_slice =
            unsafe { slice::from_raw_parts(groups.Groups.as_ptr(), groups.GroupCount as usize) };
        let logon = group_slice
            .iter()
            .find(|group| group.Attributes & SE_GROUP_LOGON_ID == SE_GROUP_LOGON_ID)
            .ok_or(TransportSecurityError::PeerAuthentication)?;
        Ok(ProcessIdentity {
            user_sid,
            logon_sid: copy_sid(logon.Sid)?,
        })
    })();
    unsafe { CloseHandle(token) };
    result
}

fn token_information(token: HANDLE, class: i32) -> Result<Vec<usize>, TransportSecurityError> {
    let mut length = 0;
    unsafe { GetTokenInformation(token, class, ptr::null_mut(), 0, &mut length) };
    if length == 0 {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    let words = (length as usize).div_ceil(std::mem::size_of::<usize>());
    let mut buffer = vec![0_usize; words];
    if unsafe {
        GetTokenInformation(
            token,
            class,
            buffer.as_mut_ptr().cast(),
            length,
            &mut length,
        )
    } == 0
    {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    Ok(buffer)
}

fn copy_sid(sid: *mut c_void) -> Result<Vec<u8>, TransportSecurityError> {
    let length = unsafe { GetLengthSid(sid) } as usize;
    if length == 0 {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    Ok(unsafe { slice::from_raw_parts(sid.cast::<u8>(), length) }.to_vec())
}

fn authenticate_client(
    stream: &LocalStream,
    expected: &ProcessIdentity,
) -> Result<(), TransportSecurityError> {
    let mut pid = 0;
    if unsafe { GetNamedPipeClientProcessId(stream.handle.raw(), &mut pid) } == 0 {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    verify_identity(expected, &identity_for_pid(pid)?)
}

fn authenticate_server(
    stream: &LocalStream,
    expected: &ProcessIdentity,
) -> Result<(), TransportSecurityError> {
    let mut pid = 0;
    if unsafe { GetNamedPipeServerProcessId(stream.handle.raw(), &mut pid) } == 0 {
        return Err(TransportSecurityError::PeerAuthentication);
    }
    verify_identity(expected, &identity_for_pid(pid)?)
}

fn verify_identity(
    expected: &ProcessIdentity,
    actual: &ProcessIdentity,
) -> Result<(), TransportSecurityError> {
    if expected == actual {
        Ok(())
    } else {
        Err(TransportSecurityError::PeerAuthentication)
    }
}

struct PipeSecurity {
    _descriptor: Box<SECURITY_DESCRIPTOR>,
    _acl: Vec<usize>,
    attributes: SECURITY_ATTRIBUTES,
}

impl PipeSecurity {
    fn new(identity: &ProcessIdentity) -> Result<Self, TransportSecurityError> {
        let mut system_sid = vec![0_u8; 68];
        let mut system_sid_length = system_sid.len() as u32;
        if unsafe {
            CreateWellKnownSid(
                WinLocalSystemSid,
                ptr::null_mut(),
                system_sid.as_mut_ptr().cast(),
                &mut system_sid_length,
            )
        } == 0
        {
            return Err(TransportSecurityError::PeerAuthentication);
        }
        system_sid.truncate(system_sid_length as usize);
        let sids = [
            identity.user_sid.as_slice(),
            identity.logon_sid.as_slice(),
            system_sid.as_slice(),
        ];
        let acl_bytes = std::mem::size_of::<ACL>()
            + sids
                .iter()
                .map(|sid| std::mem::size_of::<ACCESS_ALLOWED_ACE>() - 4 + sid.len())
                .sum::<usize>();
        let mut acl = vec![0_usize; acl_bytes.div_ceil(std::mem::size_of::<usize>())];
        let acl_ptr = acl.as_mut_ptr().cast::<ACL>();
        if unsafe { InitializeAcl(acl_ptr, acl_bytes as u32, ACL_REVISION) } == 0 {
            return Err(TransportSecurityError::PeerAuthentication);
        }
        for sid in sids {
            if unsafe {
                AddAccessAllowedAce(
                    acl_ptr,
                    ACL_REVISION,
                    FILE_ALL_ACCESS,
                    sid.as_ptr().cast_mut().cast(),
                )
            } == 0
            {
                return Err(TransportSecurityError::PeerAuthentication);
            }
        }
        let mut descriptor = Box::<SECURITY_DESCRIPTOR>::default();
        if unsafe {
            InitializeSecurityDescriptor(
                (&raw mut *descriptor).cast(),
                SECURITY_DESCRIPTOR_REVISION,
            )
        } == 0
            || unsafe { SetSecurityDescriptorDacl((&raw mut *descriptor).cast(), 1, acl_ptr, 0) }
                == 0
        {
            return Err(TransportSecurityError::PeerAuthentication);
        }
        let attributes = SECURITY_ATTRIBUTES {
            nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: (&raw mut *descriptor).cast(),
            bInheritHandle: 0,
        };
        Ok(Self {
            _descriptor: descriptor,
            _acl: acl,
            attributes,
        })
    }
}

fn last_error(error: u32) -> TransportSecurityError {
    TransportSecurityError::Io(io::Error::from_raw_os_error(error as i32).kind())
}
