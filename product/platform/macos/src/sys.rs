use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::mpsc::{self, RecvTimeoutError};
use std::time::Duration;

use block2::RcBlock;
#[cfg(feature = "desktop-shell")]
use objc2::runtime::{AnyObject, NSObjectProtocol, ProtocolObject};
use objc2::{AnyThread, MainThreadMarker, rc::Retained};
#[cfg(feature = "desktop-shell")]
use objc2_app_kit::NSWorkspaceDidWakeNotification;
use objc2_app_kit::{
    NSApplication, NSPasteboard, NSPasteboardTypePNG, NSScreen, NSScreenSaverWindowLevel, NSWindow,
    NSWindowCollectionBehavior, NSWorkspace,
};
use objc2_core_foundation::{
    CFBoolean, CFDictionary, CFNumber, CFNumberType, CFRetained, CGPoint, CGRect, CGSize,
};
use objc2_core_graphics::{
    CGDisplayBounds, CGDisplayPixelsHigh, CGDisplayPixelsWide, CGEvent, CGMainDisplayID,
    CGPreflightScreenCaptureAccess, CGRectMakeWithDictionaryRepresentation,
    CGRequestScreenCaptureAccess, CGWindowListCopyWindowInfo, CGWindowListOption,
    kCGColorSpaceSRGB, kCGNullWindowID, kCGWindowAlpha, kCGWindowBounds, kCGWindowIsOnscreen,
    kCGWindowLayer, kCGWindowNumber, kCGWindowOwnerPID,
};
use objc2_core_media::CMSampleBuffer;
use objc2_core_video::{
    CVPixelBufferGetBaseAddress, CVPixelBufferGetBytesPerRow, CVPixelBufferGetHeight,
    CVPixelBufferGetPixelFormatType, CVPixelBufferGetPlaneCount, CVPixelBufferGetWidth,
    CVPixelBufferIsPlanar, CVPixelBufferLockBaseAddress, CVPixelBufferLockFlags,
    CVPixelBufferUnlockBaseAddress, kCVPixelFormatType_32BGRA, kCVReturnSuccess,
};
use objc2_foundation::{NSArray, NSData, NSError, NSString, NSThread, NSURL};
#[cfg(feature = "desktop-shell")]
use objc2_foundation::{NSNotification, NSNotificationCenter};
use objc2_screen_capture_kit::{
    SCContentFilter, SCDisplay, SCScreenshotManager, SCShareableContent, SCStreamConfiguration,
    SCStreamErrorCode, SCStreamErrorDomain,
};
#[cfg(feature = "desktop-shell")]
use objc2_service_management::{SMAppService, SMAppServiceStatus};
use snaploom_platform_contract::{
    CapturePermissionState, CaptureSnapshot, CaptureSnapshotDescriptor, LogicalRect, LogicalSize,
    PhysicalSize, PlatformError, PremultipliedBgraFrame,
};

use crate::coordinates::{DisplayGeometry, PointRect, appkit_frame};
use crate::frame::{SensitiveBgraFrame, copy_bgra_rows};
use crate::window_catalog::{CgWindowRecord, SckWindowRecord, merge_windows};

const NATIVE_TIMEOUT: Duration = Duration::from_secs(5);

#[cfg(feature = "desktop-shell")]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum AutoStartState {
    Disabled,
    Enabled,
    RequiresApproval,
}

pub(crate) fn preflight_screen_capture_access() -> bool {
    CGPreflightScreenCaptureAccess()
}

pub(crate) fn request_screen_capture_access() -> CapturePermissionState {
    let accepted = CGRequestScreenCaptureAccess();
    if CGPreflightScreenCaptureAccess() {
        CapturePermissionState::Granted
    } else if accepted {
        CapturePermissionState::RestartRequired
    } else {
        CapturePermissionState::NotGranted
    }
}

pub(crate) fn open_screen_capture_settings() -> Result<(), PlatformError> {
    let workspace = NSWorkspace::sharedWorkspace();
    // The concrete Screen Recording pane URL is not a stable contract. Try it
    // as a best-effort convenience and fall back to Privacy & Security.
    for value in [
        "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture",
        "x-apple.systempreferences:com.apple.settings.PrivacySecurity",
    ] {
        let string = NSString::from_str(value);
        if let Some(url) = NSURL::URLWithString(&string)
            && workspace.openURL(&url)
        {
            return Ok(());
        }
    }
    Err(PlatformError::PermissionSettingsUnavailable)
}

pub(crate) fn is_main_thread() -> bool {
    NSThread::isMainThread_class()
}

pub(crate) fn write_png(png: &[u8]) -> Result<(), PlatformError> {
    let pasteboard = NSPasteboard::generalPasteboard();
    write_png_to_pasteboard(&pasteboard, png)
}

fn write_png_to_pasteboard(pasteboard: &NSPasteboard, png: &[u8]) -> Result<(), PlatformError> {
    pasteboard.clearContents();
    let data = NSData::with_bytes(png);
    if !pasteboard.setData_forType(Some(&data), unsafe { NSPasteboardTypePNG }) {
        return Err(PlatformError::ClipboardWriteFailed);
    }
    let round_trip = pasteboard
        .dataForType(unsafe { NSPasteboardTypePNG })
        .ok_or(PlatformError::ClipboardWriteFailed)?;
    if round_trip.to_vec() == png {
        Ok(())
    } else {
        Err(PlatformError::ClipboardWriteFailed)
    }
}

#[cfg(feature = "desktop-shell")]
pub(crate) fn autostart_state() -> AutoStartState {
    let service = unsafe { SMAppService::mainAppService() };
    match unsafe { service.status() } {
        SMAppServiceStatus::Enabled => AutoStartState::Enabled,
        SMAppServiceStatus::RequiresApproval => AutoStartState::RequiresApproval,
        SMAppServiceStatus::NotRegistered | SMAppServiceStatus::NotFound => {
            AutoStartState::Disabled
        }
        _ => AutoStartState::Disabled,
    }
}

#[cfg(feature = "desktop-shell")]
pub(crate) fn set_autostart(enabled: bool) -> Result<(), PlatformError> {
    let service = unsafe { SMAppService::mainAppService() };
    let changed = if enabled {
        unsafe { service.registerAndReturnError() }
    } else {
        unsafe { service.unregisterAndReturnError() }
    };
    if changed.is_err() {
        return match autostart_state() {
            AutoStartState::RequiresApproval => Err(PlatformError::AutoStartNeedsApproval),
            _ => Err(PlatformError::AutoStartFailed),
        };
    }
    match (enabled, autostart_state()) {
        (true, AutoStartState::Enabled) | (false, AutoStartState::Disabled) => Ok(()),
        (_, AutoStartState::RequiresApproval) => Err(PlatformError::AutoStartNeedsApproval),
        _ => Err(PlatformError::AutoStartFailed),
    }
}

/// Must be called on AppKit's main thread with a live Tauri NSWindow.
pub(crate) unsafe fn configure_overlay(
    ns_window: *mut c_void,
    display_id: u32,
    expected: PhysicalSize,
) -> Result<LogicalRect, PlatformError> {
    let window =
        unsafe { ns_window.cast::<NSWindow>().as_ref() }.ok_or(PlatformError::OverlayFailed)?;
    let mtm = MainThreadMarker::new().ok_or(PlatformError::OverlayFailed)?;
    if CGDisplayPixelsWide(display_id) != expected.width as usize
        || CGDisplayPixelsHigh(display_id) != expected.height as usize
    {
        return Err(PlatformError::DisplayUnavailable);
    }
    let cg_bounds = CGDisplayBounds(display_id);
    let primary_height = CGDisplayBounds(CGMainDisplayID()).size.height;
    let mapped = appkit_frame(
        PointRect {
            x: cg_bounds.origin.x,
            y: cg_bounds.origin.y,
            width: cg_bounds.size.width,
            height: cg_bounds.size.height,
        },
        primary_height,
    )
    .map_err(|_| PlatformError::DisplayUnavailable)?;
    let appkit_frame = CGRect::new(
        CGPoint::new(mapped.x, mapped.y),
        CGSize::new(mapped.width, mapped.height),
    );
    let screens = NSScreen::screens(mtm);
    let screen = (0..screens.count())
        .map(|index| screens.objectAtIndex(index))
        .find(|screen| approximately_equal_rect(screen.frame(), appkit_frame))
        .ok_or(PlatformError::DisplayUnavailable)?;
    let visible = screen.visibleFrame();
    window.setFrame_display(screen.frame(), true);
    window.setLevel(NSScreenSaverWindowLevel);
    window.setCollectionBehavior(
        NSWindowCollectionBehavior::CanJoinAllSpaces
            | NSWindowCollectionBehavior::FullScreenAuxiliary
            | NSWindowCollectionBehavior::Stationary,
    );
    Ok(LogicalRect {
        x: visible.origin.x - appkit_frame.origin.x,
        y: appkit_frame.origin.y + appkit_frame.size.height
            - visible.origin.y
            - visible.size.height,
        width: visible.size.width,
        height: visible.size.height,
    })
}

pub(crate) fn activate_accessory_app() -> Result<(), PlatformError> {
    let mtm = MainThreadMarker::new().ok_or(PlatformError::SaveDialogFailed)?;
    NSApplication::sharedApplication(mtm).activate();
    Ok(())
}

fn approximately_equal_rect(left: CGRect, right: CGRect) -> bool {
    let values = [
        (left.origin.x, right.origin.x),
        (left.origin.y, right.origin.y),
        (left.size.width, right.size.width),
        (left.size.height, right.size.height),
    ];
    values
        .into_iter()
        .all(|(left, right)| (left - right).abs() <= 0.5)
}

#[cfg(feature = "desktop-shell")]
pub(crate) struct ResumeLease {
    center: Retained<NSNotificationCenter>,
    observer: Retained<ProtocolObject<dyn NSObjectProtocol>>,
}

// NSNotificationCenter is documented as thread-safe. The token is only used
// for deregistration and both Objective-C objects are retained for the lease.
#[cfg(feature = "desktop-shell")]
unsafe impl Send for ResumeLease {}
#[cfg(feature = "desktop-shell")]
unsafe impl Sync for ResumeLease {}

#[cfg(feature = "desktop-shell")]
impl std::fmt::Debug for ResumeLease {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("ResumeLease")
            .finish_non_exhaustive()
    }
}

#[cfg(feature = "desktop-shell")]
impl Drop for ResumeLease {
    fn drop(&mut self) {
        let observer = Retained::as_ptr(&self.observer).cast::<AnyObject>();
        // SAFETY: the retained notification token is an Objective-C object and
        // remains live until this method returns.
        unsafe { self.center.removeObserver(&*observer) };
    }
}

#[cfg(feature = "desktop-shell")]
pub(crate) fn watch_resume(
    callback: std::sync::Arc<dyn Fn() + Send + Sync + 'static>,
) -> ResumeLease {
    let workspace = NSWorkspace::sharedWorkspace();
    let center = workspace.notificationCenter();
    let block = RcBlock::new(move |_notification: NonNull<NSNotification>| callback());
    let observer = unsafe {
        center.addObserverForName_object_queue_usingBlock(
            Some(NSWorkspaceDidWakeNotification),
            None::<&AnyObject>,
            None,
            &block,
        )
    };
    ResumeLease { center, observer }
}

pub(crate) fn capture_current_display() -> Result<CaptureSnapshot, PlatformError> {
    if !preflight_screen_capture_access() {
        return Err(PlatformError::PermissionNotGranted);
    }
    for _ in 0..2 {
        match capture_once() {
            Err(PlatformError::DisplayUnavailable) => continue,
            result => return result,
        }
    }
    Err(PlatformError::DisplayUnavailable)
}

struct NativeCapture {
    display_id: u32,
    geometry: DisplayGeometry,
    pointer: (f64, f64),
    windows: Vec<SckWindowRecord>,
    pixels: SensitiveBgraFrame,
    stride: u32,
}

struct PendingCapture {
    display_id: u32,
    geometry: DisplayGeometry,
    pointer: (f64, f64),
    windows: Vec<SckWindowRecord>,
    before_bounds: CGRect,
}

fn capture_once() -> Result<CaptureSnapshot, PlatformError> {
    let pointer = current_pointer()?;
    let native = native_capture(pointer)?;

    let mut random = [0_u8; 32];
    getrandom::fill(&mut random).map_err(|_| PlatformError::InternalState)?;
    let session_id = format!("macos-{}", encode_hex(&random[..16]));
    let window_secret: [u8; 16] = random[16..].try_into().expect("fixed random secret slice");
    let windows = merge_windows(
        native.windows,
        &cg_windows(),
        i32::try_from(std::process::id()).unwrap_or(i32::MAX),
        native.geometry,
        &window_secret,
    );
    let descriptor = CaptureSnapshotDescriptor {
        session_id,
        display_id: Some(u64::from(native.display_id)),
        physical_size: native.geometry.physical_size,
        logical_size: LogicalSize {
            width: native.geometry.content_points.width,
            height: native.geometry.content_points.height,
        },
        global_origin: None,
        pointer_physical: native.geometry.pointer_local(native.pointer),
        work_area_logical: LogicalRect {
            x: 0.0,
            y: 0.0,
            width: native.geometry.content_points.width,
            height: native.geometry.content_points.height,
        },
        stride: native.stride,
        windows,
    };
    let frame = PremultipliedBgraFrame::new(&descriptor, native.pixels.into_bytes())?;
    Ok(CaptureSnapshot::new(descriptor, frame))
}

fn current_pointer() -> Result<(f64, f64), PlatformError> {
    let event = CGEvent::new(None).ok_or(PlatformError::DisplayUnavailable)?;
    let location = CGEvent::location(Some(&event));
    Ok((location.x, location.y))
}

fn native_capture(pointer: (f64, f64)) -> Result<NativeCapture, PlatformError> {
    let (sender, receiver) = mpsc::sync_channel(1);
    let block = RcBlock::new(
        move |content: *mut SCShareableContent, error: *mut NSError| {
            if !error.is_null() {
                let _ = sender.send(Err(classify_sck_error(error)));
                return;
            }
            let Some(content) = (unsafe { content.as_ref() }) else {
                let _ = sender.send(Err(PlatformError::CaptureUnavailable));
                return;
            };
            if let Err(error) = begin_screenshot(content, pointer, sender.clone()) {
                let _ = sender.send(Err(error));
            }
        },
    );
    unsafe {
        SCShareableContent::getShareableContentExcludingDesktopWindows_onScreenWindowsOnly_completionHandler(
            true,
            true,
            &block,
        );
    }
    match receiver.recv_timeout(NATIVE_TIMEOUT) {
        Ok(result) => result,
        Err(RecvTimeoutError::Timeout) => Err(PlatformError::FrameTimeout),
        Err(RecvTimeoutError::Disconnected) => Err(PlatformError::CaptureUnavailable),
    }
}

fn choose_display(
    content: &SCShareableContent,
    pointer: (f64, f64),
) -> Result<Retained<SCDisplay>, PlatformError> {
    let displays = unsafe { content.displays() };
    let mut candidates = Vec::new();
    for index in 0..displays.count() {
        let display = displays.objectAtIndex(index);
        let display_id = unsafe { display.displayID() };
        let bounds = CGDisplayBounds(display_id);
        let inside = pointer.0 >= bounds.origin.x
            && pointer.1 >= bounds.origin.y
            && pointer.0 < bounds.origin.x + bounds.size.width
            && pointer.1 < bounds.origin.y + bounds.size.height;
        if inside {
            candidates.push((display_id, display));
        }
    }
    candidates.sort_by_key(|(id, _)| *id);
    candidates
        .into_iter()
        .next()
        .map(|(_, display)| display)
        .ok_or(PlatformError::DisplayUnavailable)
}

fn begin_screenshot(
    content: &SCShareableContent,
    pointer: (f64, f64),
    sender: mpsc::SyncSender<Result<NativeCapture, PlatformError>>,
) -> Result<(), PlatformError> {
    let display = choose_display(content, pointer)?;
    let display_id = unsafe { display.displayID() };
    let bounds = CGDisplayBounds(display_id);
    let excluded = NSArray::from_slice(&[]);
    let filter = unsafe {
        SCContentFilter::initWithDisplay_excludingWindows(
            SCContentFilter::alloc(),
            &display,
            &excluded,
        )
    };
    unsafe { filter.setIncludeMenuBar(true) };
    let info = unsafe { SCShareableContent::infoForFilter(&filter) };
    let content_rect = unsafe { info.contentRect() };
    let scale = f64::from(unsafe { info.pointPixelScale() });
    let geometry = DisplayGeometry {
        global_points: PointRect {
            x: bounds.origin.x,
            y: bounds.origin.y,
            width: bounds.size.width,
            height: bounds.size.height,
        },
        content_points: PointRect {
            x: content_rect.origin.x,
            y: content_rect.origin.y,
            width: content_rect.size.width,
            height: content_rect.size.height,
        },
        physical_size: PhysicalSize {
            width: checked_dimension(content_rect.size.width * scale)?,
            height: checked_dimension(content_rect.size.height * scale)?,
        },
    }
    .validate()?;
    let pending = PendingCapture {
        display_id,
        geometry,
        pointer,
        windows: sck_windows(content),
        before_bounds: bounds,
    };

    let configuration = unsafe { SCStreamConfiguration::new() };
    unsafe {
        configuration.setWidth(geometry.physical_size.width as usize);
        configuration.setHeight(geometry.physical_size.height as usize);
        configuration.setPixelFormat(kCVPixelFormatType_32BGRA);
        configuration.setShowsCursor(false);
        configuration.setCapturesAudio(false);
        configuration.setColorSpaceName(kCGColorSpaceSRGB);
    }
    let block = RcBlock::new(move |sample: *mut CMSampleBuffer, error: *mut NSError| {
        if !error.is_null() {
            let _ = sender.send(Err(classify_sck_error(error)));
            return;
        }
        let Some(pointer) = NonNull::new(sample) else {
            let _ = sender.send(Err(PlatformError::CaptureUnavailable));
            return;
        };
        // Keep the sample alive only inside this callback. The channel carries
        // owned Rust bytes, never framework pointers or retained objects.
        let retained = unsafe { CFRetained::retain(pointer) };
        let result =
            copy_sample(&retained, pending.geometry.physical_size).and_then(|(pixels, stride)| {
                if CGDisplayBounds(pending.display_id) != pending.before_bounds {
                    return Err(PlatformError::DisplayUnavailable);
                }
                Ok(NativeCapture {
                    display_id: pending.display_id,
                    geometry: pending.geometry,
                    pointer: pending.pointer,
                    windows: pending.windows.clone(),
                    pixels,
                    stride,
                })
            });
        // A late completion after timeout safely drops (and zeroizes) pixels
        // when the receiver no longer exists.
        let _ = sender.send(result);
    });
    unsafe {
        SCScreenshotManager::captureSampleBufferWithFilter_configuration_completionHandler(
            &filter,
            &configuration,
            Some(&block),
        );
    }
    Ok(())
}

fn copy_sample(
    sample: &CMSampleBuffer,
    expected: PhysicalSize,
) -> Result<(SensitiveBgraFrame, u32), PlatformError> {
    let image = unsafe { sample.image_buffer() }.ok_or(PlatformError::PixelConversionFailed)?;
    if CVPixelBufferGetPixelFormatType(&image) != kCVPixelFormatType_32BGRA
        || CVPixelBufferGetWidth(&image) != expected.width as usize
        || CVPixelBufferGetHeight(&image) != expected.height as usize
        || CVPixelBufferIsPlanar(&image)
        || CVPixelBufferGetPlaneCount(&image) != 0
    {
        return Err(PlatformError::PixelConversionFailed);
    }
    let flags = CVPixelBufferLockFlags::ReadOnly;
    if unsafe { CVPixelBufferLockBaseAddress(&image, flags) } != kCVReturnSuccess {
        return Err(PlatformError::PixelConversionFailed);
    }
    let result = copy_locked_pixel_buffer(&image, expected);
    let unlocked = unsafe { CVPixelBufferUnlockBaseAddress(&image, flags) };
    if unlocked != kCVReturnSuccess {
        return Err(PlatformError::PixelConversionFailed);
    }
    result
}

fn copy_locked_pixel_buffer(
    image: &objc2_core_video::CVPixelBuffer,
    expected: PhysicalSize,
) -> Result<(SensitiveBgraFrame, u32), PlatformError> {
    let stride = CVPixelBufferGetBytesPerRow(image);
    let length = stride
        .checked_mul(expected.height as usize)
        .filter(|length| *length <= snaploom_platform_contract::MAX_CAPTURE_FRAME_BYTES)
        .ok_or(PlatformError::PixelConversionFailed)?;
    let base = CVPixelBufferGetBaseAddress(image).cast::<u8>();
    if base.is_null() {
        return Err(PlatformError::PixelConversionFailed);
    }
    // SAFETY: the pixel buffer is read-locked and validated for `length` bytes.
    let source = unsafe { std::slice::from_raw_parts(base, length) };
    copy_bgra_rows(source, expected.width, expected.height, stride)
}

fn classify_sck_error(error: *mut NSError) -> PlatformError {
    let Some(error) = (unsafe { error.as_ref() }) else {
        return PlatformError::CaptureUnavailable;
    };
    let domain = error.domain();
    let user_declined = domain.isEqualToString(unsafe { SCStreamErrorDomain })
        && error.code() == SCStreamErrorCode::UserDeclined.0;
    if user_declined || !preflight_screen_capture_access() {
        PlatformError::PermissionRevoked
    } else {
        PlatformError::CaptureUnavailable
    }
}

fn sck_windows(content: &SCShareableContent) -> Vec<SckWindowRecord> {
    let windows = unsafe { content.windows() };
    (0..windows.count())
        .filter_map(|index| {
            let window = windows.objectAtIndex(index);
            let owner = unsafe { window.owningApplication() }?;
            let frame = unsafe { window.frame() };
            Some(SckWindowRecord {
                window_id: unsafe { window.windowID() },
                owner_pid: unsafe { owner.processID() },
                owner_bundle: Some(unsafe { owner.bundleIdentifier() }.to_string()),
                layer: unsafe { window.windowLayer() } as i64,
                on_screen: unsafe { window.isOnScreen() },
                frame: PointRect {
                    x: frame.origin.x,
                    y: frame.origin.y,
                    width: frame.size.width,
                    height: frame.size.height,
                },
            })
        })
        .collect()
}

fn cg_windows() -> Vec<CgWindowRecord> {
    let options =
        CGWindowListOption::OptionOnScreenOnly | CGWindowListOption::ExcludeDesktopElements;
    let Some(array) = CGWindowListCopyWindowInfo(options, kCGNullWindowID) else {
        return Vec::new();
    };
    let mut records = Vec::new();
    for index in 0..array.count() {
        let raw = unsafe { array.value_at_index(index) };
        if raw.is_null() {
            continue;
        }
        // SAFETY: CGWindowListCopyWindowInfo returns an array of CFDictionary values.
        let dictionary = unsafe { &*raw.cast::<CFDictionary>() };
        let Some(window_id) = dictionary_i64(dictionary, unsafe { kCGWindowNumber })
            .and_then(|value| u32::try_from(value).ok())
        else {
            continue;
        };
        let Some(owner_pid) = dictionary_i64(dictionary, unsafe { kCGWindowOwnerPID })
            .and_then(|value| i32::try_from(value).ok())
        else {
            continue;
        };
        let layer = dictionary_i64(dictionary, unsafe { kCGWindowLayer })
            .and_then(|value| i32::try_from(value).ok())
            .unwrap_or(i32::MAX);
        let alpha = dictionary_f64(dictionary, unsafe { kCGWindowAlpha }).unwrap_or(0.0);
        let on_screen = dictionary_bool(dictionary, unsafe { kCGWindowIsOnscreen }).unwrap_or(true);
        // Validate the public bounds payload even though SCK geometry is the capture truth.
        if dictionary_rect(dictionary, unsafe { kCGWindowBounds }).is_none() {
            continue;
        }
        records.push(CgWindowRecord {
            window_id,
            owner_pid,
            layer,
            alpha,
            on_screen,
        });
    }
    records
}

fn dictionary_value(
    dictionary: &CFDictionary,
    key: &objc2_core_foundation::CFString,
) -> *const c_void {
    unsafe { dictionary.value((key as *const _) as *const c_void) }
}

fn dictionary_i64(dictionary: &CFDictionary, key: &objc2_core_foundation::CFString) -> Option<i64> {
    let raw = dictionary_value(dictionary, key);
    if raw.is_null() {
        return None;
    }
    let mut value = 0_i64;
    // SAFETY: CoreGraphics documents these keys as CFNumber values.
    unsafe {
        (&*raw.cast::<CFNumber>()).value(CFNumberType::SInt64Type, (&mut value as *mut i64).cast())
    }
    .then_some(value)
}

fn dictionary_f64(dictionary: &CFDictionary, key: &objc2_core_foundation::CFString) -> Option<f64> {
    let raw = dictionary_value(dictionary, key);
    if raw.is_null() {
        return None;
    }
    let mut value = 0.0_f64;
    // SAFETY: CoreGraphics documents these keys as CFNumber values.
    unsafe {
        (&*raw.cast::<CFNumber>()).value(CFNumberType::Float64Type, (&mut value as *mut f64).cast())
    }
    .then_some(value)
}

fn dictionary_bool(
    dictionary: &CFDictionary,
    key: &objc2_core_foundation::CFString,
) -> Option<bool> {
    let raw = dictionary_value(dictionary, key);
    (!raw.is_null()).then(|| unsafe { (&*raw.cast::<CFBoolean>()).value() })
}

fn dictionary_rect(
    dictionary: &CFDictionary,
    key: &objc2_core_foundation::CFString,
) -> Option<objc2_core_foundation::CGRect> {
    let raw = dictionary_value(dictionary, key);
    if raw.is_null() {
        return None;
    }
    let mut rect = objc2_core_foundation::CGRect::ZERO;
    // SAFETY: CoreGraphics documents kCGWindowBounds as a CGRect dictionary and
    // `rect` is a valid initialized output location.
    unsafe { CGRectMakeWithDictionaryRepresentation(Some(&*raw.cast::<CFDictionary>()), &mut rect) }
        .then_some(rect)
}

fn checked_dimension(value: f64) -> Result<u32, PlatformError> {
    if !value.is_finite() || value <= 0.0 || value > f64::from(u32::MAX) {
        Err(PlatformError::InvalidCaptureDescriptor)
    } else {
        Ok(value.round() as u32)
    }
}

fn encode_hex(bytes: &[u8]) -> String {
    use std::fmt::Write as _;
    let mut encoded = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        write!(&mut encoded, "{byte:02x}").expect("writing to String cannot fail");
    }
    encoded
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn private_pasteboard_round_trips_exact_png_bytes() {
        let pasteboard = NSPasteboard::pasteboardWithUniqueName();
        let bytes = b"\x89PNG\r\n\x1a\nexact-private-pasteboard-payload";
        write_png_to_pasteboard(&pasteboard, bytes).unwrap();
        assert_eq!(
            pasteboard
                .dataForType(unsafe { NSPasteboardTypePNG })
                .unwrap()
                .to_vec(),
            bytes
        );
    }
}
