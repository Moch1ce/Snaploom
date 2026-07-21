mod ipc;
#[cfg(all(feature = "tauri-runtime", any(windows, test)))]
mod windows_bridge;

pub use ipc::{HostServer, HostServerError, HostServerOutcome};

#[cfg(feature = "tauri-runtime")]
pub fn run() {
    let bootstrap = ipc::read_bootstrap_challenge().expect("invalid Capture Host bootstrap");
    #[cfg(any(windows, test))]
    let bridge = std::sync::Arc::new(windows_bridge::WindowsBridge::new());
    #[cfg(any(windows, test))]
    let backend: std::sync::Arc<dyn snaploom_capture_session::SessionBackend> =
        std::sync::Arc::new(windows_bridge::WindowsSessionBackend::new(bridge.clone()));
    #[cfg(not(any(windows, test)))]
    let backend: std::sync::Arc<dyn snaploom_capture_session::SessionBackend> =
        std::sync::Arc::new(snaploom_capture_session::UnavailableSessionBackend);
    let server = match ipc::HostServer::start(backend).expect("Capture Host IPC startup failed") {
        ipc::HostServerOutcome::Leader(server) => server,
        ipc::HostServerOutcome::Existing(paths) => {
            ipc::verify_existing_leader(&paths).expect("existing Capture Host failed health check");
            ipc::signal_bootstrap_ready(bootstrap).expect("Capture Host readiness failed");
            return;
        }
    };
    let idle_exit = server.idle_exit_signal();
    let builder = tauri::Builder::default().plugin(tauri_plugin_dialog::init());
    #[cfg(any(windows, test))]
    let builder = builder
        .manage(bridge.clone())
        .invoke_handler(tauri::generate_handler![
            windows_bridge::capture_descriptor,
            windows_bridge::capture_frame,
            windows_bridge::write_png_clipboard,
            windows_bridge::save_png,
            windows_bridge::set_overlay_visible,
            windows_bridge::overlay_ready,
            windows_bridge::close_overlay,
            windows_bridge::cancel_overlay,
        ]);
    builder
        .setup(move |app| {
            #[cfg(any(windows, test))]
            bridge
                .initialize_shell(snaploom_platform_windows::WindowsShell::new(
                    app.handle().clone(),
                ))
                .map_err(Box::<dyn std::error::Error>::from)?;
            ipc::signal_bootstrap_ready(bootstrap)
                .map_err(|_| std::io::Error::other("Capture Host readiness failed"))?;
            let app_handle = app.handle().clone();
            std::thread::Builder::new()
                .name("snaploom-host-idle-exit".into())
                .spawn(move || {
                    while !idle_exit.load(std::sync::atomic::Ordering::Acquire) {
                        std::thread::sleep(std::time::Duration::from_millis(100));
                    }
                    app_handle.exit(0);
                })?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Snaploom Capture Host runtime failed");
    drop(server);
}
