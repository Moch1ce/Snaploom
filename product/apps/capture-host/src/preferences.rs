use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::atomic::{AtomicU64, Ordering};

use serde::{Deserialize, Serialize};

const SCHEMA_VERSION: u32 = 1;
static NEXT_TEMP_FILE: AtomicU64 = AtomicU64::new(1);

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct AnnotationStylePreferences {
    pub(crate) color: String,
    pub(crate) stroke_width: u16,
    pub(crate) font_size: u16,
    pub(crate) mosaic_brush_size: u16,
    pub(crate) mosaic_block_size: u16,
}

impl Default for AnnotationStylePreferences {
    fn default() -> Self {
        Self {
            color: "#FF4D4F".into(),
            stroke_width: 4,
            font_size: 24,
            mosaic_brush_size: 32,
            mosaic_block_size: 12,
        }
    }
}

impl AnnotationStylePreferences {
    fn is_valid(&self) -> bool {
        matches!(
            self.color.as_str(),
            "#FF4D4F" | "#FADB14" | "#07C977" | "#1677FF" | "#202124" | "#FFFFFF"
        ) && matches!(self.stroke_width, 2 | 4 | 8)
            && matches!(self.font_size, 16 | 24 | 32)
            && matches!(self.mosaic_brush_size, 16 | 32 | 64)
            && matches!(self.mosaic_block_size, 8 | 12 | 16)
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct StoredPreferences {
    schema_version: u32,
    annotation_style: AnnotationStylePreferences,
    recent_save_directory: Option<PathBuf>,
}

impl Default for StoredPreferences {
    fn default() -> Self {
        Self {
            schema_version: SCHEMA_VERSION,
            annotation_style: AnnotationStylePreferences::default(),
            recent_save_directory: None,
        }
    }
}

impl StoredPreferences {
    fn is_valid(&self) -> bool {
        self.schema_version == SCHEMA_VERSION
            && self.annotation_style.is_valid()
            && self
                .recent_save_directory
                .as_ref()
                .is_none_or(|path| path.is_absolute())
    }
}

#[derive(Debug, Clone)]
pub(crate) struct PreferencesSnapshot {
    pub(crate) annotation_style: AnnotationStylePreferences,
    pub(crate) recent_save_directory: Option<PathBuf>,
}

#[derive(Debug)]
pub(crate) struct HostPreferencesStore {
    path: PathBuf,
    current: Mutex<StoredPreferences>,
}

impl HostPreferencesStore {
    pub(crate) fn new(path: impl Into<PathBuf>) -> Self {
        let path = path.into();
        let current = fs::read(&path)
            .ok()
            .and_then(|bytes| serde_json::from_slice::<StoredPreferences>(&bytes).ok())
            .filter(StoredPreferences::is_valid)
            .unwrap_or_default();
        Self {
            path,
            current: Mutex::new(current),
        }
    }

    pub(crate) fn snapshot(&self) -> PreferencesSnapshot {
        let current = self
            .current
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        PreferencesSnapshot {
            annotation_style: current.annotation_style.clone(),
            recent_save_directory: current.recent_save_directory.clone(),
        }
    }

    pub(crate) fn update_annotation_style(
        &self,
        style: AnnotationStylePreferences,
    ) -> Result<(), PreferencesWriteError> {
        if !style.is_valid() {
            return Err(PreferencesWriteError::InvalidValue);
        }
        let mut current = self
            .current
            .lock()
            .map_err(|_| PreferencesWriteError::Unavailable)?;
        let mut next = current.clone();
        next.annotation_style = style;
        self.persist(&next)?;
        *current = next;
        Ok(())
    }

    pub(crate) fn update_recent_save_directory(
        &self,
        directory: Option<&Path>,
    ) -> Result<(), PreferencesWriteError> {
        if directory.is_some_and(|path| !path.is_absolute()) {
            return Err(PreferencesWriteError::InvalidValue);
        }
        let mut current = self
            .current
            .lock()
            .map_err(|_| PreferencesWriteError::Unavailable)?;
        let mut next = current.clone();
        next.recent_save_directory = directory.map(Path::to_path_buf);
        self.persist(&next)?;
        *current = next;
        Ok(())
    }

    fn persist(&self, preferences: &StoredPreferences) -> Result<(), PreferencesWriteError> {
        let parent = self
            .path
            .parent()
            .ok_or(PreferencesWriteError::InvalidPath)?;
        fs::create_dir_all(parent).map_err(|_| PreferencesWriteError::Unavailable)?;
        let temporary = parent.join(format!(
            ".snaploom-capture-preferences-{}-{}.tmp",
            std::process::id(),
            NEXT_TEMP_FILE.fetch_add(1, Ordering::Relaxed)
        ));
        let result = (|| {
            let mut file = OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&temporary)
                .map_err(|_| PreferencesWriteError::Unavailable)?;
            let bytes = serde_json::to_vec_pretty(preferences)
                .map_err(|_| PreferencesWriteError::Unavailable)?;
            file.write_all(&bytes)
                .map_err(|_| PreferencesWriteError::Unavailable)?;
            file.sync_all()
                .map_err(|_| PreferencesWriteError::Unavailable)?;
            #[cfg(windows)]
            if self.path.exists() {
                fs::remove_file(&self.path).map_err(|_| PreferencesWriteError::Unavailable)?;
            }
            fs::rename(&temporary, &self.path).map_err(|_| PreferencesWriteError::Unavailable)
        })();
        if result.is_err() {
            let _ = fs::remove_file(temporary);
        }
        result
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum PreferencesWriteError {
    InvalidPath,
    InvalidValue,
    Unavailable,
}

#[cfg(test)]
mod tests {
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::*;

    fn test_path(label: &str) -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir()
            .join(format!("snaploom-{label}-{}-{nonce}", std::process::id()))
            .join("capture-preferences.json")
    }

    #[test]
    fn round_trips_valid_style_and_recent_directory() {
        let path = test_path("capture-preferences-roundtrip");
        let directory = path.parent().unwrap().join("captures");
        let store = HostPreferencesStore::new(&path);
        let style = AnnotationStylePreferences {
            color: "#1677FF".into(),
            stroke_width: 8,
            font_size: 32,
            mosaic_brush_size: 64,
            mosaic_block_size: 16,
        };
        store.update_annotation_style(style.clone()).unwrap();
        store
            .update_recent_save_directory(Some(&directory))
            .unwrap();
        let loaded = HostPreferencesStore::new(&path).snapshot();
        assert_eq!(loaded.annotation_style, style);
        assert_eq!(
            loaded.recent_save_directory.as_deref(),
            Some(directory.as_path())
        );
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }

    #[test]
    fn corrupt_future_or_invalid_values_fall_back_without_blocking_capture() {
        let path = test_path("capture-preferences-fallback");
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, b"not-json").unwrap();
        assert_eq!(
            HostPreferencesStore::new(&path).snapshot().annotation_style,
            AnnotationStylePreferences::default()
        );
        fs::write(
            &path,
            br##"{
              "schemaVersion": 999,
              "annotationStyle": {
                "color": "#1677FF",
                "strokeWidth": 8,
                "fontSize": 32,
                "mosaicBrushSize": 64,
                "mosaicBlockSize": 16
              },
              "recentSaveDirectory": null
            }"##,
        )
        .unwrap();
        assert_eq!(
            HostPreferencesStore::new(&path).snapshot().annotation_style,
            AnnotationStylePreferences::default()
        );
        let store = HostPreferencesStore::new(&path);
        let invalid = AnnotationStylePreferences {
            color: "#123456".into(),
            ..AnnotationStylePreferences::default()
        };
        assert_eq!(
            store.update_annotation_style(invalid),
            Err(PreferencesWriteError::InvalidValue)
        );
        fs::remove_dir_all(path.parent().unwrap()).unwrap();
    }

    #[test]
    fn failed_write_does_not_change_the_in_memory_snapshot() {
        let parent_file = test_path("capture-preferences-write-failure");
        fs::create_dir_all(parent_file.parent().unwrap()).unwrap();
        fs::write(&parent_file, b"not a directory").unwrap();
        let store = HostPreferencesStore::new(parent_file.join("preferences.json"));
        let replacement = AnnotationStylePreferences {
            color: "#1677FF".into(),
            ..AnnotationStylePreferences::default()
        };

        assert_eq!(
            store.update_annotation_style(replacement),
            Err(PreferencesWriteError::Unavailable)
        );
        assert_eq!(
            store.snapshot().annotation_style,
            AnnotationStylePreferences::default()
        );
        fs::remove_dir_all(parent_file.parent().unwrap()).unwrap();
    }
}
