#[test]
fn windows_manifest_declares_pmv2_without_elevation_or_capture_capabilities() {
    let manifest = include_str!("../windows-app-manifest.xml");

    assert!(manifest.contains(">PerMonitorV2</dpiAwareness>"));
    assert!(manifest.contains("level=\"asInvoker\""));
    assert!(!manifest.contains("requireAdministrator"));
    assert!(!manifest.contains("graphicsCaptureWithoutBorder"));
}
