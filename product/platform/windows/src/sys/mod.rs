#![cfg(windows)]

#[allow(clippy::all)]
#[allow(dead_code)]
#[allow(unpredictable_function_pointer_comparisons)]
mod bindings;
mod capture;
mod clipboard;
mod dialog;
mod overlay;
mod power;
mod version;

pub(crate) use bindings::Windows;
pub(crate) use capture::{capture_current_display, is_supported};
pub(crate) use clipboard::write_png;
pub(crate) use dialog::choose_png_destination;
pub(crate) use overlay::{atomic_replace, prepare_overlay};
pub(crate) use power::{ResumeLease, watch_resume};
pub(crate) use version::current_process_id;
