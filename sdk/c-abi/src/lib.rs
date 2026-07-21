use std::collections::HashMap;
use std::ffi::{c_char, c_void};
use std::path::Path;
use std::path::PathBuf;
use std::ptr;
use std::slice;
use std::str;
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

#[cfg(not(any(unix, windows)))]
use snaploom_capture_client::UnavailableDriver;
use snaploom_capture_client::{
    CaptureClient, CaptureDriver, CaptureOptions, ClientStatus, StableError, Terminal,
};
use zeroize::Zeroize;

#[cfg(any(unix, windows))]
use snaploom_capture_client::ipc::{IpcClientConfig, IpcDriver};

const STATUS_OK: u32 = 0;
const STATUS_INVALID_ARGUMENT: u32 = 1;
const STATUS_INVALID_STRUCT_SIZE: u32 = 2;
const STATUS_CLIENT_CLOSED: u32 = 3;
const STATUS_CALLBACK_CONTEXT: u32 = 4;
const STATUS_NOT_FOUND: u32 = 5;
const STATUS_OUT_OF_MEMORY: u32 = 6;
const STATUS_INTERNAL: u32 = 255;

const ABI_MAJOR: u32 = 1;
const FLAG_DISABLE_CLIPBOARD: u32 = 1;
const COMPLETION_COMPLETED: u32 = 1;
const COMPLETION_CANCELED: u32 = 2;
const COMPLETION_FAILED: u32 = 3;
const COMPLETION_FLAG_CLIPBOARD_WRITTEN: u32 = 1;
const COMPLETION_FLAG_RETRYABLE: u32 = 2;
const SDK_SEMVER: &[u8] = env!("CARGO_PKG_VERSION").as_bytes();

#[repr(C)]
pub struct SnaploomCaptureClientV1 {
    _private: [u8; 0],
}

#[derive(Clone, Copy)]
#[repr(C)]
pub struct SnaploomUtf8ViewV1 {
    pub data: *const u8,
    pub length: u64,
}

#[derive(Clone, Copy)]
#[repr(C)]
pub struct SnaploomCaptureVersionInfoV1 {
    pub struct_size: u32,
    pub abi_major: u32,
    pub sdk_semver: SnaploomUtf8ViewV1,
    pub reserved: [u64; 4],
}

#[derive(Clone, Copy)]
#[repr(C)]
pub struct SnaploomCaptureClientConfigV1 {
    pub struct_size: u32,
    pub flags: u32,
    pub host_executable_override: SnaploomUtf8ViewV1,
    pub launch_timeout_ms: u32,
    pub handshake_timeout_ms: u32,
    pub reserved: [u64; 4],
}

#[derive(Clone, Copy)]
#[repr(C)]
pub struct SnaploomCaptureOptionsV1 {
    pub struct_size: u32,
    pub flags: u32,
    pub interaction_timeout_ms: u64,
    pub reserved: [u64; 4],
}

#[repr(C)]
pub struct SnaploomCaptureCompletionV1 {
    pub struct_size: u32,
    pub kind: u32,
    pub error_code: u32,
    pub flags: u32,
    pub request_id: u64,
    pub png_data: *const u8,
    pub png_size: u64,
    pub pixel_width: u32,
    pub pixel_height: u32,
    pub reserved: [u64; 4],
}

pub type SnaploomCaptureCallbackV1 = unsafe extern "C" fn(
    client: *mut SnaploomCaptureClientV1,
    completion: *mut SnaploomCaptureCompletionV1,
    user_data: *mut c_void,
);

#[repr(C)]
struct OwnedCompletion {
    public: SnaploomCaptureCompletionV1,
    png: Vec<u8>,
}

type ClientRegistry = Mutex<HashMap<usize, Arc<CaptureClient>>>;

fn clients() -> &'static ClientRegistry {
    static CLIENTS: OnceLock<ClientRegistry> = OnceLock::new();
    CLIENTS.get_or_init(|| Mutex::new(HashMap::new()))
}

#[cfg(debug_assertions)]
fn test_driver() -> &'static Mutex<Option<Arc<dyn CaptureDriver>>> {
    static DRIVER: OnceLock<Mutex<Option<Arc<dyn CaptureDriver>>>> = OnceLock::new();
    DRIVER.get_or_init(|| Mutex::new(None))
}

#[doc(hidden)]
#[cfg(debug_assertions)]
pub fn install_test_driver(driver: Arc<dyn CaptureDriver>) {
    *test_driver()
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = Some(driver);
}

struct DriverConfiguration {
    host_executable_override: Option<PathBuf>,
    launch_timeout: Duration,
    handshake_timeout: Duration,
}

fn driver_for_new_client(config: DriverConfiguration) -> Arc<dyn CaptureDriver> {
    #[cfg(debug_assertions)]
    if let Some(driver) = test_driver()
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .clone()
    {
        return driver;
    }
    #[cfg(any(unix, windows))]
    {
        Arc::new(IpcDriver::new(IpcClientConfig {
            host_executable_override: config.host_executable_override,
            endpoint_override: None,
            launch_timeout: config.launch_timeout,
            handshake_timeout: config.handshake_timeout,
            origin: snaploom_capture_protocol::CaptureOrigin::Sdk,
            language: snaploom_capture_client::CaptureLanguage::System,
        }))
    }
    #[cfg(not(any(unix, windows)))]
    {
        let _ = config;
        Arc::new(UnavailableDriver)
    }
}

fn status_from_client(status: ClientStatus) -> u32 {
    match status {
        ClientStatus::InvalidArgument => STATUS_INVALID_ARGUMENT,
        ClientStatus::InvalidStructSize => STATUS_INVALID_STRUCT_SIZE,
        ClientStatus::ClientClosed => STATUS_CLIENT_CLOSED,
        ClientStatus::CallbackContext => STATUS_CALLBACK_CONTEXT,
        ClientStatus::NotFound => STATUS_NOT_FOUND,
        ClientStatus::OutOfMemory => STATUS_OUT_OF_MEMORY,
        ClientStatus::Internal => STATUS_INTERNAL,
    }
}

fn catch_status(operation: impl FnOnce() -> u32) -> u32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(operation)).unwrap_or(STATUS_INTERNAL)
}

fn get_client(client: *mut SnaploomCaptureClientV1) -> Result<Arc<CaptureClient>, u32> {
    if client.is_null() {
        return Err(STATUS_INVALID_ARGUMENT);
    }
    clients()
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .get(&(client as usize))
        .cloned()
        .ok_or(STATUS_CLIENT_CLOSED)
}

unsafe fn validate_config(
    config: *const SnaploomCaptureClientConfigV1,
) -> Result<DriverConfiguration, u32> {
    if config.is_null() {
        return Ok(DriverConfiguration {
            host_executable_override: None,
            launch_timeout: Duration::from_secs(5),
            handshake_timeout: Duration::from_secs(2),
        });
    }
    // SAFETY: the non-null pointer is required by the C API to reference readable v1 storage.
    let struct_size = unsafe { ptr::read_unaligned(config.cast::<u32>()) };
    if (struct_size as usize) < size_of::<SnaploomCaptureClientConfigV1>() {
        return Err(STATUS_INVALID_STRUCT_SIZE);
    }
    // SAFETY: the size check above establishes readable v1 input storage.
    let config = unsafe { &*config };
    if config.flags != 0 || config.reserved.iter().any(|value| *value != 0) {
        return Err(STATUS_INVALID_ARGUMENT);
    }
    validate_timeout(config.launch_timeout_ms)?;
    validate_timeout(config.handshake_timeout_ms)?;
    let host_executable_override =
        unsafe { utf8_view(config.host_executable_override)? }.map(PathBuf::from);
    if host_executable_override
        .as_deref()
        .is_some_and(|path| !Path::new(path).is_absolute())
    {
        return Err(STATUS_INVALID_ARGUMENT);
    }
    Ok(DriverConfiguration {
        host_executable_override,
        launch_timeout: Duration::from_millis(if config.launch_timeout_ms == 0 {
            5_000
        } else {
            u64::from(config.launch_timeout_ms)
        }),
        handshake_timeout: Duration::from_millis(if config.handshake_timeout_ms == 0 {
            2_000
        } else {
            u64::from(config.handshake_timeout_ms)
        }),
    })
}

fn validate_timeout(timeout_ms: u32) -> Result<(), u32> {
    if timeout_ms == 0 || (1_000..=30_000).contains(&timeout_ms) {
        Ok(())
    } else {
        Err(STATUS_INVALID_ARGUMENT)
    }
}

unsafe fn utf8_view(view: SnaploomUtf8ViewV1) -> Result<Option<String>, u32> {
    if view.data.is_null() {
        return if view.length == 0 {
            Ok(None)
        } else {
            Err(STATUS_INVALID_ARGUMENT)
        };
    }
    let length = usize::try_from(view.length).map_err(|_| STATUS_INVALID_ARGUMENT)?;
    // SAFETY: the caller promises this view remains readable for the duration of the call.
    let bytes = unsafe { slice::from_raw_parts(view.data, length) };
    if bytes.contains(&0) {
        return Err(STATUS_INVALID_ARGUMENT);
    }
    str::from_utf8(bytes)
        .map(str::to_owned)
        .map(Some)
        .map_err(|_| STATUS_INVALID_ARGUMENT)
}

unsafe fn capture_options(options: *const SnaploomCaptureOptionsV1) -> Result<CaptureOptions, u32> {
    if options.is_null() {
        return Ok(CaptureOptions::default());
    }
    // SAFETY: the non-null pointer is required by the C API to reference readable v1 storage.
    let struct_size = unsafe { ptr::read_unaligned(options.cast::<u32>()) };
    if (struct_size as usize) < size_of::<SnaploomCaptureOptionsV1>() {
        return Err(STATUS_INVALID_STRUCT_SIZE);
    }
    // SAFETY: the size check above establishes readable v1 input storage.
    let options = unsafe { &*options };
    if options.flags & !FLAG_DISABLE_CLIPBOARD != 0
        || options.reserved.iter().any(|value| *value != 0)
    {
        return Err(STATUS_INVALID_ARGUMENT);
    }
    Ok(CaptureOptions {
        disable_clipboard: options.flags & FLAG_DISABLE_CLIPBOARD != 0,
        interaction_timeout: (options.interaction_timeout_ms != 0)
            .then(|| Duration::from_millis(options.interaction_timeout_ms)),
    })
}

fn into_completion(request_id: u64, terminal: Terminal) -> *mut SnaploomCaptureCompletionV1 {
    let (kind, error_code, flags, mut png, pixel_width, pixel_height) = match terminal {
        Terminal::Completed {
            png,
            clipboard_written,
        } => {
            let pixel_width = png.pixel_width();
            let pixel_height = png.pixel_height();
            (
                COMPLETION_COMPLETED,
                StableError::None as u32,
                if clipboard_written {
                    COMPLETION_FLAG_CLIPBOARD_WRITTEN
                } else {
                    0
                },
                png.into_vec(),
                pixel_width,
                pixel_height,
            )
        }
        Terminal::Canceled { .. } => (
            COMPLETION_CANCELED,
            StableError::None as u32,
            0,
            Vec::new(),
            0,
            0,
        ),
        Terminal::Failed { error, retryable } => (
            COMPLETION_FAILED,
            error as u32,
            if retryable {
                COMPLETION_FLAG_RETRYABLE
            } else {
                0
            },
            Vec::new(),
            0,
            0,
        ),
    };
    let png_data = if png.is_empty() {
        ptr::null()
    } else {
        png.as_ptr()
    };
    let png_size = png.len() as u64;
    let owned = Box::new(OwnedCompletion {
        public: SnaploomCaptureCompletionV1 {
            struct_size: size_of::<SnaploomCaptureCompletionV1>() as u32,
            kind,
            error_code,
            flags,
            request_id,
            png_data,
            png_size,
            pixel_width,
            pixel_height,
            reserved: [0; 4],
        },
        png: std::mem::take(&mut png),
    });
    Box::into_raw(owned).cast::<SnaploomCaptureCompletionV1>()
}

#[unsafe(no_mangle)]
/// Writes the stable ABI version into caller-owned storage.
///
/// # Safety
///
/// `out_version` must be null or point to writable storage whose first field is a readable
/// `struct_size` and whose declared size covers the v1 structure.
pub unsafe extern "C" fn snaploom_capture_version_v1(
    out_version: *mut SnaploomCaptureVersionInfoV1,
) -> u32 {
    catch_status(|| {
        if out_version.is_null() {
            return STATUS_INVALID_ARGUMENT;
        }
        // SAFETY: the caller supplies writable storage beginning with struct_size.
        if unsafe { (*out_version).struct_size as usize }
            < size_of::<SnaploomCaptureVersionInfoV1>()
        {
            return STATUS_INVALID_STRUCT_SIZE;
        }
        // SAFETY: the size check above establishes writable v1 output storage.
        unsafe {
            out_version.write(SnaploomCaptureVersionInfoV1 {
                struct_size: size_of::<SnaploomCaptureVersionInfoV1>() as u32,
                abi_major: ABI_MAJOR,
                sdk_semver: SnaploomUtf8ViewV1 {
                    data: SDK_SEMVER.as_ptr(),
                    length: SDK_SEMVER.len() as u64,
                },
                reserved: [0; 4],
            });
        }
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
/// Creates an isolated capture client and returns its opaque handle.
///
/// # Safety
///
/// Non-null pointers must reference readable or writable storage as documented by the public C
/// header. Any byte views in `config` must remain readable for the duration of this call.
pub unsafe extern "C" fn snaploom_capture_client_create_v1(
    config: *const SnaploomCaptureClientConfigV1,
    out_client: *mut *mut SnaploomCaptureClientV1,
) -> u32 {
    catch_status(|| {
        if out_client.is_null() {
            return STATUS_INVALID_ARGUMENT;
        }
        // SAFETY: out_client is non-null writable output storage by contract.
        unsafe { out_client.write(ptr::null_mut()) };
        let config = match unsafe { validate_config(config) } {
            Ok(config) => config,
            Err(status) => return status,
        };
        let client = match CaptureClient::new(driver_for_new_client(config)) {
            Ok(client) => Arc::new(client),
            Err(status) => return status_from_client(status),
        };
        let token = Box::into_raw(Box::new(0_u8)).cast::<SnaploomCaptureClientV1>();
        clients()
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .insert(token as usize, client);
        // SAFETY: out_client is non-null writable output storage by contract.
        unsafe { out_client.write(token) };
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
/// Starts one asynchronous capture operation.
///
/// # Safety
///
/// `client` must be a live handle from this library. Non-null input/output pointers must be valid
/// for the call, and an accepted request requires `callback` and `user_data` to remain valid until
/// the callback returns.
pub unsafe extern "C" fn snaploom_capture_start_v1(
    client: *mut SnaploomCaptureClientV1,
    options: *const SnaploomCaptureOptionsV1,
    callback: Option<SnaploomCaptureCallbackV1>,
    user_data: *mut c_void,
    out_request_id: *mut u64,
) -> u32 {
    catch_status(|| {
        if !out_request_id.is_null() {
            // SAFETY: a non-null request output points to writable u64 storage by contract.
            unsafe { out_request_id.write(0) };
        }
        let Some(callback) = callback else {
            return STATUS_INVALID_ARGUMENT;
        };
        let client_instance = match get_client(client) {
            Ok(client) => client,
            Err(status) => return status,
        };
        let options = match unsafe { capture_options(options) } {
            Ok(options) => options,
            Err(status) => return status,
        };
        let client_address = client as usize;
        let user_data_address = user_data as usize;
        let request_id =
            match client_instance.start_with_request_id(options, move |request_id, terminal| {
                let completion = into_completion(request_id, terminal);
                // SAFETY: start acceptance guarantees the caller keeps callback/user_data valid.
                unsafe {
                    callback(
                        client_address as *mut SnaploomCaptureClientV1,
                        completion,
                        user_data_address as *mut c_void,
                    );
                }
            }) {
                Ok(request_id) => request_id,
                Err(status) => return status_from_client(status),
            };
        if !out_request_id.is_null() {
            // SAFETY: a non-null request output points to writable u64 storage by contract.
            unsafe { out_request_id.write(request_id) };
        }
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
/// Records an idempotent cancellation intent for a live request.
///
/// # Safety
///
/// `client` must be null or a handle returned by this library that has not been destroyed.
pub unsafe extern "C" fn snaploom_capture_cancel_v1(
    client: *mut SnaploomCaptureClientV1,
    request_id: u64,
) -> u32 {
    catch_status(|| match get_client(client) {
        Ok(client) => client
            .cancel(request_id)
            .map_or_else(status_from_client, |()| STATUS_OK),
        Err(status) => status,
    })
}

#[unsafe(no_mangle)]
/// Closes and releases one opaque client handle after its promised callbacks finish.
///
/// # Safety
///
/// `client` must be null or a handle returned by this library. A successful destroy consumes the
/// handle, which must not be used again.
pub unsafe extern "C" fn snaploom_capture_client_destroy_v1(
    client: *mut SnaploomCaptureClientV1,
) -> u32 {
    catch_status(|| {
        if client.is_null() {
            return STATUS_INVALID_ARGUMENT;
        }
        let key = client as usize;
        let client_instance = {
            let mut registry = clients().lock().unwrap_or_else(|error| error.into_inner());
            let Some(client_instance) = registry.get(&key).cloned() else {
                return STATUS_CLIENT_CLOSED;
            };
            if client_instance.is_callback_context() {
                return STATUS_CALLBACK_CONTEXT;
            }
            registry.remove(&key);
            client_instance
        };
        if let Err(status) = client_instance.close() {
            return status_from_client(status);
        }
        // SAFETY: this token was allocated by create and removed exactly once above.
        unsafe { drop(Box::from_raw(client.cast::<u8>())) };
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
/// Zeroes and releases one completion allocation.
///
/// # Safety
///
/// `completion` must be null or an allocation delivered by this exact library instance, and each
/// non-null completion may be passed at most once.
pub unsafe extern "C" fn snaploom_capture_completion_free_v1(
    completion: *mut SnaploomCaptureCompletionV1,
) {
    let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if completion.is_null() {
            return;
        }
        // SAFETY: the completion was allocated by into_completion with public as its first field.
        let mut owned = unsafe { Box::from_raw(completion.cast::<OwnedCompletion>()) };
        owned.png.zeroize();
        // SAFETY: overwriting the plain C fields does not affect Vec drop state.
        unsafe { ptr::write_bytes(&mut owned.public, 0, 1) };
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn snaploom_capture_error_name_v1(error_code: u32) -> *const c_char {
    let name: &'static [u8] = match StableError::try_from(error_code as i32) {
        Ok(StableError::None) => b"NONE\0",
        Ok(StableError::Busy) => b"BUSY\0",
        Ok(StableError::HostNotFound) => b"HOST_NOT_FOUND\0",
        Ok(StableError::HostStartFailed) => b"HOST_START_FAILED\0",
        Ok(StableError::HostStartTimeout) => b"HOST_START_TIMEOUT\0",
        Ok(StableError::AuthenticationFailed) => b"AUTHENTICATION_FAILED\0",
        Ok(StableError::ProtocolIncompatible) => b"PROTOCOL_INCOMPATIBLE\0",
        Ok(StableError::ProtocolError) => b"PROTOCOL_ERROR\0",
        Ok(StableError::HostCrashed) => b"HOST_CRASHED\0",
        Ok(StableError::TransportFailed) => b"TRANSPORT_FAILED\0",
        Ok(StableError::HandshakeTimeout) => b"HANDSHAKE_TIMEOUT\0",
        Ok(StableError::RequestTimeout) => b"REQUEST_TIMEOUT\0",
        Ok(StableError::PlatformUnavailable) => b"PLATFORM_UNAVAILABLE\0",
        Ok(StableError::PermissionNotGranted) => b"PERMISSION_NOT_GRANTED\0",
        Ok(StableError::PermissionRevoked) => b"PERMISSION_REVOKED\0",
        Ok(StableError::DisplayUnavailable) => b"DISPLAY_UNAVAILABLE\0",
        Ok(StableError::CaptureUnavailable) => b"CAPTURE_UNAVAILABLE\0",
        Ok(StableError::CaptureTimeout) => b"CAPTURE_TIMEOUT\0",
        Ok(StableError::PixelConversionFailed) => b"PIXEL_CONVERSION_FAILED\0",
        Ok(StableError::InvalidResult) => b"INVALID_RESULT\0",
        Ok(StableError::ResultTooLarge) => b"RESULT_TOO_LARGE\0",
        Ok(StableError::ClipboardWriteFailed) => b"CLIPBOARD_WRITE_FAILED\0",
        Ok(StableError::OutOfMemory) => b"OUT_OF_MEMORY\0",
        Ok(StableError::ClientClosed) => b"CLIENT_CLOSED\0",
        Ok(StableError::Internal) => b"INTERNAL\0",
        Err(_) => b"UNKNOWN\0",
    };
    name.as_ptr().cast::<c_char>()
}

const fn size_of<T>() -> usize {
    std::mem::size_of::<T>()
}
