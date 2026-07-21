use std::collections::BTreeMap;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

use serde::{Deserialize, Serialize};

use crate::Language;

pub const SETTINGS_SCHEMA_VERSION: u32 = 1;
static NEXT_TEMP_FILE: AtomicU64 = AtomicU64::new(1);

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    pub schema_version: u32,
    pub shortcut: String,
    pub autostart: bool,
    #[serde(default)]
    pub recent_annotation_styles: BTreeMap<String, String>,
    pub recent_save_directory: Option<String>,
    pub language: Language,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            schema_version: SETTINGS_SCHEMA_VERSION,
            shortcut: if cfg!(target_os = "macos") {
                "Command+Shift+A".into()
            } else {
                "Alt+Shift+A".into()
            },
            autostart: false,
            recent_annotation_styles: BTreeMap::new(),
            recent_save_directory: None,
            language: Language::En,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SettingsLoadStatus {
    Loaded,
    Missing,
    Corrupt,
    UnsupportedVersion,
    Unreadable,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SettingsLoad {
    pub settings: AppSettings,
    pub status: SettingsLoadStatus,
}

#[derive(Debug)]
pub struct SettingsStore {
    path: PathBuf,
}

impl SettingsStore {
    #[must_use]
    pub fn new(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into() }
    }

    #[must_use]
    pub fn path(&self) -> &Path {
        &self.path
    }

    #[must_use]
    pub fn load(&self) -> SettingsLoad {
        let bytes = match fs::read(&self.path) {
            Ok(bytes) => bytes,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                return SettingsLoad {
                    settings: AppSettings::default(),
                    status: SettingsLoadStatus::Missing,
                };
            }
            Err(_) => {
                return SettingsLoad {
                    settings: AppSettings::default(),
                    status: SettingsLoadStatus::Unreadable,
                };
            }
        };
        let settings: AppSettings = match serde_json::from_slice(&bytes) {
            Ok(settings) => settings,
            Err(_) => {
                return SettingsLoad {
                    settings: AppSettings::default(),
                    status: SettingsLoadStatus::Corrupt,
                };
            }
        };
        if settings.schema_version != SETTINGS_SCHEMA_VERSION {
            return SettingsLoad {
                settings: AppSettings::default(),
                status: SettingsLoadStatus::UnsupportedVersion,
            };
        }
        SettingsLoad {
            settings,
            status: SettingsLoadStatus::Loaded,
        }
    }

    pub fn save(&self, settings: &AppSettings) -> Result<(), SettingsWriteError> {
        if settings.schema_version != SETTINGS_SCHEMA_VERSION {
            return Err(SettingsWriteError::UnsupportedVersion);
        }
        let parent = self.path.parent().ok_or(SettingsWriteError::InvalidPath)?;
        fs::create_dir_all(parent).map_err(SettingsWriteError::Io)?;
        let temporary = parent.join(format!(
            ".snaploom-settings-{}-{}.tmp",
            std::process::id(),
            NEXT_TEMP_FILE.fetch_add(1, Ordering::Relaxed)
        ));
        let result = (|| {
            let mut file = OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&temporary)
                .map_err(SettingsWriteError::Io)?;
            let bytes = serde_json::to_vec_pretty(settings).map_err(SettingsWriteError::Json)?;
            file.write_all(&bytes).map_err(SettingsWriteError::Io)?;
            file.sync_all().map_err(SettingsWriteError::Io)?;
            #[cfg(windows)]
            if self.path.exists() {
                fs::remove_file(&self.path).map_err(SettingsWriteError::Io)?;
            }
            fs::rename(&temporary, &self.path).map_err(SettingsWriteError::Io)
        })();
        if result.is_err() {
            let _ = fs::remove_file(&temporary);
        }
        result
    }
}

#[derive(Debug)]
pub enum SettingsWriteError {
    InvalidPath,
    UnsupportedVersion,
    Io(std::io::Error),
    Json(serde_json::Error),
}

impl std::fmt::Display for SettingsWriteError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(match self {
            Self::InvalidPath => "invalid settings path",
            Self::UnsupportedVersion => "unsupported settings version",
            Self::Io(_) => "settings write failed",
            Self::Json(_) => "settings serialization failed",
        })
    }
}

impl std::error::Error for SettingsWriteError {}

#[cfg(test)]
mod tests {
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::*;

    fn test_dir(label: &str) -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("snaploom-{label}-{}-{nonce}", std::process::id()))
    }

    #[test]
    fn round_trips_the_versioned_settings_schema() {
        let directory = test_dir("settings-roundtrip");
        let store = SettingsStore::new(directory.join("settings.json"));
        let settings = AppSettings {
            language: Language::ZhCn,
            autostart: true,
            recent_save_directory: Some("/private/example".into()),
            ..AppSettings::default()
        };
        store.save(&settings).unwrap();
        assert_eq!(
            store.load(),
            SettingsLoad {
                settings,
                status: SettingsLoadStatus::Loaded
            }
        );
        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn corrupt_and_future_settings_fall_back_safely() {
        let directory = test_dir("settings-fallback");
        fs::create_dir_all(&directory).unwrap();
        let path = directory.join("settings.json");
        fs::write(&path, b"not-json").unwrap();
        let store = SettingsStore::new(&path);
        assert_eq!(store.load().status, SettingsLoadStatus::Corrupt);
        fs::write(&path, br#"{"schemaVersion":999,"shortcut":"x","autostart":true,"recentAnnotationStyles":{},"recentSaveDirectory":null,"language":"en"}"#).unwrap();
        let loaded = store.load();
        assert_eq!(loaded.status, SettingsLoadStatus::UnsupportedVersion);
        assert!(!loaded.settings.autostart);
        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn an_unwritable_target_reports_a_typed_failure() {
        let directory = test_dir("settings-unwritable");
        fs::create_dir_all(&directory).unwrap();
        let store = SettingsStore::new(&directory);
        assert!(matches!(
            store.save(&AppSettings::default()),
            Err(SettingsWriteError::Io(_))
        ));
        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn an_unreadable_settings_target_falls_back_safely() {
        let directory = test_dir("settings-unreadable");
        fs::create_dir_all(&directory).unwrap();
        let store = SettingsStore::new(&directory);
        let loaded = store.load();
        assert_eq!(loaded.status, SettingsLoadStatus::Unreadable);
        assert_eq!(loaded.settings, AppSettings::default());
        fs::remove_dir_all(directory).unwrap();
    }
}
