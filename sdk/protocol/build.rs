fn main() {
    println!("cargo:rerun-if-changed=proto/capture.proto");
    let protoc = protoc_bin_vendored::protoc_bin_path().expect("vendored protoc is unavailable");
    // SAFETY: Cargo build scripts run in their own process and this value is set
    // before prost-build starts any worker process.
    unsafe { std::env::set_var("PROTOC", protoc) };
    prost_build::Config::new()
        .compile_protos(&["proto/capture.proto"], &["proto"])
        .expect("capture protocol code generation failed");
}
