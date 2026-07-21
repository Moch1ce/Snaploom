use std::time::Duration;

use snaploom_platform_contract::PlatformError;

pub(crate) const CLIPBOARD_ATTEMPTS: usize = 5;
pub(crate) const CLIPBOARD_RETRY_DELAY: Duration = Duration::from_millis(20);

pub(crate) trait ClipboardBackend {
    type Memory;

    fn open(&mut self) -> Result<(), ()>;
    fn close(&mut self);
    fn empty(&mut self) -> Result<(), ()>;
    fn allocate_png(&mut self, png: &[u8]) -> Result<Self::Memory, ()>;
    fn transfer_png(&mut self, memory: &mut Option<Self::Memory>) -> Result<(), ()>;
    fn release(&mut self, memory: Self::Memory);
    fn wait(&mut self, delay: Duration);
}

pub(crate) fn write_png<B: ClipboardBackend>(
    backend: &mut B,
    png: &[u8],
) -> Result<(), PlatformError> {
    if png.is_empty() {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    let mut opened = false;
    for attempt in 0..CLIPBOARD_ATTEMPTS {
        if backend.open().is_ok() {
            opened = true;
            break;
        }
        if attempt + 1 < CLIPBOARD_ATTEMPTS {
            backend.wait(CLIPBOARD_RETRY_DELAY);
        }
    }
    if !opened {
        return Err(PlatformError::ClipboardBusy);
    }

    let result = (|| {
        backend
            .empty()
            .map_err(|()| PlatformError::ClipboardWriteFailed)?;
        let mut memory = Some(
            backend
                .allocate_png(png)
                .map_err(|()| PlatformError::ClipboardWriteFailed)?,
        );
        if backend.transfer_png(&mut memory).is_err() {
            if let Some(memory) = memory.take() {
                backend.release(memory);
            }
            return Err(PlatformError::ClipboardWriteFailed);
        }
        debug_assert!(memory.is_none(), "clipboard ownership was not transferred");
        Ok(())
    })();
    backend.close();
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Default)]
    struct FakeClipboard {
        open_failures: usize,
        calls: Vec<&'static str>,
        transfer_fails: bool,
    }

    impl ClipboardBackend for FakeClipboard {
        type Memory = Vec<u8>;

        fn open(&mut self) -> Result<(), ()> {
            self.calls.push("open");
            if self.open_failures > 0 {
                self.open_failures -= 1;
                Err(())
            } else {
                Ok(())
            }
        }

        fn close(&mut self) {
            self.calls.push("close");
        }

        fn empty(&mut self) -> Result<(), ()> {
            self.calls.push("empty");
            Ok(())
        }

        fn allocate_png(&mut self, png: &[u8]) -> Result<Self::Memory, ()> {
            self.calls.push("allocate");
            Ok(png.to_vec())
        }

        fn transfer_png(&mut self, memory: &mut Option<Self::Memory>) -> Result<(), ()> {
            self.calls.push("transfer");
            if self.transfer_fails {
                Err(())
            } else {
                let _ = memory.take();
                Ok(())
            }
        }

        fn release(&mut self, _memory: Self::Memory) {
            self.calls.push("release");
        }

        fn wait(&mut self, _delay: Duration) {
            self.calls.push("wait");
        }
    }

    #[test]
    fn retries_contention_with_a_bounded_schedule() {
        let mut clipboard = FakeClipboard {
            open_failures: 4,
            ..FakeClipboard::default()
        };

        write_png(&mut clipboard, b"png").unwrap();

        assert_eq!(
            clipboard.calls,
            [
                "open", "wait", "open", "wait", "open", "wait", "open", "wait", "open", "empty",
                "allocate", "transfer", "close"
            ]
        );
    }

    #[test]
    fn reports_busy_after_five_attempts_without_emptying() {
        let mut clipboard = FakeClipboard {
            open_failures: 5,
            ..FakeClipboard::default()
        };

        assert_eq!(
            write_png(&mut clipboard, b"png"),
            Err(PlatformError::ClipboardBusy)
        );
        assert_eq!(
            clipboard.calls,
            [
                "open", "wait", "open", "wait", "open", "wait", "open", "wait", "open"
            ]
        );
    }

    #[test]
    fn releases_memory_and_closes_on_transfer_failure() {
        let mut clipboard = FakeClipboard {
            transfer_fails: true,
            ..FakeClipboard::default()
        };

        assert_eq!(
            write_png(&mut clipboard, b"png"),
            Err(PlatformError::ClipboardWriteFailed)
        );
        assert_eq!(
            clipboard.calls,
            ["open", "empty", "allocate", "transfer", "release", "close"]
        );
    }
}
