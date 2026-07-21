use std::{env, fs, path::PathBuf, process::Command};

const FILTERS: &[&str] = &[
    // Windows Graphics Capture and its WinRT/D3D interop.
    "Windows.Graphics.Capture.Direct3D11CaptureFrame",
    "Windows.Graphics.Capture.Direct3D11CaptureFramePool",
    "Windows.Graphics.Capture.GraphicsCaptureItem",
    "Windows.Graphics.Capture.GraphicsCaptureSession",
    "Windows.Graphics.DirectX.DirectXPixelFormat",
    "Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice",
    "Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface",
    "Windows.Graphics.SizeInt32",
    "Windows.Win32.System.WinRT.Direct3D11.CreateDirect3D11DeviceFromDXGIDevice",
    "Windows.Win32.System.WinRT.Direct3D11.IDirect3DDxgiInterfaceAccess",
    "Windows.Win32.System.WinRT.Graphics.Capture.IGraphicsCaptureItemInterop",
    // D3D11 texture creation, GPU copy and mapped staging reads.
    "Windows.Win32.Graphics.Direct3D.D3D_DRIVER_TYPE_HARDWARE",
    "Windows.Win32.Graphics.Direct3D.D3D_FEATURE_LEVEL_11_0",
    "Windows.Win32.Graphics.Direct3D11.D3D11CreateDevice",
    "Windows.Win32.Graphics.Direct3D11.D3D11_CPU_ACCESS_READ",
    "Windows.Win32.Graphics.Direct3D11.D3D11_CREATE_DEVICE_BGRA_SUPPORT",
    "Windows.Win32.Graphics.Direct3D11.D3D11_MAP_READ",
    "Windows.Win32.Graphics.Direct3D11.D3D11_MAPPED_SUBRESOURCE",
    "Windows.Win32.Graphics.Direct3D11.D3D11_SDK_VERSION",
    "Windows.Win32.Graphics.Direct3D11.D3D11_SUBRESOURCE_DATA",
    "Windows.Win32.Graphics.Direct3D11.D3D11_TEXTURE2D_DESC",
    "Windows.Win32.Graphics.Direct3D11.D3D11_USAGE_STAGING",
    "Windows.Win32.Graphics.Direct3D11.ID3D11Device",
    "Windows.Win32.Graphics.Direct3D11.ID3D11DeviceContext",
    "Windows.Win32.Graphics.Direct3D11.ID3D11Resource",
    "Windows.Win32.Graphics.Direct3D11.ID3D11Texture2D",
    "Windows.Win32.Graphics.Dxgi.IDXGIDevice",
    "Windows.Win32.Graphics.Dxgi.CreateDXGIFactory1",
    "Windows.Win32.Graphics.Dxgi.DXGI_OUTPUT_DESC",
    "Windows.Win32.Graphics.Dxgi.DXGI_OUTPUT_DESC1",
    "Windows.Win32.Graphics.Dxgi.IDXGIAdapter1",
    "Windows.Win32.Graphics.Dxgi.IDXGIFactory1",
    "Windows.Win32.Graphics.Dxgi.IDXGIOutput",
    "Windows.Win32.Graphics.Dxgi.IDXGIOutput6",
    "Windows.Win32.Graphics.Dxgi.Common.DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709",
    "Windows.Win32.Graphics.Dxgi.Common.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020",
    "Windows.Win32.Graphics.Dxgi.Common.DXGI_FORMAT",
    "Windows.Win32.Graphics.Dxgi.Common.DXGI_SAMPLE_DESC",
    // Monitor/window catalog, DWM filtering, PMv2 and topmost overlay.
    "Windows.Win32.Graphics.Dwm.DwmGetWindowAttribute",
    "Windows.Win32.Graphics.Dwm.DWMWA_CLOAKED",
    "Windows.Win32.Graphics.Dwm.DWMWA_EXTENDED_FRAME_BOUNDS",
    "Windows.Win32.Graphics.Gdi.GetMonitorInfoW",
    "Windows.Win32.Graphics.Gdi.HMONITOR",
    "Windows.Win32.Graphics.Gdi.MONITOR_DEFAULTTONEAREST",
    "Windows.Win32.Graphics.Gdi.MONITORINFO",
    "Windows.Win32.Graphics.Gdi.MonitorFromPoint",
    "Windows.Win32.UI.HiDpi.GetDpiForWindow",
    "Windows.Win32.UI.WindowsAndMessaging.EnumWindows",
    "Windows.Win32.UI.WindowsAndMessaging.GetClassNameW",
    "Windows.Win32.UI.WindowsAndMessaging.GetCursorPos",
    "Windows.Win32.UI.WindowsAndMessaging.GetLayeredWindowAttributes",
    "Windows.Win32.UI.WindowsAndMessaging.GetShellWindow",
    "Windows.Win32.UI.WindowsAndMessaging.GetWindow",
    "Windows.Win32.UI.WindowsAndMessaging.GetWindowLongPtrW",
    "Windows.Win32.UI.WindowsAndMessaging.GetWindowRect",
    "Windows.Win32.UI.WindowsAndMessaging.GetWindowThreadProcessId",
    "Windows.Win32.UI.WindowsAndMessaging.GWL_EXSTYLE",
    "Windows.Win32.UI.WindowsAndMessaging.GWL_STYLE",
    "Windows.Win32.UI.WindowsAndMessaging.GW_OWNER",
    "Windows.Win32.UI.WindowsAndMessaging.HWND_TOPMOST",
    "Windows.Win32.UI.WindowsAndMessaging.IsIconic",
    "Windows.Win32.UI.WindowsAndMessaging.IsWindowVisible",
    "Windows.Win32.UI.WindowsAndMessaging.LWA_ALPHA",
    "Windows.Win32.UI.WindowsAndMessaging.SetWindowPos",
    "Windows.Win32.UI.WindowsAndMessaging.SWP_NOACTIVATE",
    "Windows.Win32.UI.WindowsAndMessaging.SWP_NOMOVE",
    "Windows.Win32.UI.WindowsAndMessaging.SWP_NOSIZE",
    "Windows.Win32.UI.WindowsAndMessaging.WS_CHILD",
    "Windows.Win32.UI.WindowsAndMessaging.WS_EX_LAYERED",
    "Windows.Win32.UI.WindowsAndMessaging.WS_EX_NOACTIVATE",
    "Windows.Win32.UI.WindowsAndMessaging.WS_EX_TOOLWINDOW",
    "Windows.Win32.UI.WindowsAndMessaging.WS_EX_TRANSPARENT",
    // Registered PNG clipboard ownership and suspend/resume observation.
    "Windows.Win32.System.DataExchange.CloseClipboard",
    "Windows.Win32.System.DataExchange.EmptyClipboard",
    "Windows.Win32.System.DataExchange.OpenClipboard",
    "Windows.Win32.System.DataExchange.RegisterClipboardFormatW",
    "Windows.Win32.System.DataExchange.SetClipboardData",
    "Windows.Win32.Foundation.GlobalFree",
    "Windows.Win32.Foundation.GetLastError",
    "Windows.Win32.Foundation.SetLastError",
    "Windows.Win32.System.Memory.GlobalAlloc",
    "Windows.Win32.System.Memory.GlobalLock",
    "Windows.Win32.System.Memory.GlobalUnlock",
    "Windows.Win32.System.Memory.GMEM_MOVEABLE",
    "Windows.Win32.System.Com.CLSCTX_INPROC_SERVER",
    "Windows.Win32.System.Com.COINIT_APARTMENTTHREADED",
    "Windows.Win32.System.Com.CoCreateInstance",
    "Windows.Win32.System.Com.CoInitializeEx",
    "Windows.Win32.System.Com.CoTaskMemFree",
    "Windows.Win32.System.Com.CoUninitialize",
    "Windows.Win32.Storage.FileSystem.MoveFileExW",
    "Windows.Win32.Storage.FileSystem.MOVEFILE_REPLACE_EXISTING",
    "Windows.Win32.Storage.FileSystem.MOVEFILE_WRITE_THROUGH",
    "Windows.Win32.System.Power.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS",
    "Windows.Win32.System.Power.PowerRegisterSuspendResumeNotification",
    "Windows.Win32.System.Power.PowerUnregisterSuspendResumeNotification",
    "Windows.Win32.System.SystemInformation.GetVersionExW",
    "Windows.Win32.System.SystemInformation.OSVERSIONINFOW",
    "Windows.Win32.System.Threading.GetCurrentProcessId",
    "Windows.Win32.System.Threading.Sleep",
    "Windows.Win32.System.WinRT.RO_INIT_MULTITHREADED",
    "Windows.Win32.System.WinRT.RoInitialize",
    "Windows.Win32.System.WinRT.RoUninitialize",
    "Windows.Win32.UI.WindowsAndMessaging.DEVICE_NOTIFY_CALLBACK",
    "Windows.Win32.UI.WindowsAndMessaging.PBT_APMRESUMEAUTOMATIC",
    // Native save dialog with distinguishable cancel and failure HRESULTs.
    "Windows.Win32.UI.Shell.Common.COMDLG_FILTERSPEC",
    "Windows.Win32.UI.Shell.FOS_FORCEFILESYSTEM",
    "Windows.Win32.UI.Shell.FOS_OVERWRITEPROMPT",
    "Windows.Win32.UI.Shell.FOS_PATHMUSTEXIST",
    "Windows.Win32.UI.Shell.FileSaveDialog",
    "Windows.Win32.UI.Shell.IFileSaveDialog",
    "Windows.Win32.UI.Shell.IShellItem",
    "Windows.Win32.UI.Shell.SHCreateItemFromParsingName",
    "Windows.Win32.UI.Shell.SIGDN_FILESYSPATH",
];

fn output_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("product/platform/windows/src/sys/bindings.rs")
}

fn generate(path: &std::path::Path) {
    let output = path
        .to_str()
        .unwrap_or_else(|| panic!("non-UTF-8 binding output path"));
    let rustfmt = Command::new("rustup")
        .args(["which", "rustfmt"])
        .output()
        .expect("locate rustfmt through rustup");
    assert!(rustfmt.status.success(), "rustfmt is not installed");
    let rustfmt = String::from_utf8(rustfmt.stdout)
        .expect("rustfmt path is not UTF-8")
        .trim()
        .to_owned();
    let mut args = vec![
        "--out",
        output,
        "--no-toml",
        "--rustfmt",
        rustfmt.as_str(),
        "--filter",
    ];
    args.extend(FILTERS);
    let _warnings = windows_bindgen::bindgen(args);

    // `windows-bindgen` invokes rustfmt once, but the deeply nested generated
    // module tree needs a second pass with current rustfmt releases before it
    // reaches a fixed point. Keep formatting until it is stable so `--check`
    // and the workspace `cargo fmt --check` agree on the committed artifact.
    let mut formatting_is_stable = false;
    for _ in 0..4 {
        let before = fs::read(path).expect("read Windows bindings before rustfmt");
        let status = Command::new(&rustfmt)
            .args(["--edition", "2024"])
            .arg(path)
            .status()
            .expect("format generated Windows bindings");
        assert!(status.success(), "rustfmt generated Windows bindings");
        let after = fs::read(path).expect("read Windows bindings after rustfmt");
        if before == after {
            formatting_is_stable = true;
            break;
        }
    }
    assert!(
        formatting_is_stable,
        "rustfmt did not reach a stable Windows bindings artifact"
    );
    let generated = fs::read_to_string(path).expect("read generated Windows bindings");
    for required in [
        "CreateFreeThreaded",
        "CreateForMonitor",
        "D3D11CreateDevice",
        "OpenClipboard",
        "PowerRegisterSuspendResumeNotification",
        "GetVersionExW",
        "GetLastError",
        "FileSaveDialog",
    ] {
        assert!(
            generated.contains(required),
            "generated bindings omit required API {required}"
        );
    }
}

fn main() {
    let output = output_path();
    if env::args().skip(1).any(|argument| argument == "--check") {
        let temporary = env::temp_dir().join(format!(
            "snaploom-windows-bindings-{}-{}.rs",
            env!("CARGO_PKG_VERSION"),
            std::process::id()
        ));
        generate(&temporary);
        let generated = fs::read_to_string(&temporary)
            .expect("read generated Windows bindings")
            .replace("\r\n", "\n");
        let committed = fs::read_to_string(&output)
            .expect("read committed Windows bindings")
            .replace("\r\n", "\n");
        let _ = fs::remove_file(&temporary);
        assert_eq!(generated, committed, "Windows bindings are stale");
    } else {
        generate(&output);
    }
}
