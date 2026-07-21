#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    snaploom_capture_host_lib::run();
}
