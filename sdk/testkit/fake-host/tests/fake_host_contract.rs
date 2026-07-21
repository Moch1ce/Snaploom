use std::sync::{Arc, mpsc};
use std::time::Duration;

use snaploom_capture_client::{CaptureClient, CaptureOptions, StableError, Terminal};
use snaploom_capture_fake_host::{FakeHost, Scenario};

fn run(scenario: Scenario) -> Terminal {
    let client = CaptureClient::new(Arc::new(FakeHost::scripted([scenario]))).unwrap();
    let (sender, receiver) = mpsc::channel();
    client
        .start(CaptureOptions::default(), move |terminal| {
            sender.send(terminal).unwrap();
        })
        .unwrap();
    let terminal = receiver.recv_timeout(Duration::from_secs(1)).unwrap();
    client.close().unwrap();
    terminal
}

#[test]
fn scripts_busy_and_crash_as_stable_errors() {
    assert!(matches!(
        run(Scenario::Busy),
        Terminal::Failed {
            error: StableError::Busy,
            retryable: true
        }
    ));
    assert!(matches!(
        run(Scenario::Crash),
        Terminal::Failed {
            error: StableError::HostCrashed,
            retryable: true
        }
    ));
}

#[test]
fn malformed_scripted_png_fails_closed() {
    assert!(matches!(
        run(Scenario::Completed {
            png: vec![1, 2, 3],
            pixel_width: 1,
            pixel_height: 1,
            clipboard_written: false,
        }),
        Terminal::Failed {
            error: StableError::InvalidResult,
            retryable: false
        }
    ));
}
