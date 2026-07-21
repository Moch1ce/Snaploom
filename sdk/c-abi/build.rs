fn main() {
    println!("cargo:rerun-if-changed=../c/include/snaploom/snaploom_capture.h");
    println!("cargo:rerun-if-changed=tests/c_harness.c");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("macos") {
        println!("cargo:rustc-link-arg-cdylib=-Wl,-install_name,@rpath/libsnaploom_capture.dylib");
    }
    let mut build = cc::Build::new();
    build
        .file("tests/c_harness.c")
        .include("../c/include")
        .define("SNAPLOOM_CAPTURE_BUILD", None)
        .std("c17")
        .warnings(true)
        .warnings_into_errors(true);
    if build.get_compiler().is_like_msvc() {
        build.flag("/experimental:c11atomics");
    }
    build.compile("snaploom_capture_c_harness");
}
