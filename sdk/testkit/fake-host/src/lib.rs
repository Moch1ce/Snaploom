use std::collections::VecDeque;
use std::sync::Mutex;
use std::thread;
use std::time::{Duration, Instant};

use snaploom_capture_client::{CancelSource, CaptureDriver, DriverRequest, StableError, Terminal};
use snaploom_capture_protocol::PngAccumulator;

#[derive(Debug, Clone)]
pub enum Scenario {
    Completed {
        png: Vec<u8>,
        pixel_width: u32,
        pixel_height: u32,
        clipboard_written: bool,
    },
    Busy,
    Canceled(CancelSource),
    Failed {
        error: StableError,
        retryable: bool,
    },
    Crash,
    Delay {
        duration: Duration,
        then: Box<Scenario>,
    },
}

#[derive(Debug, Default)]
pub struct FakeHost {
    scenarios: Mutex<VecDeque<Scenario>>,
}

impl FakeHost {
    #[must_use]
    pub fn scripted(scenarios: impl IntoIterator<Item = Scenario>) -> Self {
        Self {
            scenarios: Mutex::new(scenarios.into_iter().collect()),
        }
    }

    #[must_use]
    pub fn successful() -> Self {
        Self::scripted([Scenario::Completed {
            png: tiny_png(),
            pixel_width: 1,
            pixel_height: 1,
            clipboard_written: true,
        }])
    }

    fn run_scenario(request: &DriverRequest, scenario: Scenario) -> Terminal {
        match scenario {
            Scenario::Completed {
                png,
                pixel_width,
                pixel_height,
                clipboard_written,
            } => {
                let Ok(mut accumulator) =
                    PngAccumulator::begin(png.len() as u64, pixel_width, pixel_height)
                else {
                    return invalid_result();
                };
                if accumulator.push(0, &png).is_err() {
                    return invalid_result();
                }
                match accumulator.finish() {
                    Ok(png) => Terminal::Completed {
                        png,
                        clipboard_written,
                    },
                    Err(_) => invalid_result(),
                }
            }
            Scenario::Busy => Terminal::Failed {
                error: StableError::Busy,
                retryable: true,
            },
            Scenario::Canceled(source) => Terminal::Canceled { source },
            Scenario::Failed { error, retryable } => Terminal::Failed { error, retryable },
            Scenario::Crash => Terminal::Failed {
                error: StableError::HostCrashed,
                retryable: true,
            },
            Scenario::Delay { duration, then } => {
                let deadline = Instant::now() + duration;
                while Instant::now() < deadline {
                    if request.is_canceled() {
                        return Terminal::Canceled {
                            source: CancelSource::Caller,
                        };
                    }
                    thread::sleep(Duration::from_millis(1));
                }
                Self::run_scenario(request, *then)
            }
        }
    }
}

impl CaptureDriver for FakeHost {
    fn capture(&self, request: DriverRequest) -> Terminal {
        let scenario = self
            .scenarios
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .pop_front()
            .unwrap_or(Scenario::Failed {
                error: StableError::HostNotFound,
                retryable: false,
            });
        Self::run_scenario(&request, scenario)
    }
}

fn invalid_result() -> Terminal {
    Terminal::Failed {
        error: StableError::InvalidResult,
        retryable: false,
    }
}

#[must_use]
pub fn tiny_png() -> Vec<u8> {
    vec![
        0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, b'I', b'H', b'D', b'R', 0, 0,
        0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0, 0, 0, 0,
    ]
}
