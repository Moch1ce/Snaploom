use std::path::PathBuf;
use std::sync::{Arc, Mutex};

use serde::Serialize;
use snaploom_capture_client::ipc::{IpcClientConfig, IpcDriver};
use snaploom_capture_client::{
    CaptureClient, CaptureLanguage, CaptureOptions, CapturePermission, StableError, Terminal,
};
use snaploom_capture_protocol::CaptureOrigin;
use snaploom_desktop_shell::{
    AppSettings, CaptureIntentGate, CaptureTrigger, HttpReleaseResponse, Language, PrivacyEvent,
    PrivacyLevel, PrivacyLog, PrivacyRecord, SettingsLoadStatus, SettingsStore, UpdateState,
    evaluate_release_response, utc_now,
};
use snaploom_platform_contract::{PlatformAdapter, PlatformEvent, PlatformLanguage};
use tauri::menu::{CheckMenuItem, Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Emitter, Manager, Runtime, State, Wry};
use tauri_plugin_global_shortcut::ShortcutState;
use tauri_plugin_opener::OpenerExt;

#[cfg(target_os = "macos")]
type NativeShell = snaploom_platform_macos::MacShell<Wry>;
#[cfg(windows)]
type NativeShell = snaploom_platform_windows::WindowsShell<Wry>;

const RELEASES_API: &str = "https://api.github.com/repos/Moch1ce/Snaploom/releases";
const SETTINGS_LABEL: &str = "main";

struct DesktopState {
    settings_store: SettingsStore,
    settings: Mutex<AppSettings>,
    privacy_log: PrivacyLog,
    intent_gate: Arc<CaptureIntentGate>,
    capture_client: Arc<CaptureClient>,
    capture_driver: Arc<IpcDriver>,
    platform: NativeShell,
    resume_lease: Mutex<Option<<NativeShell as PlatformAdapter>::ResumeLease>>,
    verified_release_url: Mutex<Option<String>>,
    start_menu: Mutex<Option<MenuItem<Wry>>>,
    settings_menu: Mutex<Option<MenuItem<Wry>>>,
    autostart_menu: Mutex<Option<CheckMenuItem<Wry>>>,
    quit_menu: Mutex<Option<MenuItem<Wry>>>,
}

impl DesktopState {
    fn log(&self, level: PrivacyLevel, event: PrivacyEvent, exception_type: Option<&str>) {
        let _ = self.privacy_log.append(&PrivacyRecord {
            utc: utc_now(),
            level,
            event,
            exception_type: exception_type.map(str::to_owned),
        });
    }

    fn persist_current_settings(&self) -> bool {
        let settings = self
            .settings
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone();
        match self.settings_store.save(&settings) {
            Ok(()) => true,
            Err(_) => {
                self.log(
                    PrivacyLevel::Error,
                    PrivacyEvent::SettingsWriteFailed,
                    Some("snaploom_desktop_shell::SettingsWriteError"),
                );
                false
            }
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SettingsSnapshot {
    settings: AppSettings,
    platform: &'static str,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SettingsMutation {
    settings: AppSettings,
    persisted: bool,
}

#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
enum CapturePermissionView {
    Granted,
    NotGranted,
    RestartRequired,
    NotApplicable,
}

#[tauri::command]
fn settings_snapshot(state: State<'_, DesktopState>) -> SettingsSnapshot {
    SettingsSnapshot {
        settings: state
            .settings
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone(),
        platform: state.platform.platform_name(),
    }
}

#[tauri::command]
fn capture_permission(state: State<'_, DesktopState>) -> Result<CapturePermissionView, String> {
    state
        .capture_driver
        .capture_permission()
        .map(permission_view)
        .map_err(|error| stable_error_name(error).to_owned())
}

#[tauri::command]
fn request_capture_permission(
    state: State<'_, DesktopState>,
) -> Result<CapturePermissionView, String> {
    state
        .capture_driver
        .request_capture_permission()
        .map(permission_view)
        .map_err(|error| stable_error_name(error).to_owned())
}

#[tauri::command]
fn open_capture_permission_settings(state: State<'_, DesktopState>) -> Result<(), String> {
    state
        .capture_driver
        .open_capture_permission_settings()
        .map_err(|error| stable_error_name(error).to_owned())
}

const fn permission_view(permission: CapturePermission) -> CapturePermissionView {
    match permission {
        CapturePermission::Granted => CapturePermissionView::Granted,
        CapturePermission::NotGranted => CapturePermissionView::NotGranted,
        CapturePermission::RestartRequired => CapturePermissionView::RestartRequired,
        CapturePermission::NotApplicable => CapturePermissionView::NotApplicable,
    }
}

#[tauri::command]
fn replace_shortcut(
    shortcut: String,
    state: State<'_, DesktopState>,
) -> Result<SettingsMutation, String> {
    state
        .platform
        .replace_shortcut(&shortcut)
        .map_err(|error| error.to_string())?;
    let settings = {
        let mut settings = state
            .settings
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        settings.shortcut = shortcut;
        settings.clone()
    };
    state.log(PrivacyLevel::Info, PrivacyEvent::ShortcutChanged, None);
    Ok(SettingsMutation {
        settings,
        persisted: state.persist_current_settings(),
    })
}

#[tauri::command]
fn set_autostart(
    enabled: bool,
    state: State<'_, DesktopState>,
) -> Result<SettingsMutation, String> {
    set_autostart_inner(enabled, &state)
}

fn set_autostart_inner(enabled: bool, state: &DesktopState) -> Result<SettingsMutation, String> {
    state.platform.set_autostart(enabled).map_err(|error| {
        state.log(
            PrivacyLevel::Error,
            PrivacyEvent::AutostartFailed,
            Some("snaploom_platform_contract::PlatformError"),
        );
        error.to_string()
    })?;
    let readback = state.platform.autostart_enabled().map_err(|error| {
        state.log(
            PrivacyLevel::Error,
            PrivacyEvent::AutostartFailed,
            Some("snaploom_platform_contract::PlatformError"),
        );
        error.to_string()
    })?;
    if readback != enabled {
        state.log(
            PrivacyLevel::Error,
            PrivacyEvent::AutostartFailed,
            Some("snaploom_desktop::AutostartReadbackMismatch"),
        );
        return Err("autostart readback mismatch".into());
    }
    let settings = {
        let mut settings = state
            .settings
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        settings.autostart = enabled;
        settings.clone()
    };
    if let Some(item) = state
        .autostart_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .as_ref()
    {
        let _ = item.set_checked(enabled);
    }
    state.log(PrivacyLevel::Info, PrivacyEvent::AutostartChanged, None);
    Ok(SettingsMutation {
        settings,
        persisted: state.persist_current_settings(),
    })
}

#[tauri::command]
fn set_language(language: Language, state: State<'_, DesktopState>) -> SettingsMutation {
    state
        .capture_driver
        .set_capture_language(capture_language(language));
    let settings = {
        let mut settings = state
            .settings
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        settings.language = language;
        settings.clone()
    };
    let _ = state.platform.set_language(platform_language(language));
    let labels = TrayLabels::for_language(language);
    if let Some(item) = state
        .start_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .as_ref()
    {
        let _ = item.set_text(labels.capture);
    }
    if let Some(item) = state
        .settings_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .as_ref()
    {
        let _ = item.set_text(labels.settings);
    }
    if let Some(item) = state
        .autostart_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .as_ref()
    {
        let _ = item.set_text(labels.autostart);
    }
    if let Some(item) = state
        .quit_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .as_ref()
    {
        let _ = item.set_text(labels.quit);
    }
    SettingsMutation {
        settings,
        persisted: state.persist_current_settings(),
    }
}

#[tauri::command]
fn open_privacy_logs(app: AppHandle, state: State<'_, DesktopState>) -> Result<(), String> {
    std::fs::create_dir_all(state.privacy_log.directory())
        .map_err(|_| "log directory unavailable")?;
    app.opener()
        .open_path(
            state.privacy_log.directory().to_string_lossy(),
            None::<&str>,
        )
        .map_err(|_| "could not open log directory".into())
}

#[tauri::command]
fn clear_privacy_logs(state: State<'_, DesktopState>) -> Result<(), String> {
    state
        .privacy_log
        .clear()
        .map_err(|_| "could not clear privacy logs".to_owned())?;
    Ok(())
}

#[tauri::command]
async fn check_for_updates(state: State<'_, DesktopState>) -> Result<UpdateState, String> {
    state.log(PrivacyLevel::Info, PrivacyEvent::UpdateCheckStarted, None);
    let response = reqwest::Client::builder()
        .https_only(true)
        .build()
        .map_err(|_| "update client unavailable")?
        .get(RELEASES_API)
        .header("Accept", "application/vnd.github+json")
        .header("User-Agent", "Snaploom-Desktop")
        .send()
        .await;
    let evaluated = match response {
        Ok(response) => {
            let status = response.status().as_u16();
            match response.text().await {
                Ok(body) => evaluate_release_response(
                    env!("CARGO_PKG_VERSION"),
                    Ok(HttpReleaseResponse {
                        request_url: RELEASES_API,
                        status,
                        body: &body,
                    }),
                ),
                Err(_) => UpdateState::NetworkError,
            }
        }
        Err(_) => UpdateState::NetworkError,
    };
    let release_url = match &evaluated {
        UpdateState::Available { release_url, .. } => Some(release_url.clone()),
        _ => None,
    };
    *state
        .verified_release_url
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = release_url;
    state.log(
        if matches!(
            evaluated,
            UpdateState::Current | UpdateState::Available { .. }
        ) {
            PrivacyLevel::Info
        } else {
            PrivacyLevel::Warning
        },
        if matches!(
            evaluated,
            UpdateState::Current | UpdateState::Available { .. }
        ) {
            PrivacyEvent::UpdateCheckCompleted
        } else {
            PrivacyEvent::UpdateCheckFailed
        },
        None,
    );
    Ok(evaluated)
}

#[tauri::command]
fn open_verified_release(app: AppHandle, state: State<'_, DesktopState>) -> Result<(), String> {
    let release_url = state
        .verified_release_url
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .clone()
        .ok_or_else(|| "no verified release is available".to_owned())?;
    app.opener()
        .open_url(release_url, None::<&str>)
        .map_err(|_| "could not open release page".into())
}

#[tauri::command]
fn start_capture(app: AppHandle) {
    request_capture(&app, CaptureTrigger::Tray);
}

fn request_capture<R: Runtime>(app: &AppHandle<R>, trigger: CaptureTrigger) {
    let Some(state) = app.try_state::<DesktopState>() else {
        return;
    };
    let Some(lease) = state.intent_gate.try_begin_owned() else {
        return;
    };
    state.log(PrivacyLevel::Info, PrivacyEvent::CaptureRequested, None);
    let callback_app = app.clone();
    if state
        .capture_client
        .start(CaptureOptions::default(), move |terminal| {
            let Some(state) = callback_app.try_state::<DesktopState>() else {
                drop(lease);
                return;
            };
            let (event, level, terminal_view, unavailable, show_failure) = match terminal {
                Terminal::Completed { .. } => (
                    PrivacyEvent::CaptureCompleted,
                    PrivacyLevel::Info,
                    "completed",
                    false,
                    false,
                ),
                Terminal::Canceled { .. } => (
                    PrivacyEvent::CaptureCanceled,
                    PrivacyLevel::Info,
                    "canceled",
                    false,
                    false,
                ),
                Terminal::Failed {
                    error: StableError::Busy,
                    ..
                } => {
                    drop(lease);
                    return;
                }
                Terminal::Failed { error, .. } => {
                    let unavailable = matches!(
                        error,
                        StableError::HostNotFound
                            | StableError::HostStartFailed
                            | StableError::HostStartTimeout
                            | StableError::PlatformUnavailable
                    );
                    (
                        PrivacyEvent::CaptureFailed,
                        PrivacyLevel::Error,
                        stable_error_name(error),
                        unavailable,
                        true,
                    )
                }
            };
            state.log(
                level,
                event,
                (level == PrivacyLevel::Error).then_some("snaploom_capture_client::StableError"),
            );
            if unavailable
                && let Some(item) = state
                    .start_menu
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .as_ref()
            {
                let _ = item.set_enabled(false);
            }
            if show_failure {
                show_settings(&callback_app);
            }
            let _ = callback_app.emit(
                "capture-terminal",
                CaptureTerminalEvent {
                    trigger,
                    terminal: terminal_view,
                },
            );
            drop(lease);
        })
        .is_err()
    {
        state.log(
            PrivacyLevel::Error,
            PrivacyEvent::CaptureFailed,
            Some("snaploom_capture_client::ClientStatus"),
        );
        show_settings(app);
        let _ = app.emit(
            "capture-terminal",
            CaptureTerminalEvent {
                trigger,
                terminal: "internal",
            },
        );
    }
}

#[derive(Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
struct CaptureTerminalEvent {
    trigger: CaptureTrigger,
    terminal: &'static str,
}

const fn stable_error_name(error: StableError) -> &'static str {
    match error {
        StableError::None => "none",
        StableError::Busy => "busy",
        StableError::HostNotFound => "hostNotFound",
        StableError::PlatformUnavailable => "platformUnavailable",
        StableError::PermissionNotGranted => "permissionNotGranted",
        StableError::PermissionRevoked => "permissionRevoked",
        StableError::DisplayUnavailable => "displayUnavailable",
        StableError::CaptureUnavailable => "captureUnavailable",
        StableError::CaptureTimeout => "captureTimeout",
        StableError::PixelConversionFailed => "pixelConversionFailed",
        StableError::RequestTimeout => "requestTimeout",
        StableError::HostCrashed => "hostCrashed",
        StableError::HostStartFailed => "hostStartFailed",
        StableError::HostStartTimeout => "hostStartTimeout",
        StableError::HandshakeTimeout => "handshakeTimeout",
        StableError::ProtocolIncompatible => "protocolIncompatible",
        StableError::ProtocolError => "protocolError",
        StableError::TransportFailed => "transportFailed",
        StableError::AuthenticationFailed => "authenticationFailed",
        StableError::InvalidResult => "invalidResult",
        StableError::ResultTooLarge => "resultTooLarge",
        StableError::ClipboardWriteFailed => "clipboardWriteFailed",
        StableError::ClientClosed => "clientClosed",
        StableError::OutOfMemory => "outOfMemory",
        StableError::Internal => "internal",
    }
}

fn show_settings<R: Runtime>(app: &AppHandle<R>) {
    if let Some(window) = app.get_webview_window(SETTINGS_LABEL) {
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn configure_tray(app: &tauri::App<Wry>) -> tauri::Result<()> {
    let state = app.state::<DesktopState>();
    let language = state
        .settings
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .language;
    let labels = TrayLabels::for_language(language);
    let capture = MenuItem::with_id(app, "capture", labels.capture, true, None::<&str>)?;
    let settings = MenuItem::with_id(app, "settings", labels.settings, true, None::<&str>)?;
    let autostart_enabled = state
        .settings
        .lock()
        .unwrap_or_else(|error| error.into_inner())
        .autostart;
    let autostart = CheckMenuItem::with_id(
        app,
        "autostart",
        labels.autostart,
        true,
        autostart_enabled,
        None::<&str>,
    )?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", labels.quit, true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&capture, &settings, &autostart, &separator, &quit])?;
    *state
        .start_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = Some(capture);
    *state
        .settings_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = Some(settings);
    *state
        .autostart_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = Some(autostart);
    *state
        .quit_menu
        .lock()
        .unwrap_or_else(|error| error.into_inner()) = Some(quit);
    let mut tray = TrayIconBuilder::new()
        .menu(&menu)
        .tooltip("Snaploom")
        .show_menu_on_left_click(true)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "capture" => request_capture(app, CaptureTrigger::Tray),
            "settings" => show_settings(app),
            "autostart" => {
                if let Some(state) = app.try_state::<DesktopState>() {
                    let current = state
                        .settings
                        .lock()
                        .unwrap_or_else(|error| error.into_inner())
                        .autostart;
                    let _ = set_autostart_inner(!current, &state);
                }
            }
            "quit" => app.exit(0),
            _ => {}
        });
    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }
    tray.build(app)?;
    Ok(())
}

struct TrayLabels {
    capture: &'static str,
    settings: &'static str,
    autostart: &'static str,
    quit: &'static str,
}

impl TrayLabels {
    const fn for_language(language: Language) -> Self {
        match language {
            Language::ZhCn => Self {
                capture: "开始截图",
                settings: "设置",
                autostart: "开机启动",
                quit: "退出",
            },
            Language::En => Self {
                capture: "Start Capture",
                settings: "Settings",
                autostart: "Launch at Login",
                quit: "Quit",
            },
        }
    }
}

const fn platform_language(language: Language) -> PlatformLanguage {
    match language {
        Language::ZhCn => PlatformLanguage::ZhCn,
        Language::En => PlatformLanguage::En,
    }
}

const fn capture_language(language: Language) -> CaptureLanguage {
    match language {
        Language::ZhCn => CaptureLanguage::ZhCn,
        Language::En => CaptureLanguage::En,
    }
}

fn host_executable() -> Option<PathBuf> {
    let executable = std::env::current_exe().ok()?;
    let executable_dir = executable.parent()?;
    let direct = executable_dir.join(if cfg!(windows) {
        "snaploom-capture-host.exe"
    } else {
        "snaploom-capture-host"
    });
    if direct.is_file() {
        return Some(direct);
    }
    #[cfg(target_os = "macos")]
    {
        let bundled = executable_dir
            .parent()?
            .join("Resources/Snaploom Capture Host.app/Contents/MacOS/snaploom-capture-host");
        if bundled.is_file() {
            return Some(bundled);
        }
    }
    None
}

pub fn run() {
    tauri::Builder::default()
        // APP-02: this must remain the first plugin so no second resident
        // instance can initialize tray, shortcut, or update services.
        .plugin(tauri_plugin_single_instance::init(
            |app, _arguments, _cwd| {
                request_capture(app, CaptureTrigger::SecondInstance);
            },
        ))
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    if event.state() == ShortcutState::Pressed {
                        request_capture(app, CaptureTrigger::Shortcut);
                    }
                })
                .build(),
        )
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(autostart_plugin())
        .setup(|app| {
            let config_dir = app.path().app_config_dir()?;
            let log_dir = app.path().app_log_dir()?;
            let store = SettingsStore::new(config_dir.join("settings.json"));
            let mut loaded = store.load();
            if loaded.status != SettingsLoadStatus::Loaded {
                loaded.settings.language =
                    Language::from_system_locale(std::env::var("LANG").ok().as_deref());
            }
            let driver = Arc::new(IpcDriver::new(IpcClientConfig {
                host_executable_override: host_executable(),
                origin: CaptureOrigin::App,
                language: capture_language(loaded.settings.language),
                ..IpcClientConfig::default()
            }));
            let client = CaptureClient::new(driver.clone())
                .map_err(|_| std::io::Error::other("capture client initialization failed"))?;
            app.manage(DesktopState {
                settings_store: store,
                settings: Mutex::new(loaded.settings),
                privacy_log: PrivacyLog::new(log_dir),
                intent_gate: Arc::new(CaptureIntentGate::new()),
                capture_client: Arc::new(client),
                capture_driver: driver,
                platform: NativeShell::new(app.handle().clone()),
                resume_lease: Mutex::new(None),
                verified_release_url: Mutex::new(None),
                start_menu: Mutex::new(None),
                settings_menu: Mutex::new(None),
                autostart_menu: Mutex::new(None),
                quit_menu: Mutex::new(None),
            });
            let state = app.state::<DesktopState>();
            state.log(PrivacyLevel::Info, PrivacyEvent::AppStarted, None);
            if loaded.status != SettingsLoadStatus::Loaded {
                state.log(
                    PrivacyLevel::Warning,
                    PrivacyEvent::SettingsLoadedDefault,
                    None,
                );
            }
            let language = state
                .settings
                .lock()
                .unwrap_or_else(|error| error.into_inner())
                .language;
            state.platform.set_language(platform_language(language))?;
            if let Ok(native_autostart) = state.platform.autostart_enabled() {
                state
                    .settings
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .autostart = native_autostart;
            }
            let shortcut = state
                .settings
                .lock()
                .unwrap_or_else(|error| error.into_inner())
                .shortcut
                .clone();
            if state.platform.replace_shortcut(&shortcut).is_ok() {
                let callback_app = app.handle().clone();
                if let Ok(lease) = state.platform.watch_resume(Arc::new(move |event| {
                    if event == PlatformEvent::ShortcutResumeFailed
                        && let Some(state) = callback_app.try_state::<DesktopState>()
                    {
                        state.log(
                            PrivacyLevel::Error,
                            PrivacyEvent::ShortcutResumeFailed,
                            Some("snaploom_platform_contract::PlatformError"),
                        );
                    }
                })) {
                    *state
                        .resume_lease
                        .lock()
                        .unwrap_or_else(|error| error.into_inner()) = Some(lease);
                }
            }
            configure_tray(app)?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            settings_snapshot,
            capture_permission,
            request_capture_permission,
            open_capture_permission_settings,
            replace_shortcut,
            set_autostart,
            set_language,
            open_privacy_logs,
            clear_privacy_logs,
            check_for_updates,
            open_verified_release,
            start_capture,
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("Snaploom Desktop runtime failed");
}

#[cfg(windows)]
fn autostart_plugin() -> tauri::plugin::TauriPlugin<Wry> {
    tauri_plugin_autostart::init(
        tauri_plugin_autostart::MacosLauncher::LaunchAgent,
        None::<Vec<&str>>,
    )
}

#[cfg(target_os = "macos")]
fn autostart_plugin() -> tauri::plugin::TauriPlugin<Wry> {
    tauri::plugin::Builder::new("snaploom-native-autostart").build()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn stable_capture_errors_never_expose_dynamic_messages() {
        assert_eq!(stable_error_name(StableError::HostNotFound), "hostNotFound");
        assert_eq!(stable_error_name(StableError::Busy), "busy");
    }

    #[test]
    fn release_api_is_fixed_to_the_snaploom_repository() {
        assert_eq!(
            RELEASES_API,
            "https://api.github.com/repos/Moch1ce/Snaploom/releases"
        );
    }
}
