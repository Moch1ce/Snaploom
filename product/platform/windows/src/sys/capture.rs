use std::time::{Duration, Instant};

use windows_core::Interface;

use super::Windows;
use crate::window_catalog::RawWindow;
use snaploom_platform_contract::{PhysicalPoint, PhysicalRect, PlatformError};

const FIRST_FRAME_TIMEOUT: Duration = Duration::from_millis(1_000);
const FRAME_POLL_INTERVAL_MS: u32 = 5;

pub(crate) struct NativeCapture {
    pub(crate) display: PhysicalRect,
    pub(crate) work_area: PhysicalRect,
    pub(crate) pointer: PhysicalPoint,
    pub(crate) pixels: Vec<u8>,
    pub(crate) stride: u32,
    pub(crate) windows: Vec<RawWindow>,
}

pub(crate) fn is_supported() -> bool {
    use Windows::Graphics::Capture::GraphicsCaptureSession;

    let Ok(_apartment) = Apartment::initialize() else {
        return false;
    };
    crate::supports_windows_build(super::version::windows_build())
        && GraphicsCaptureSession::IsSupported().unwrap_or(false)
}

pub(crate) fn capture_current_display() -> Result<NativeCapture, PlatformError> {
    if !is_supported() {
        return Err(PlatformError::PlatformUnavailable);
    }
    let first = capture_once();
    if matches!(
        first,
        Err(PlatformError::DisplayUnavailable | PlatformError::CaptureUnavailable)
    ) {
        capture_once()
    } else {
        first
    }
}

fn capture_once() -> Result<NativeCapture, PlatformError> {
    use Windows::Graphics::Capture::{
        Direct3D11CaptureFramePool, GraphicsCaptureItem, GraphicsCaptureSession,
    };
    use Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;
    use Windows::Graphics::DirectX::DirectXPixelFormat;
    use Windows::Graphics::SizeInt32;
    use Windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL_11_0};
    use Windows::Win32::Graphics::Direct3D11::{
        D3D11_CPU_ACCESS_READ, D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_MAP_READ,
        D3D11_MAPPED_SUBRESOURCE, D3D11_SDK_VERSION, D3D11_TEXTURE2D_DESC, D3D11_USAGE_STAGING,
        D3D11CreateDevice, ID3D11Device, ID3D11DeviceContext, ID3D11Resource, ID3D11Texture2D,
    };
    use Windows::Win32::Graphics::Dxgi::IDXGIDevice;
    use Windows::Win32::System::WinRT::Direct3D11::{
        CreateDirect3D11DeviceFromDXGIDevice, IDirect3DDxgiInterfaceAccess,
    };
    use Windows::Win32::System::WinRT::Graphics::Capture::IGraphicsCaptureItemInterop;

    let _apartment = Apartment::initialize()?;
    if !GraphicsCaptureSession::IsSupported().unwrap_or(false) {
        return Err(PlatformError::PlatformUnavailable);
    }
    let target = resolve_target_display()?;
    let width = target.display.width;
    let height = target.display.height;
    validate_capture_size(width, height)?;
    let size = SizeInt32 {
        Width: i32::try_from(width).map_err(|_| PlatformError::DisplayUnavailable)?,
        Height: i32::try_from(height).map_err(|_| PlatformError::DisplayUnavailable)?,
    };

    let mut device: Option<ID3D11Device> = None;
    let mut context: Option<ID3D11DeviceContext> = None;
    unsafe {
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_HARDWARE,
            Default::default(),
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            Some(&[D3D_FEATURE_LEVEL_11_0]),
            D3D11_SDK_VERSION,
            Some(&mut device),
            None,
            Some(&mut context),
        )
    }
    .map_err(|_| PlatformError::CaptureUnavailable)?;
    let device = device.ok_or(PlatformError::CaptureUnavailable)?;
    let context = context.ok_or(PlatformError::CaptureUnavailable)?;
    let dxgi: IDXGIDevice = device
        .cast()
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    let inspectable = unsafe { CreateDirect3D11DeviceFromDXGIDevice(&dxgi) }
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    let winrt_device: IDirect3DDevice = inspectable
        .cast()
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    let interop: IGraphicsCaptureItemInterop =
        windows_core::factory::<GraphicsCaptureItem, IGraphicsCaptureItemInterop>()
            .map_err(|_| PlatformError::CaptureUnavailable)?;
    let item: GraphicsCaptureItem = unsafe { interop.CreateForMonitor(target.monitor) }
        .map_err(|_| PlatformError::DisplayUnavailable)?;
    let pixel_format = if target.hdr {
        DirectXPixelFormat::R16G16B16A16Float
    } else {
        DirectXPixelFormat::B8G8R8A8UIntNormalized
    };
    let pool = Direct3D11CaptureFramePool::CreateFreeThreaded(&winrt_device, pixel_format, 1, size)
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    let session = pool
        .CreateCaptureSession(&item)
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    session
        .SetIsCursorCaptureEnabled(false)
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    session
        .StartCapture()
        .map_err(|_| PlatformError::CaptureUnavailable)?;

    let frame = wait_for_frame(&pool);
    let _ = session.Close();
    let _ = pool.Close();
    let frame = frame?;
    let content_size = frame
        .ContentSize()
        .map_err(|_| PlatformError::CaptureUnavailable)?;
    if content_size.Width != size.Width || content_size.Height != size.Height {
        let _ = frame.Close();
        return Err(PlatformError::DisplayUnavailable);
    }
    let surface = frame
        .Surface()
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    let access: IDirect3DDxgiInterfaceAccess = surface
        .cast()
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    let source: ID3D11Texture2D =
        unsafe { access.GetInterface() }.map_err(|_| PlatformError::PixelConversionFailed)?;
    let mut source_desc = D3D11_TEXTURE2D_DESC::default();
    unsafe { source.GetDesc(&mut source_desc) };
    if source_desc.Width != width || source_desc.Height != height {
        let _ = frame.Close();
        return Err(PlatformError::DisplayUnavailable);
    }
    let staging_desc = D3D11_TEXTURE2D_DESC {
        Usage: D3D11_USAGE_STAGING,
        BindFlags: 0,
        CPUAccessFlags: D3D11_CPU_ACCESS_READ.0 as u32,
        MiscFlags: 0,
        ..source_desc
    };
    let mut staging: Option<ID3D11Texture2D> = None;
    unsafe { device.CreateTexture2D(&staging_desc, None, Some(&mut staging)) }
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    let staging = staging.ok_or(PlatformError::PixelConversionFailed)?;
    let source_resource: ID3D11Resource = source
        .cast()
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    let staging_resource: ID3D11Resource = staging
        .cast()
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    unsafe { context.CopyResource(&staging_resource, &source_resource) };
    let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
    unsafe { context.Map(&staging_resource, 0, D3D11_MAP_READ, 0, Some(&mut mapped)) }
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    let pixels = copy_mapped_pixels(mapped, width, height, target.hdr);
    unsafe { context.Unmap(&staging_resource, 0) };
    let _ = frame.Close();
    let pixels = pixels?;
    let stride = width
        .checked_mul(4)
        .ok_or(PlatformError::PixelConversionFailed)?;
    let windows = enumerate_windows();
    Ok(NativeCapture {
        display: target.display,
        work_area: target.work_area,
        pointer: target.pointer,
        pixels,
        stride,
        windows,
    })
}

fn wait_for_frame(
    pool: &Windows::Graphics::Capture::Direct3D11CaptureFramePool,
) -> Result<Windows::Graphics::Capture::Direct3D11CaptureFrame, PlatformError> {
    let deadline = Instant::now() + FIRST_FRAME_TIMEOUT;
    loop {
        if let Ok(frame) = pool.TryGetNextFrame() {
            return Ok(frame);
        }
        if Instant::now() >= deadline {
            return Err(PlatformError::FrameTimeout);
        }
        unsafe {
            Windows::Win32::System::Threading::Sleep(FRAME_POLL_INTERVAL_MS);
        }
    }
}

fn copy_mapped_pixels(
    mapped: Windows::Win32::Graphics::Direct3D11::D3D11_MAPPED_SUBRESOURCE,
    width: u32,
    height: u32,
    hdr: bool,
) -> Result<Vec<u8>, PlatformError> {
    let rows = usize::try_from(height).map_err(|_| PlatformError::PixelConversionFailed)?;
    let row_pitch =
        usize::try_from(mapped.RowPitch).map_err(|_| PlatformError::PixelConversionFailed)?;
    if mapped.pData.is_null() {
        return Err(PlatformError::PixelConversionFailed);
    }
    if hdr {
        let source_len = row_pitch
            .checked_mul(rows)
            .ok_or(PlatformError::PixelConversionFailed)?;
        let source = unsafe { std::slice::from_raw_parts(mapped.pData.cast::<u8>(), source_len) };
        return crate::hdr::rgba16f_to_bgra8(
            source,
            row_pitch,
            usize::try_from(width).map_err(|_| PlatformError::PixelConversionFailed)?,
            rows,
        );
    }
    let row_bytes = width
        .checked_mul(4)
        .and_then(|value| usize::try_from(value).ok())
        .ok_or(PlatformError::PixelConversionFailed)?;
    if row_pitch < row_bytes {
        return Err(PlatformError::PixelConversionFailed);
    }
    let output_len = row_bytes
        .checked_mul(rows)
        .ok_or(PlatformError::PixelConversionFailed)?;
    let mut output = vec![0_u8; output_len];
    for row in 0..rows {
        let source = unsafe {
            std::slice::from_raw_parts(mapped.pData.cast::<u8>().add(row * row_pitch), row_bytes)
        };
        let target = &mut output[row * row_bytes..(row + 1) * row_bytes];
        target.copy_from_slice(source);
        for alpha in target[3..].iter_mut().step_by(4) {
            *alpha = 255;
        }
    }
    Ok(output)
}

fn validate_capture_size(width: u32, height: u32) -> Result<(), PlatformError> {
    let bytes = usize::try_from(width)
        .ok()
        .and_then(|width| width.checked_mul(4))
        .and_then(|stride| {
            usize::try_from(height)
                .ok()
                .and_then(|height| stride.checked_mul(height))
        })
        .ok_or(PlatformError::CaptureFrameExceedsLimit)?;
    if bytes > snaploom_platform_contract::MAX_CAPTURE_FRAME_BYTES {
        return Err(PlatformError::CaptureFrameExceedsLimit);
    }
    Ok(())
}

struct Apartment;

impl Apartment {
    fn initialize() -> Result<Self, PlatformError> {
        unsafe {
            Windows::Win32::System::WinRT::RoInitialize(
                Windows::Win32::System::WinRT::RO_INIT_MULTITHREADED,
            )
        }
        .map_err(|_| PlatformError::CaptureUnavailable)?;
        Ok(Self)
    }
}

impl Drop for Apartment {
    fn drop(&mut self) {
        unsafe { Windows::Win32::System::WinRT::RoUninitialize() };
    }
}

struct TargetDisplay {
    monitor: Windows::Win32::Graphics::Gdi::HMONITOR,
    display: PhysicalRect,
    work_area: PhysicalRect,
    pointer: PhysicalPoint,
    hdr: bool,
}

fn resolve_target_display() -> Result<TargetDisplay, PlatformError> {
    use Windows::Win32::Foundation::POINT;
    use Windows::Win32::Graphics::Gdi::{
        GetMonitorInfoW, MONITOR_DEFAULTTONEAREST, MONITORINFO, MonitorFromPoint,
    };
    use Windows::Win32::UI::WindowsAndMessaging::GetCursorPos;

    let mut pointer = POINT::default();
    unsafe { GetCursorPos(&mut pointer) }.map_err(|_| PlatformError::DisplayUnavailable)?;
    let monitor = unsafe { MonitorFromPoint(pointer, MONITOR_DEFAULTTONEAREST) };
    if monitor.is_invalid() {
        return Err(PlatformError::DisplayUnavailable);
    }
    let mut info = MONITORINFO {
        cbSize: u32::try_from(std::mem::size_of::<MONITORINFO>())
            .map_err(|_| PlatformError::DisplayUnavailable)?,
        ..MONITORINFO::default()
    };
    if !unsafe { GetMonitorInfoW(monitor, &mut info) }.as_bool() {
        return Err(PlatformError::DisplayUnavailable);
    }
    let display = rect_from_win32(info.rcMonitor)?;
    let work_area = rect_from_win32(info.rcWork)?;
    Ok(TargetDisplay {
        monitor,
        display,
        work_area,
        pointer: PhysicalPoint {
            x: pointer.x - display.x,
            y: pointer.y - display.y,
        },
        hdr: is_hdr_monitor(monitor),
    })
}

fn is_hdr_monitor(monitor: Windows::Win32::Graphics::Gdi::HMONITOR) -> bool {
    use Windows::Win32::Graphics::Dxgi::Common::{
        DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709, DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020,
    };
    use Windows::Win32::Graphics::Dxgi::{
        CreateDXGIFactory1, IDXGIAdapter1, IDXGIFactory1, IDXGIOutput6,
    };

    let factory: IDXGIFactory1 = match unsafe { CreateDXGIFactory1() } {
        Ok(factory) => factory,
        Err(_) => return false,
    };
    for adapter_index in 0..64 {
        let adapter: IDXGIAdapter1 = match unsafe { factory.EnumAdapters1(adapter_index) } {
            Ok(adapter) => adapter,
            Err(_) => break,
        };
        for output_index in 0..64 {
            let output = match unsafe { adapter.EnumOutputs(output_index) } {
                Ok(output) => output,
                Err(_) => break,
            };
            let output: IDXGIOutput6 = match output.cast() {
                Ok(output) => output,
                Err(_) => continue,
            };
            let descriptor = match unsafe { output.GetDesc1() } {
                Ok(descriptor) => descriptor,
                Err(_) => continue,
            };
            if descriptor.Monitor == monitor {
                return descriptor.BitsPerColor > 8
                    || descriptor.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709
                    || descriptor.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
            }
        }
    }
    false
}

fn rect_from_win32(rect: Windows::Win32::Foundation::RECT) -> Result<PhysicalRect, PlatformError> {
    if rect.right <= rect.left || rect.bottom <= rect.top {
        return Err(PlatformError::DisplayUnavailable);
    }
    Ok(PhysicalRect {
        x: rect.left,
        y: rect.top,
        width: u32::try_from(i64::from(rect.right) - i64::from(rect.left))
            .map_err(|_| PlatformError::DisplayUnavailable)?,
        height: u32::try_from(i64::from(rect.bottom) - i64::from(rect.top))
            .map_err(|_| PlatformError::DisplayUnavailable)?,
    })
}

fn enumerate_windows() -> Vec<RawWindow> {
    use Windows::Win32::Foundation::LPARAM;
    use Windows::Win32::UI::WindowsAndMessaging::EnumWindows;

    let mut windows = Vec::<RawWindow>::new();
    let pointer = (&mut windows as *mut Vec<RawWindow>) as isize;
    if unsafe { EnumWindows(Some(enum_window), LPARAM(pointer)) }.is_err() {
        Vec::new()
    } else {
        windows
    }
}

unsafe extern "system" fn enum_window(
    hwnd: Windows::Win32::Foundation::HWND,
    state: Windows::Win32::Foundation::LPARAM,
) -> windows_core::BOOL {
    let windows = unsafe { &mut *(state.0 as *mut Vec<RawWindow>) };
    if let Some(window) = unsafe { inspect_window(hwnd, windows.len() as u32) } {
        windows.push(window);
    }
    windows_core::BOOL(1)
}

unsafe fn inspect_window(
    hwnd: Windows::Win32::Foundation::HWND,
    z_order: u32,
) -> Option<RawWindow> {
    use Windows::Win32::Foundation::{GetLastError, RECT, SetLastError, WIN32_ERROR};
    use Windows::Win32::Graphics::Dwm::{
        DWMWA_CLOAKED, DWMWA_EXTENDED_FRAME_BOUNDS, DwmGetWindowAttribute,
    };
    use Windows::Win32::UI::WindowsAndMessaging::{
        GW_OWNER, GWL_EXSTYLE, GWL_STYLE, GetClassNameW, GetLayeredWindowAttributes,
        GetShellWindow, GetWindow, GetWindowLongPtrW, GetWindowRect, GetWindowThreadProcessId,
        IsIconic, IsWindowVisible, LWA_ALPHA, WS_CHILD, WS_EX_LAYERED, WS_EX_NOACTIVATE,
        WS_EX_TOOLWINDOW, WS_EX_TRANSPARENT,
    };

    let mut process_id = 0_u32;
    if unsafe { GetWindowThreadProcessId(hwnd, Some(&mut process_id)) } == 0 || process_id == 0 {
        return None;
    }
    unsafe { SetLastError(WIN32_ERROR(0)) };
    let style_value = unsafe { GetWindowLongPtrW(hwnd, GWL_STYLE) };
    if style_value == 0 && unsafe { GetLastError() }.0 != 0 {
        return None;
    }
    unsafe { SetLastError(WIN32_ERROR(0)) };
    let ex_style_value = unsafe { GetWindowLongPtrW(hwnd, GWL_EXSTYLE) };
    if ex_style_value == 0 && unsafe { GetLastError() }.0 != 0 {
        return None;
    }
    let style = style_value as u32;
    let ex_style = ex_style_value as u32;
    let mut class = [0_u16; 128];
    let class_length = unsafe { GetClassNameW(hwnd, &mut class) };
    if class_length <= 0 {
        return None;
    }
    let class_length = class_length as usize;
    let class = String::from_utf16_lossy(&class[..class_length]);
    let system_ui = hwnd == unsafe { GetShellWindow() }
        || matches!(
            class.as_str(),
            "Shell_TrayWnd"
                | "Shell_SecondaryTrayWnd"
                | "Progman"
                | "WorkerW"
                | "#32768"
                | "tooltips_class32"
        );
    let mut bounds = RECT::default();
    if unsafe {
        DwmGetWindowAttribute(
            hwnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            (&mut bounds as *mut RECT).cast(),
            std::mem::size_of::<RECT>() as u32,
        )
    }
    .is_err()
        && unsafe { GetWindowRect(hwnd, &mut bounds) }.is_err()
    {
        return None;
    }
    let bounds = rect_from_win32(bounds).ok()?;
    let mut cloaked = 0_u32;
    unsafe {
        DwmGetWindowAttribute(
            hwnd,
            DWMWA_CLOAKED,
            (&mut cloaked as *mut u32).cast(),
            std::mem::size_of::<u32>() as u32,
        )
    }
    .ok()?;
    let layered = ex_style & WS_EX_LAYERED.0 != 0;
    let mut alpha = 255_u8;
    let mut layered_flags = Default::default();
    if layered {
        unsafe {
            GetLayeredWindowAttributes(hwnd, None, Some(&mut alpha), Some(&mut layered_flags))
        }
        .ok()?;
    }
    Some(RawWindow {
        native_id: hwnd.0 as isize,
        z_order,
        process_id,
        bounds,
        visible: unsafe { IsWindowVisible(hwnd) }.as_bool(),
        minimized: unsafe { IsIconic(hwnd) }.as_bool(),
        child: style & WS_CHILD.0 != 0,
        owned: unsafe { GetWindow(hwnd, GW_OWNER) }.is_ok(),
        tool_window: ex_style & WS_EX_TOOLWINDOW.0 != 0,
        no_activate: ex_style & WS_EX_NOACTIVATE.0 != 0,
        click_through: ex_style & WS_EX_TRANSPARENT.0 != 0,
        cloaked: cloaked != 0,
        transparent: layered && layered_flags.contains(LWA_ALPHA) && alpha == 0,
        system_ui,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_capture_output_larger_than_the_contract_before_allocation() {
        assert_eq!(validate_capture_size(8_192, 8_192), Ok(()));
        assert_eq!(
            validate_capture_size(8_192, 8_193),
            Err(PlatformError::CaptureFrameExceedsLimit)
        );
    }
}
