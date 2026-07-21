use std::fs;
use std::path::PathBuf;

fn product_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..")
}

fn read(relative: &str) -> String {
    fs::read_to_string(product_root().join(relative)).unwrap()
}

#[test]
fn macos_adapter_uses_only_public_rust_framework_bindings() {
    let cargo = read("platform/macos/Cargo.toml");
    for dependency in [
        "objc2-app-kit",
        "objc2-core-graphics",
        "objc2-core-media",
        "objc2-core-video",
        "objc2-screen-capture-kit",
        "objc2-service-management",
    ] {
        assert!(cargo.contains(dependency), "missing {dependency}");
    }
    assert!(!cargo.contains("tauri-plugin-autostart"));

    let sys = read("platform/macos/src/sys.rs");
    for required in [
        "captureSampleBufferWithFilter_configuration_completionHandler",
        "CVPixelBufferLockBaseAddress",
        "CGPreflightScreenCaptureAccess",
        "CGRequestScreenCaptureAccess",
        "CGWindowListCopyWindowInfo",
        "CGDisplayBounds",
        "SCStreamErrorCode::UserDeclined",
        "CVPixelBufferIsPlanar",
        "setColorSpaceName(kCGColorSpaceSRGB)",
        "NSScreen::screens",
        "visibleFrame",
        "NSPasteboard::generalPasteboard",
        "SMAppService::mainAppService",
        "NSWorkspaceDidWakeNotification",
    ] {
        assert!(sys.contains(required), "missing {required}");
    }
    for forbidden in [
        "CGWindowListCreateImage",
        "CGDisplayCreateImage",
        "CGDisplayStream",
        "AXUIElement",
    ] {
        assert!(!sys.contains(forbidden), "forbidden API {forbidden}");
    }

    let shell = read("platform/macos/src/shell.rs");
    assert!(shell.contains("descriptor.display_id"));
    assert!(!shell.contains("cursor_position()"));
    assert!(!shell.contains("monitor_from_point"));
}

#[test]
fn macos_bundles_are_opaque_sonoma_accessory_apps() {
    for app in ["apps/desktop", "apps/capture-host"] {
        let config = read(&format!("{app}/tauri.conf.json"));
        assert!(config.contains("\"macOSPrivateApi\": false"));
        assert!(config.contains("\"minimumSystemVersion\": \"14.0\""));
        assert!(!config.contains("\"transparent\": true"));

        let plist = read(&format!("{app}/Info.plist"));
        assert!(plist.contains("<key>LSUIElement</key>"));
        assert!(plist.contains("<key>NSScreenCaptureUsageDescription</key>"));
        assert!(plist.contains("<string>14.0</string>"));

        let english = read(&format!("{app}/en.lproj/InfoPlist.strings"));
        let chinese = read(&format!("{app}/zh-Hans.lproj/InfoPlist.strings"));
        assert!(english.contains("NSScreenCaptureUsageDescription"));
        assert!(chinese.contains("NSScreenCaptureUsageDescription"));
    }
}

#[test]
fn product_sources_do_not_restore_a_swift_dynamic_bridge() {
    let root = product_root();
    let mut pending = vec![root.clone()];
    while let Some(directory) = pending.pop() {
        for entry in fs::read_dir(directory).unwrap() {
            let path = entry.unwrap().path();
            if path.starts_with(root.join("target")) {
                continue;
            }
            if path.is_dir() {
                pending.push(path);
                continue;
            }
            let name = path
                .file_name()
                .and_then(|name| name.to_str())
                .unwrap_or_default();
            assert!(
                !name.ends_with(".swift"),
                "Swift source restored: {}",
                path.display()
            );
            assert!(
                !name.ends_with(".dylib"),
                "project dylib restored: {}",
                path.display()
            );
        }
    }
}
