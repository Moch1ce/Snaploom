use snaploom_platform_contract::{
    CaptureSnapshotDescriptor, LogicalRect, LogicalSize, MAX_CAPTURE_FRAME_BYTES, PhysicalPoint,
    PhysicalRect, PhysicalSize, PremultipliedBgraFrame, WindowCandidate,
};

fn descriptor() -> CaptureSnapshotDescriptor {
    CaptureSnapshotDescriptor {
        session_id: "session-39".into(),
        physical_size: PhysicalSize {
            width: 4,
            height: 2,
        },
        logical_size: LogicalSize {
            width: 2.0,
            height: 1.0,
        },
        global_origin: PhysicalPoint { x: -1920, y: -200 },
        pointer_physical: PhysicalPoint { x: 1, y: 1 },
        work_area_logical: LogicalRect {
            x: 0.0,
            y: 24.0,
            width: 2.0,
            height: 1.0,
        },
        stride: 16,
        windows: vec![WindowCandidate {
            stable_id: "front".into(),
            z_order: 1,
            bounds: PhysicalRect {
                x: 0,
                y: 0,
                width: 2,
                height: 1,
            },
        }],
    }
}

#[test]
fn accepts_only_exact_bounded_premultiplied_bgra_frames() {
    let descriptor = descriptor();
    let frame = PremultipliedBgraFrame::new(&descriptor, vec![0; 32]).unwrap();
    assert_eq!(frame.bytes(), &[0; 32]);
    assert_eq!(frame.pixel_format(), "BGRA8_PREMULTIPLIED");

    assert_eq!(
        PremultipliedBgraFrame::new(&descriptor, vec![0; 31])
            .unwrap_err()
            .to_string(),
        "capture frame length mismatch"
    );
    assert_eq!(
        PremultipliedBgraFrame::validate_length(MAX_CAPTURE_FRAME_BYTES + 1, 1)
            .unwrap_err()
            .to_string(),
        "capture frame exceeds limit"
    );
}

#[test]
fn reports_independent_physical_to_logical_scales() {
    assert_eq!(descriptor().scale(), (2.0, 2.0));
}
