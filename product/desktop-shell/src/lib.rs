mod lifecycle;
mod localization;
mod privacy_log;
mod settings;
mod update;

pub use lifecycle::{CaptureIntentGate, CaptureTrigger, IntentLease, OwnedIntentLease};
pub use localization::{
    Language, MessageKey, localize, resolve_language, resource_keys, system_language,
};
pub use privacy_log::{
    PrivacyEvent, PrivacyLevel, PrivacyLog, PrivacyLogError, PrivacyRecord, utc_now,
};
pub use settings::{
    AppSettings, SettingsLoad, SettingsLoadStatus, SettingsStore, SettingsWriteError,
};
pub use update::{HttpReleaseResponse, UpdateState, evaluate_release_response};

pub const PRODUCT_NAME: &str = "Snaploom";
