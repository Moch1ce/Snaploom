use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

const DEFAULT_MAX_FILES: usize = 5;
const DEFAULT_MAX_BYTES: u64 = 2 * 1024 * 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
pub enum PrivacyLevel {
    Info,
    Warning,
    Error,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
pub enum PrivacyEvent {
    AppStarted,
    SettingsLoadedDefault,
    SettingsWriteFailed,
    CaptureRequested,
    CaptureCompleted,
    CaptureCanceled,
    CaptureFailed,
    ShortcutChanged,
    ShortcutResumeFailed,
    AutostartChanged,
    AutostartFailed,
    UpdateCheckStarted,
    UpdateCheckCompleted,
    UpdateCheckFailed,
    LogsCleared,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PrivacyRecord {
    pub utc: String,
    pub level: PrivacyLevel,
    pub event: PrivacyEvent,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub exception_type: Option<String>,
}

#[derive(Debug)]
pub struct PrivacyLog {
    directory: PathBuf,
    max_files: usize,
    max_bytes: u64,
}

impl PrivacyLog {
    #[must_use]
    pub fn new(directory: impl Into<PathBuf>) -> Self {
        Self::with_limits(directory, DEFAULT_MAX_FILES, DEFAULT_MAX_BYTES)
    }

    #[must_use]
    pub fn with_limits(directory: impl Into<PathBuf>, max_files: usize, max_bytes: u64) -> Self {
        Self {
            directory: directory.into(),
            max_files: max_files.clamp(1, DEFAULT_MAX_FILES),
            max_bytes: max_bytes.clamp(1, DEFAULT_MAX_BYTES),
        }
    }

    #[must_use]
    pub fn directory(&self) -> &Path {
        &self.directory
    }

    pub fn append(&self, record: &PrivacyRecord) -> Result<(), PrivacyLogError> {
        if !is_utc_timestamp(&record.utc)
            || record.exception_type.as_deref().is_some_and(|name| {
                name.is_empty()
                    || name.len() > 128
                    || !name.chars().all(|value| {
                        value.is_ascii_alphanumeric() || matches!(value, ':' | '_' | '.')
                    })
            })
        {
            return Err(PrivacyLogError::InvalidRecord);
        }
        fs::create_dir_all(&self.directory).map_err(PrivacyLogError::Io)?;
        let mut bytes = serde_json::to_vec(record).map_err(PrivacyLogError::Json)?;
        bytes.push(b'\n');
        let active = self.directory.join("snaploom.log");
        let current_bytes = fs::metadata(&active).map(|value| value.len()).unwrap_or(0);
        if current_bytes.saturating_add(bytes.len() as u64) > self.max_bytes {
            self.rotate()?;
        }
        let mut file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(active)
            .map_err(PrivacyLogError::Io)?;
        file.write_all(&bytes).map_err(PrivacyLogError::Io)
    }

    pub fn clear(&self) -> Result<(), PrivacyLogError> {
        for index in 0..self.max_files {
            let path = self.path_for(index);
            match fs::remove_file(path) {
                Ok(()) => {}
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                Err(error) => return Err(PrivacyLogError::Io(error)),
            }
        }
        Ok(())
    }

    fn rotate(&self) -> Result<(), PrivacyLogError> {
        if self.max_files > 1 {
            let oldest = self.path_for(self.max_files - 1);
            let _ = fs::remove_file(oldest);
            for index in (1..self.max_files).rev() {
                let from = self.path_for(index - 1);
                let to = self.path_for(index);
                match fs::rename(from, to) {
                    Ok(()) => {}
                    Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                    Err(error) => return Err(PrivacyLogError::Io(error)),
                }
            }
        } else {
            let _ = fs::remove_file(self.path_for(0));
        }
        Ok(())
    }

    fn path_for(&self, index: usize) -> PathBuf {
        if index == 0 {
            self.directory.join("snaploom.log")
        } else {
            self.directory.join(format!("snaploom.log.{index}"))
        }
    }
}

#[must_use]
pub fn utc_now() -> String {
    time::OffsetDateTime::now_utc()
        .format(&time::format_description::well_known::Rfc3339)
        .unwrap_or_else(|_| "1970-01-01T00:00:00Z".into())
}

fn is_utc_timestamp(value: &str) -> bool {
    value.ends_with('Z')
        && value.len() >= 20
        && value.as_bytes().get(4) == Some(&b'-')
        && value.as_bytes().get(7) == Some(&b'-')
        && value.as_bytes().get(10) == Some(&b'T')
}

#[derive(Debug)]
pub enum PrivacyLogError {
    InvalidRecord,
    Io(std::io::Error),
    Json(serde_json::Error),
}

impl std::fmt::Display for PrivacyLogError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(match self {
            Self::InvalidRecord => "privacy log record rejected",
            Self::Io(_) => "privacy log I/O failed",
            Self::Json(_) => "privacy log serialization failed",
        })
    }
}

impl std::error::Error for PrivacyLogError {}

#[cfg(test)]
mod tests {
    use std::time::{SystemTime, UNIX_EPOCH};

    use super::*;

    fn test_dir() -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("snaploom-privacy-{}-{nonce}", std::process::id()))
    }

    #[test]
    fn rotates_at_the_configured_limits_without_sensitive_payloads() {
        let directory = test_dir();
        let log = PrivacyLog::with_limits(&directory, 3, 180);
        let record = PrivacyRecord {
            utc: "2026-07-21T01:02:03Z".into(),
            level: PrivacyLevel::Info,
            event: PrivacyEvent::CaptureCompleted,
            exception_type: None,
        };
        for _ in 0..12 {
            log.append(&record).unwrap();
        }
        let files: Vec<_> = fs::read_dir(&directory).unwrap().collect();
        assert!(files.len() <= 3);
        let joined = files
            .into_iter()
            .map(|entry| fs::read_to_string(entry.unwrap().path()).unwrap())
            .collect::<String>();
        assert!(!joined.contains("pixels"));
        assert!(!joined.contains("clipboard"));
        assert!(!joined.contains("/Users/"));
        log.clear().unwrap();
        assert_eq!(fs::read_dir(&directory).unwrap().count(), 0);
        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn rejects_messages_disguised_as_exception_types() {
        let log = PrivacyLog::new(test_dir());
        let record = PrivacyRecord {
            utc: "2026-07-21T01:02:03Z".into(),
            level: PrivacyLevel::Error,
            event: PrivacyEvent::CaptureFailed,
            exception_type: Some("network error: token=/private/path".into()),
        };
        assert!(matches!(
            log.append(&record),
            Err(PrivacyLogError::InvalidRecord)
        ));
    }

    #[test]
    fn generated_timestamps_are_explicitly_utc() {
        assert!(utc_now().ends_with('Z'));
    }
}
