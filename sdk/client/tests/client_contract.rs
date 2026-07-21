use std::sync::{
    Arc, Mutex,
    atomic::{AtomicBool, AtomicUsize, Ordering},
    mpsc,
};
use std::time::{Duration, Instant};

use snaploom_capture_client::{
    CancelSource, CaptureClient, CaptureDriver, CaptureOptions, ClientStatus, DriverRequest,
    StableError, Terminal,
};
use snaploom_capture_protocol::PngAccumulator;

fn completed() -> Terminal {
    let bytes = vec![
        0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, b'I', b'H', b'D', b'R', 0, 0,
        0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0, 0, 0, 0,
    ];
    let mut png = PngAccumulator::begin(bytes.len() as u64, 1, 1).unwrap();
    png.push(0, &bytes).unwrap();
    Terminal::Completed {
        png: png.finish().unwrap(),
        clipboard_written: true,
    }
}

struct ImmediateDriver(Mutex<Option<Terminal>>);

impl CaptureDriver for ImmediateDriver {
    fn capture(&self, _request: DriverRequest) -> Terminal {
        self.0.lock().unwrap().take().unwrap()
    }
}

struct CancelDriver;

impl CaptureDriver for CancelDriver {
    fn capture(&self, request: DriverRequest) -> Terminal {
        while !request.is_canceled() {
            std::thread::sleep(Duration::from_millis(1));
        }
        Terminal::Canceled {
            source: CancelSource::Caller,
        }
    }
}

#[test]
fn accepted_start_delivers_exactly_one_serial_callback() {
    let client =
        CaptureClient::new(Arc::new(ImmediateDriver(Mutex::new(Some(completed()))))).unwrap();
    let (sender, receiver) = mpsc::channel();
    let callbacks = Arc::new(AtomicUsize::new(0));
    let callback_counter = callbacks.clone();
    let request_id = client
        .start(CaptureOptions::default(), move |terminal| {
            callback_counter.fetch_add(1, Ordering::SeqCst);
            sender.send(terminal).unwrap();
        })
        .unwrap();
    assert_ne!(request_id, 0);
    assert!(matches!(
        receiver.recv_timeout(Duration::from_secs(1)).unwrap(),
        Terminal::Completed { .. }
    ));
    std::thread::sleep(Duration::from_millis(10));
    assert_eq!(callbacks.load(Ordering::SeqCst), 1);
    client.close().unwrap();
}

#[test]
fn cancel_is_idempotent_until_terminal_callback() {
    let client = CaptureClient::new(Arc::new(CancelDriver)).unwrap();
    let (sender, receiver) = mpsc::channel();
    let request_id = client
        .start(CaptureOptions::default(), move |terminal| {
            sender.send(terminal).unwrap();
        })
        .unwrap();
    assert_eq!(client.cancel(request_id), Ok(()));
    assert_eq!(client.cancel(request_id), Ok(()));
    assert!(matches!(
        receiver.recv_timeout(Duration::from_secs(1)).unwrap(),
        Terminal::Canceled { .. }
    ));
    assert_eq!(client.cancel(request_id), Err(ClientStatus::NotFound));
    client.close().unwrap();
}

#[test]
fn explicit_interaction_timeout_becomes_request_timeout() {
    let client = CaptureClient::new(Arc::new(CancelDriver)).unwrap();
    let (sender, receiver) = mpsc::channel();
    client
        .start(
            CaptureOptions {
                interaction_timeout: Some(Duration::from_millis(10)),
                ..CaptureOptions::default()
            },
            move |terminal| sender.send(terminal).unwrap(),
        )
        .unwrap();
    assert!(matches!(
        receiver.recv_timeout(Duration::from_secs(1)).unwrap(),
        Terminal::Failed {
            error: StableError::RequestTimeout,
            ..
        }
    ));
    client.close().unwrap();
}

#[test]
fn close_waits_for_an_already_committed_callback() {
    let client =
        CaptureClient::new(Arc::new(ImmediateDriver(Mutex::new(Some(completed()))))).unwrap();
    let entered = Arc::new(AtomicBool::new(false));
    let callback_entered = entered.clone();
    client
        .start(CaptureOptions::default(), move |_| {
            callback_entered.store(true, Ordering::SeqCst);
            std::thread::sleep(Duration::from_millis(40));
        })
        .unwrap();
    while !entered.load(Ordering::SeqCst) {
        std::thread::yield_now();
    }
    let start = Instant::now();
    client.close().unwrap();
    assert!(start.elapsed() >= Duration::from_millis(30));
}

#[test]
fn callback_context_cannot_close_its_own_executor() {
    let client = Arc::new(
        CaptureClient::new(Arc::new(ImmediateDriver(Mutex::new(Some(completed()))))).unwrap(),
    );
    let callback_client = client.clone();
    let (sender, receiver) = mpsc::channel();
    client
        .start(CaptureOptions::default(), move |_| {
            sender.send(callback_client.close()).unwrap();
        })
        .unwrap();
    assert_eq!(
        receiver.recv_timeout(Duration::from_secs(1)).unwrap(),
        Err(ClientStatus::CallbackContext)
    );
    client.close().unwrap();
}

#[test]
fn a_panicking_foreign_callback_does_not_break_destroy() {
    let client =
        CaptureClient::new(Arc::new(ImmediateDriver(Mutex::new(Some(completed()))))).unwrap();
    client
        .start(CaptureOptions::default(), move |_| {
            panic!("simulated foreign callback failure");
        })
        .unwrap();
    client.close().unwrap();
}
