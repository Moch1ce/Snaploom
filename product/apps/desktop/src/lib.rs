pub fn run() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("Snaploom Desktop runtime failed");
}
