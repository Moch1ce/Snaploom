use snaploom_platform_contract::{
    CaptureSnapshot, CaptureSnapshotDescriptor, LogicalRect, LogicalSize, PhysicalPoint,
    PhysicalSize, PlatformAdapter, PremultipliedBgraFrame,
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
