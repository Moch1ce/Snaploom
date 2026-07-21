use std::time::Duration;

use super::Windows;
use crate::clipboard::{self, ClipboardBackend};
use snaploom_platform_contract::PlatformError;

pub(crate) fn write_png(owner: isize, png: &[u8]) -> Result<(), PlatformError> {
    if owner == 0 {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    let mut backend = NativeClipboard {
        owner: Windows::Win32::Foundation::HWND(owner as *mut _),
        format: None,
    };
    clipboard::write_png(&mut backend, png)
}

struct NativeClipboard {
    owner: Windows::Win32::Foundation::HWND,
    format: Option<u32>,
}

struct NativeClipboardMemory {
    handle: Windows::Win32::Foundation::HGLOBAL,
    byte_len: usize,
}

impl ClipboardBackend for NativeClipboard {
    type Memory = NativeClipboardMemory;

    fn open(&mut self) -> Result<(), ()> {
        unsafe { Windows::Win32::System::DataExchange::OpenClipboard(Some(self.owner)) }
            .map_err(|_| ())
    }

    fn close(&mut self) {
        let _ = unsafe { Windows::Win32::System::DataExchange::CloseClipboard() };
    }

    fn empty(&mut self) -> Result<(), ()> {
        unsafe { Windows::Win32::System::DataExchange::EmptyClipboard() }.map_err(|_| ())
    }

    fn allocate_png(&mut self, png: &[u8]) -> Result<Self::Memory, ()> {
        use Windows::Win32::System::Memory::{
            GMEM_MOVEABLE, GlobalAlloc, GlobalLock, GlobalUnlock,
        };

        let memory = unsafe { GlobalAlloc(GMEM_MOVEABLE, png.len()) }.map_err(|_| ())?;
        let pointer = unsafe { GlobalLock(memory) };
        if pointer.is_null() {
            let _ = unsafe { Windows::Win32::Foundation::GlobalFree(Some(memory)) };
            return Err(());
        }
        unsafe { std::ptr::copy_nonoverlapping(png.as_ptr(), pointer.cast::<u8>(), png.len()) };
        let _ = unsafe { GlobalUnlock(memory) };
        Ok(NativeClipboardMemory {
            handle: memory,
            byte_len: png.len(),
        })
    }

    fn transfer_png(&mut self, memory: &mut Option<Self::Memory>) -> Result<(), ()> {
        use Windows::Win32::Foundation::HANDLE;
        use Windows::Win32::System::DataExchange::{RegisterClipboardFormatW, SetClipboardData};

        let format = *self
            .format
            .get_or_insert_with(|| unsafe { RegisterClipboardFormatW(windows_core::w!("PNG")) });
        if format == 0 {
            return Err(());
        }
        let allocation = memory.as_ref().ok_or(())?;
        unsafe { SetClipboardData(format, Some(HANDLE(allocation.handle.0))) }.map_err(|_| ())?;
        let _ = memory.take();
        Ok(())
    }

    fn release(&mut self, memory: Self::Memory) {
        use Windows::Win32::System::Memory::{GlobalLock, GlobalUnlock};

        let pointer = unsafe { GlobalLock(memory.handle) };
        if !pointer.is_null() {
            unsafe { std::ptr::write_bytes(pointer.cast::<u8>(), 0, memory.byte_len) };
            let _ = unsafe { GlobalUnlock(memory.handle) };
        }
        let _ = unsafe { Windows::Win32::Foundation::GlobalFree(Some(memory.handle)) };
    }

    fn wait(&mut self, delay: Duration) {
        let milliseconds = u32::try_from(delay.as_millis()).unwrap_or(u32::MAX);
        unsafe { Windows::Win32::System::Threading::Sleep(milliseconds) };
    }
}
