fn main() {
    println!("cargo:rerun-if-changed=../include/snaploom_capture.h");
    println!("cargo:rerun-if-changed=tests/c_harness.c");
    let mut build = cc::Build::new();
    build
        .file("tests/c_harness.c")
        .include("../include")
        .define("SNAPLOOM_CAPTURE_BUILD", None)
        .std("c11")
        .warnings(true);
    if build.get_compiler().is_like_msvc() {
        build.flag("/experimental:c11atomics");
    }
    build.compile("snaploom_capture_c_harness");
}
