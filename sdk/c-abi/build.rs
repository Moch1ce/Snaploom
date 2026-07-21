fn main() {
    println!("cargo:rerun-if-changed=../include/snaploom_capture.h");
    println!("cargo:rerun-if-changed=tests/c_harness.c");
    cc::Build::new()
        .file("tests/c_harness.c")
        .include("../include")
        .define("SNAPLOOM_CAPTURE_BUILD", None)
        .std("c11")
        .warnings(true)
        .compile("snaploom_capture_c_harness");
}
