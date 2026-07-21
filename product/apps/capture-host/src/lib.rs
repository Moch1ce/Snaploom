pub fn run() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("Snaploom Capture Host runtime failed");
}
