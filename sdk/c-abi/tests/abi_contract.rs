use std::ffi::CStr;
use std::mem::{align_of, size_of};
use std::ptr;
use std::sync::Arc;

use snaploom_capture::{
    SnaploomCaptureClientConfigV1, SnaploomCaptureCompletionV1, SnaploomCaptureOptionsV1,
    SnaploomCaptureVersionInfoV1, SnaploomUtf8ViewV1, install_test_driver,
    snaploom_capture_client_create_v1, snaploom_capture_client_destroy_v1,
    snaploom_capture_error_name_v1, snaploom_capture_start_v1, snaploom_capture_version_v1,
};
use snaploom_capture_fake_host::FakeHost;

unsafe extern "C" {
    fn snaploom_c_harness_run() -> i32;
}

unsafe extern "C" fn unexpected_callback(
    _client: *mut snaploom_capture::SnaploomCaptureClientV1,
    _completion: *mut SnaploomCaptureCompletionV1,
    _user_data: *mut std::ffi::c_void,
) {
    panic!("invalid start must not invoke its callback");
}

#[test]
fn c_struct_layout_is_frozen_on_64_bit_targets() {
    assert_eq!(
        (
            size_of::<SnaploomUtf8ViewV1>(),
            align_of::<SnaploomUtf8ViewV1>()
        ),
        (16, 8)
    );
    assert_eq!(size_of::<SnaploomCaptureVersionInfoV1>(), 56);
    assert_eq!(size_of::<SnaploomCaptureClientConfigV1>(), 64);
    assert_eq!(size_of::<SnaploomCaptureOptionsV1>(), 48);
    assert_eq!(size_of::<SnaploomCaptureCompletionV1>(), 80);
}

#[test]
fn public_c_header_runs_from_create_through_callback_and_free() {
    install_test_driver(Arc::new(FakeHost::successful()));
    // SAFETY: the harness owns every handle it creates and follows the public C header contract.
    assert_eq!(unsafe { snaploom_c_harness_run() }, 0);
}

#[test]
fn abi_rejects_short_structs_unknown_flags_and_relative_host_paths() {
    let mut version = SnaploomCaptureVersionInfoV1 {
        struct_size: 4,
        abi_major: 99,
        sdk_semver: SnaploomUtf8ViewV1 {
            data: ptr::null(),
            length: 0,
        },
        reserved: [0; 4],
    };
    // SAFETY: all pointers refer to initialized writable test storage.
    assert_eq!(unsafe { snaploom_capture_version_v1(&mut version) }, 2);

    let relative = b"relative/host";
    let config = SnaploomCaptureClientConfigV1 {
        struct_size: size_of::<SnaploomCaptureClientConfigV1>() as u32,
        flags: 0,
        host_executable_override: SnaploomUtf8ViewV1 {
            data: relative.as_ptr(),
            length: relative.len() as u64,
        },
        launch_timeout_ms: 0,
        handshake_timeout_ms: 0,
        reserved: [0; 4],
    };
    let mut client = ptr::null_mut();
    // SAFETY: config and output pointers remain valid throughout the call.
    assert_eq!(
        unsafe { snaploom_capture_client_create_v1(&config, &mut client) },
        1
    );
    assert!(client.is_null());

    install_test_driver(Arc::new(FakeHost::successful()));
    // SAFETY: the output pointer remains valid throughout the call.
    assert_eq!(
        unsafe { snaploom_capture_client_create_v1(ptr::null(), &mut client) },
        0
    );
    let options = SnaploomCaptureOptionsV1 {
        struct_size: size_of::<SnaploomCaptureOptionsV1>() as u32,
        flags: 2,
        interaction_timeout_ms: 0,
        reserved: [0; 4],
    };
    let mut request_id = 999;
    // SAFETY: the live client and initialized options remain valid throughout the call.
    assert_eq!(
        unsafe {
            snaploom_capture_start_v1(
                client,
                &options,
                Some(unexpected_callback),
                ptr::null_mut(),
                &mut request_id,
            )
        },
        1
    );
    assert_eq!(request_id, 0);
    // SAFETY: the client is live and destroyed exactly once.
    assert_eq!(unsafe { snaploom_capture_client_destroy_v1(client) }, 0);
}

#[test]
fn error_names_are_static_and_unknown_codes_are_explicit() {
    // SAFETY: the ABI returns static NUL-terminated strings for every numeric input.
    let known = unsafe { CStr::from_ptr(snaploom_capture_error_name_v1(8)) };
    // SAFETY: unknown values also return a static NUL-terminated fallback.
    let unknown = unsafe { CStr::from_ptr(snaploom_capture_error_name_v1(999)) };
    assert_eq!(known.to_bytes(), b"HOST_CRASHED");
    assert_eq!(unknown.to_bytes(), b"UNKNOWN");
}
