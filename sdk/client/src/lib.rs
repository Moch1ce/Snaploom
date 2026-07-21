use std::collections::HashMap;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex, mpsc};
use std::thread::{self, JoinHandle, ThreadId};
use std::time::Duration;

use snaploom_capture_protocol::SensitivePng;
pub use snaploom_capture_protocol::{CancelSource, StableError};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ProtocolVersion {
    pub major: u16,
    pub minor: u16,
}

#[must_use]
pub const fn supported_protocol() -> ProtocolVersion {
    ProtocolVersion {
        major: snaploom_capture_protocol::PROTOCOL_MAJOR,
        minor: snaploom_capture_protocol::PROTOCOL_MINOR,
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u32)]
pub enum ClientStatus {
    InvalidArgument = 1,
    InvalidStructSize = 2,
    ClientClosed = 3,
    CallbackContext = 4,
    NotFound = 5,
    OutOfMemory = 6,
    Internal = 255,
}

#[derive(Debug, Clone, Default)]
pub struct CaptureOptions {
    pub disable_clipboard: bool,
    pub interaction_timeout: Option<Duration>,
}

#[derive(Debug)]
pub enum Terminal {
    Completed {
        png: SensitivePng,
        clipboard_written: bool,
    },
    Canceled {
        source: CancelSource,
    },
    Failed {
        error: StableError,
        retryable: bool,
    },
}

#[derive(Clone)]
pub struct DriverRequest {
    pub request_id: u64,
    pub options: CaptureOptions,
    cancel: Arc<AtomicBool>,
}

impl DriverRequest {
    #[must_use]
    pub fn is_canceled(&self) -> bool {
        self.cancel.load(Ordering::Acquire)
    }
}

pub trait CaptureDriver: Send + Sync + 'static {
    fn capture(&self, request: DriverRequest) -> Terminal;
}

#[derive(Debug, Default)]
pub struct UnavailableDriver;

impl CaptureDriver for UnavailableDriver {
    fn capture(&self, _request: DriverRequest) -> Terminal {
        Terminal::Failed {
            error: StableError::HostNotFound,
            retryable: false,
        }
    }
}

type Callback = Box<dyn FnOnce(u64, Terminal) + Send + 'static>;

enum CallbackMessage {
    Complete {
        request_id: u64,
        terminal: Terminal,
        callback: Callback,
    },
    Shutdown,
}

const CAUSE_NONE: u8 = 0;
const CAUSE_CALLER: u8 = 1;
const CAUSE_CLIENT_CLOSED: u8 = 2;
const CAUSE_TIMEOUT: u8 = 3;

struct RequestState {
    cancel: Arc<AtomicBool>,
    terminal_cause: Arc<AtomicU8>,
    committed: bool,
}

#[derive(Default)]
struct State {
    closed: bool,
    requests: HashMap<u64, RequestState>,
}

struct Inner {
    state: Mutex<State>,
    requests_drained: Condvar,
    callback_thread_id: Mutex<Option<ThreadId>>,
}

pub struct CaptureClient {
    inner: Arc<Inner>,
    driver: Arc<dyn CaptureDriver>,
    callback_sender: mpsc::Sender<CallbackMessage>,
    callback_thread: Mutex<Option<JoinHandle<()>>>,
    next_request_id: AtomicU64,
}

impl CaptureClient {
    pub fn new(driver: Arc<dyn CaptureDriver>) -> Result<Self, ClientStatus> {
        let (callback_sender, callback_receiver) = mpsc::channel();
        let (started_sender, started_receiver) = mpsc::sync_channel(1);
        let inner = Arc::new(Inner {
            state: Mutex::new(State::default()),
            requests_drained: Condvar::new(),
            callback_thread_id: Mutex::new(None),
        });
        let callback_inner = inner.clone();
        let callback_thread = thread::Builder::new()
            .name("snaploom-sdk-callback".into())
            .spawn(move || {
                let thread_id = thread::current().id();
                *callback_inner
                    .callback_thread_id
                    .lock()
                    .unwrap_or_else(|error| error.into_inner()) = Some(thread_id);
                let _ = started_sender.send(());
                while let Ok(message) = callback_receiver.recv() {
                    match message {
                        CallbackMessage::Complete {
                            request_id,
                            terminal,
                            callback,
                        } => {
                            let _ =
                                catch_unwind(AssertUnwindSafe(|| callback(request_id, terminal)));
                            let mut state = callback_inner
                                .state
                                .lock()
                                .unwrap_or_else(|error| error.into_inner());
                            state.requests.remove(&request_id);
                            callback_inner.requests_drained.notify_all();
                        }
                        CallbackMessage::Shutdown => break,
                    }
                }
            })
            .map_err(|_| ClientStatus::OutOfMemory)?;
        started_receiver
            .recv()
            .map_err(|_| ClientStatus::Internal)?;

        Ok(Self {
            inner,
            driver,
            callback_sender,
            callback_thread: Mutex::new(Some(callback_thread)),
            next_request_id: AtomicU64::new(1),
        })
    }

    pub fn start<F>(&self, options: CaptureOptions, callback: F) -> Result<u64, ClientStatus>
    where
        F: FnOnce(Terminal) + Send + 'static,
    {
        self.start_with_request_id(options, move |_request_id, terminal| callback(terminal))
    }

    pub fn start_with_request_id<F>(
        &self,
        options: CaptureOptions,
        callback: F,
    ) -> Result<u64, ClientStatus>
    where
        F: FnOnce(u64, Terminal) + Send + 'static,
    {
        let request_id = self.next_nonzero_request_id();
        let cancel = Arc::new(AtomicBool::new(false));
        let terminal_cause = Arc::new(AtomicU8::new(CAUSE_NONE));

        {
            let mut state = self
                .inner
                .state
                .lock()
                .unwrap_or_else(|error| error.into_inner());
            if state.closed {
                return Err(ClientStatus::ClientClosed);
            }
            state.requests.insert(
                request_id,
                RequestState {
                    cancel: cancel.clone(),
                    terminal_cause: terminal_cause.clone(),
                    committed: false,
                },
            );
        }

        if let Some(timeout) = options.interaction_timeout {
            let weak_inner = Arc::downgrade(&self.inner);
            if thread::Builder::new()
                .name("snaploom-sdk-timeout".into())
                .spawn(move || {
                    thread::sleep(timeout);
                    if let Some(inner) = weak_inner.upgrade() {
                        let state = inner
                            .state
                            .lock()
                            .unwrap_or_else(|error| error.into_inner());
                        if let Some(request) = state.requests.get(&request_id)
                            && !request.committed
                            && request
                                .terminal_cause
                                .compare_exchange(
                                    CAUSE_NONE,
                                    CAUSE_TIMEOUT,
                                    Ordering::AcqRel,
                                    Ordering::Acquire,
                                )
                                .is_ok()
                        {
                            request.cancel.store(true, Ordering::Release);
                        }
                    }
                })
                .is_err()
            {
                self.remove_uncommitted_request(request_id);
                return Err(ClientStatus::OutOfMemory);
            }
        }

        let driver = self.driver.clone();
        let inner = self.inner.clone();
        let callback_sender = self.callback_sender.clone();
        let driver_request = DriverRequest {
            request_id,
            options,
            cancel,
        };
        let worker = thread::Builder::new()
            .name("snaploom-sdk-request".into())
            .spawn(move || {
                let terminal = catch_unwind(AssertUnwindSafe(|| driver.capture(driver_request)))
                    .unwrap_or(Terminal::Failed {
                        error: StableError::Internal,
                        retryable: false,
                    });
                let terminal = {
                    let mut state = inner
                        .state
                        .lock()
                        .unwrap_or_else(|error| error.into_inner());
                    let Some(request) = state.requests.get_mut(&request_id) else {
                        return;
                    };
                    request.committed = true;
                    match request.terminal_cause.load(Ordering::Acquire) {
                        CAUSE_CLIENT_CLOSED => Terminal::Failed {
                            error: StableError::ClientClosed,
                            retryable: false,
                        },
                        CAUSE_TIMEOUT => Terminal::Failed {
                            error: StableError::RequestTimeout,
                            retryable: true,
                        },
                        _ => terminal,
                    }
                };
                if callback_sender
                    .send(CallbackMessage::Complete {
                        request_id,
                        terminal,
                        callback: Box::new(callback),
                    })
                    .is_err()
                {
                    let mut state = inner
                        .state
                        .lock()
                        .unwrap_or_else(|error| error.into_inner());
                    state.requests.remove(&request_id);
                    inner.requests_drained.notify_all();
                }
            });

        if worker.is_err() {
            self.remove_uncommitted_request(request_id);
            return Err(ClientStatus::OutOfMemory);
        }

        Ok(request_id)
    }

    pub fn cancel(&self, request_id: u64) -> Result<(), ClientStatus> {
        let state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let request = state
            .requests
            .get(&request_id)
            .ok_or(ClientStatus::NotFound)?;
        let _ = request.terminal_cause.compare_exchange(
            CAUSE_NONE,
            CAUSE_CALLER,
            Ordering::AcqRel,
            Ordering::Acquire,
        );
        request.cancel.store(true, Ordering::Release);
        Ok(())
    }

    pub fn close(&self) -> Result<(), ClientStatus> {
        if self.is_callback_context() {
            return Err(ClientStatus::CallbackContext);
        }
        let mut state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        state.closed = true;
        for request in state.requests.values() {
            if !request.committed {
                request
                    .terminal_cause
                    .store(CAUSE_CLIENT_CLOSED, Ordering::Release);
                request.cancel.store(true, Ordering::Release);
            }
        }
        while !state.requests.is_empty() {
            state = self
                .inner
                .requests_drained
                .wait(state)
                .unwrap_or_else(|error| error.into_inner());
        }
        drop(state);

        let mut callback_thread = self
            .callback_thread
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if let Some(callback_thread) = callback_thread.take() {
            let _ = self.callback_sender.send(CallbackMessage::Shutdown);
            callback_thread.join().map_err(|_| ClientStatus::Internal)?;
        }
        Ok(())
    }

    #[must_use]
    pub fn is_callback_context(&self) -> bool {
        self.inner
            .callback_thread_id
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .as_ref()
            .is_some_and(|thread_id| thread::current().id() == *thread_id)
    }

    fn next_nonzero_request_id(&self) -> u64 {
        loop {
            let request_id = self.next_request_id.fetch_add(1, Ordering::Relaxed);
            if request_id != 0 {
                return request_id;
            }
        }
    }

    fn remove_uncommitted_request(&self, request_id: u64) {
        let mut state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        state.requests.remove(&request_id);
        self.inner.requests_drained.notify_all();
    }
}

impl Drop for CaptureClient {
    fn drop(&mut self) {
        if !self.is_callback_context() {
            let _ = self.close();
        }
    }
}
