use base64::{Engine as _, engine::general_purpose::STANDARD};
use serde::Serialize;
use sha2::{Digest, Sha256};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CaptureReceipt {
    width_px: u32,
    height_px: u32,
    png_bytes: usize,
    sha256: String,
}

/// PROTOTYPE seam: proves a composed PNG can cross WebView -> Rust without
/// granting a guest-side filesystem or clipboard capability.
#[tauri::command]
fn complete_capture(
    png_base64: &str,
    width_px: u32,
    height_px: u32,
) -> Result<CaptureReceipt, String> {
    let payload = png_base64
        .strip_prefix("data:image/png;base64,")
        .ok_or_else(|| "not-png-data-url".to_owned())?;
    let png = STANDARD
        .decode(payload)
        .map_err(|_| "invalid-base64".to_owned())?;
    if png.len() < 8 || &png[..8] != b"\x89PNG\r\n\x1a\n" {
        return Err("invalid-png-signature".to_owned());
    }

    Ok(CaptureReceipt {
        width_px,
        height_px,
        png_bytes: png.len(),
        sha256: format!("{:x}", Sha256::digest(&png)),
    })
}

#[tauri::command]
fn cancel_capture() -> &'static str {
    "canceled"
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![complete_capture, cancel_capture])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
