use snaploom_platform_contract::{
    CaptureSnapshot, CaptureSnapshotDescriptor, LogicalRect, LogicalSize, PhysicalPoint,
    PhysicalSize, PlatformAdapter, PlatformEvent, PremultipliedBgraFrame, SaveDisposition,
};
use snaploom_platform_fake::FakePlatform;

fn snapshot(session_id: &str) -> CaptureSnapshot {
    let descriptor = CaptureSnapshotDescriptor {
        session_id: session_id.into(),
        physical_size: PhysicalSize {
            width: 2,
            height: 1,
        },
        logical_size: LogicalSize {
            width: 1.0,
            height: 1.0,
        },
        global_origin: PhysicalPoint { x: -2, y: 0 },
        pointer_physical: PhysicalPoint { x: 0, y: 0 },
        work_area_logical: LogicalRect {
            x: 0.0,
            y: 0.0,
            width: 1.0,
            height: 1.0,
        },
        stride: 8,
        windows: vec![],
    };
    let frame = PremultipliedBgraFrame::new(&descriptor, vec![1; 8]).unwrap();
    CaptureSnapshot::new(descriptor, frame)
}

#[test]
fn one_deep_adapter_contract_covers_overlay_output_shell_and_resume() {
    use std::sync::Arc;
    use std::sync::atomic::{AtomicBool, Ordering};

    let fake = FakePlatform::scripted([snapshot("session")]);
    let capture = fake.capture_snapshot().unwrap();
    fake.show_overlay(capture.descriptor()).unwrap();
    fake.write_png("session", b"png").unwrap();
    let destination = fake
        .choose_png_destination("session", "Snaploom.png")
        .unwrap()
        .unwrap();
    assert_eq!(
        fake.commit_png_save("session", destination, b"png")
            .unwrap(),
        SaveDisposition::Saved
    );
    fake.replace_shortcut("Alt+Shift+A").unwrap();
    fake.set_autostart(true).unwrap();
    assert!(fake.autostart_enabled().unwrap());

    let resumed = Arc::new(AtomicBool::new(false));
    let observed = resumed.clone();
    let _lease = fake
        .watch_resume(Arc::new(move |event| {
            if event == PlatformEvent::Resumed {
                observed.store(true, Ordering::SeqCst);
            }
        }))
        .unwrap();
    fake.trigger_resume().unwrap();
    assert!(resumed.load(Ordering::SeqCst));
    assert_eq!(fake.clipboard_png().unwrap(), Some(b"png".to_vec()));
    assert_eq!(fake.saved_png().unwrap(), Some(b"png".to_vec()));
    fake.finish_session("session").unwrap();
    assert!(fake.write_png("session", b"stale").is_err());
}

#[test]
fn returns_scripted_snapshots_once_in_order() {
    let fake = FakePlatform::scripted([snapshot("one"), snapshot("two")]);
    assert_eq!(
        fake.capture_snapshot().unwrap().descriptor().session_id,
        "one"
    );
    assert_eq!(
        fake.capture_snapshot().unwrap().descriptor().session_id,
        "two"
    );
    assert_eq!(
        fake.capture_snapshot().unwrap_err().to_string(),
        "no scripted capture snapshot"
    );
}
