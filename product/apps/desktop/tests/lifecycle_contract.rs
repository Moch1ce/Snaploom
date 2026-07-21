use serde_json::Value;

#[test]
fn desktop_is_tray_only_with_a_hidden_fixed_light_settings_window() {
    let config: Value = serde_json::from_str(include_str!("../tauri.conf.json")).unwrap();
    let window = &config["app"]["windows"][0];
    assert_eq!(window["visible"], false);
    assert_eq!(window["skipTaskbar"], true);
    assert_eq!(window["theme"], "Light");
    assert_eq!(config["identifier"], "com.snaploom.app");
}

#[test]
fn single_instance_is_initialized_before_other_desktop_plugins() {
    let source = include_str!("../src/lib.rs");
    let single = source
        .find(".plugin(tauri_plugin_single_instance::init")
        .expect("single-instance plugin");
    let shortcut = source
        .find(".plugin(\n            tauri_plugin_global_shortcut")
        .expect("shortcut plugin");
    assert!(single < shortcut);
}

#[test]
fn desktop_uses_capture_sdk_ipc_without_a_capture_session_bypass() {
    let manifest = include_str!("../Cargo.toml");
    let source = include_str!("../src/lib.rs");
    assert!(manifest.contains("snaploom-capture-client"));
    assert!(source.contains("IpcDriver"));
    assert!(!manifest.contains("snaploom-capture-session"));
    assert!(!source.contains("CaptureSession"));
}

#[test]
fn update_network_access_exists_only_behind_the_manual_command() {
    let source = include_str!("../src/lib.rs");
    let command = source.find("async fn check_for_updates").unwrap();
    let network = source.find("reqwest::Client::builder").unwrap();
    let next_command = source[command + 1..]
        .find("#[tauri::command]")
        .map(|offset| command + 1 + offset)
        .unwrap();
    assert!(network > command && network < next_command);
}

#[test]
fn recoverable_capture_failures_open_settings_without_disabling_capture() {
    let source = include_str!("../src/lib.rs");
    assert!(source.contains("show_settings(&callback_app)"));
    let unavailable = source
        .split("let unavailable = matches!(")
        .nth(1)
        .expect("stable unavailable classification")
        .split(");")
        .next()
        .unwrap();
    assert!(!unavailable.contains("StableError::CaptureUnavailable"));
}

#[test]
fn desktop_permission_commands_use_capture_host_ipc() {
    let source = include_str!("../src/lib.rs");
    assert!(source.contains("fn capture_permission("));
    assert!(source.contains("fn request_capture_permission("));
    assert!(source.contains("fn open_capture_permission_settings("));
    assert!(source.contains(".capture_driver"));
    assert!(source.contains(".capture_permission()"));
}
