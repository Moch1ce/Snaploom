mod ipc;

pub use ipc::{HostServer, HostServerError, HostServerOutcome};

#[cfg(feature = "tauri-runtime")]
pub fn run() {
    let bootstrap = ipc::read_bootstrap_challenge().expect("invalid Capture Host bootstrap");
    let server = match ipc::HostServer::start(std::sync::Arc::new(
        snaploom_capture_session::UnavailableSessionBackend,
    ))
    .expect("Capture Host IPC startup failed")
    {
        ipc::HostServerOutcome::Leader(server) => server,
        ipc::HostServerOutcome::Existing(paths) => {
            ipc::verify_existing_leader(&paths).expect("existing Capture Host failed health check");
            ipc::signal_bootstrap_ready(bootstrap).expect("Capture Host readiness failed");
            return;
        }
    };
    ipc::signal_bootstrap_ready(bootstrap).expect("Capture Host readiness failed");
    let idle_exit = server.idle_exit_signal();
    tauri::Builder::default()
        .setup(move |app| {
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
