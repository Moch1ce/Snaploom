use std::collections::BTreeSet;

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum Language {
    ZhCn,
    #[default]
    En,
}

impl Language {
    #[must_use]
    pub fn from_system_locale(locale: Option<&str>) -> Self {
        match locale.map(|value| value.to_ascii_lowercase()) {
            Some(value)
                if value == "zh" || value.starts_with("zh-") || value.starts_with("zh_") =>
            {
                Self::ZhCn
            }
            _ => Self::En,
        }
    }
}

#[must_use]
pub fn system_language() -> Language {
    system_language_from(sys_locale::get_locale)
}

fn system_language_from(provider: impl FnOnce() -> Option<String>) -> Language {
    let locale = provider();
    Language::from_system_locale(locale.as_deref())
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum MessageKey {
    ProductReady,
    StartCapture,
    Settings,
    LaunchAtLogin,
    Exit,
    Shortcut,
    Language,
    PrivacyLogs,
    OpenLogs,
    ClearLogs,
    CheckForUpdates,
    OpenRelease,
    CurrentVersion,
    UpdateAvailable,
    UpdateNetworkError,
    UpdateRateLimited,
    UpdateInvalidResponse,
    PermissionRequired,
    OpenPermissionSettings,
}

const ALL_KEYS: [MessageKey; 19] = [
    MessageKey::ProductReady,
    MessageKey::StartCapture,
    MessageKey::Settings,
    MessageKey::LaunchAtLogin,
    MessageKey::Exit,
    MessageKey::Shortcut,
    MessageKey::Language,
    MessageKey::PrivacyLogs,
    MessageKey::OpenLogs,
    MessageKey::ClearLogs,
    MessageKey::CheckForUpdates,
    MessageKey::OpenRelease,
    MessageKey::CurrentVersion,
    MessageKey::UpdateAvailable,
    MessageKey::UpdateNetworkError,
    MessageKey::UpdateRateLimited,
    MessageKey::UpdateInvalidResponse,
    MessageKey::PermissionRequired,
    MessageKey::OpenPermissionSettings,
];

#[must_use]
pub fn resource_keys(_language: Language) -> BTreeSet<MessageKey> {
    ALL_KEYS.into_iter().collect()
}

#[must_use]
pub const fn localize(language: Language, key: MessageKey) -> &'static str {
    match (language, key) {
        (Language::ZhCn, MessageKey::ProductReady) => "Snaploom 已就绪",
        (Language::ZhCn, MessageKey::StartCapture) => "开始截图",
        (Language::ZhCn, MessageKey::Settings) => "设置",
        (Language::ZhCn, MessageKey::LaunchAtLogin) => "开机启动",
        (Language::ZhCn, MessageKey::Exit) => "退出",
        (Language::ZhCn, MessageKey::Shortcut) => "截图快捷键",
        (Language::ZhCn, MessageKey::Language) => "语言",
        (Language::ZhCn, MessageKey::PrivacyLogs) => "隐私日志",
        (Language::ZhCn, MessageKey::OpenLogs) => "打开日志目录",
        (Language::ZhCn, MessageKey::ClearLogs) => "清除日志",
        (Language::ZhCn, MessageKey::CheckForUpdates) => "检查更新",
        (Language::ZhCn, MessageKey::OpenRelease) => "打开发布页",
        (Language::ZhCn, MessageKey::CurrentVersion) => "当前已是最新版本",
        (Language::ZhCn, MessageKey::UpdateAvailable) => "发现新版本",
        (Language::ZhCn, MessageKey::UpdateNetworkError) => "无法连接更新服务",
        (Language::ZhCn, MessageKey::UpdateRateLimited) => "更新服务请求受限，请稍后再试",
        (Language::ZhCn, MessageKey::UpdateInvalidResponse) => "更新信息无效",
        (Language::ZhCn, MessageKey::PermissionRequired) => "需要屏幕录制权限",
        (Language::ZhCn, MessageKey::OpenPermissionSettings) => "打开系统设置",
        (Language::En, MessageKey::ProductReady) => "Snaploom is ready",
        (Language::En, MessageKey::StartCapture) => "Start Capture",
        (Language::En, MessageKey::Settings) => "Settings",
        (Language::En, MessageKey::LaunchAtLogin) => "Launch at Login",
        (Language::En, MessageKey::Exit) => "Quit",
        (Language::En, MessageKey::Shortcut) => "Capture Shortcut",
        (Language::En, MessageKey::Language) => "Language",
        (Language::En, MessageKey::PrivacyLogs) => "Privacy Logs",
        (Language::En, MessageKey::OpenLogs) => "Open Log Folder",
        (Language::En, MessageKey::ClearLogs) => "Clear Logs",
        (Language::En, MessageKey::CheckForUpdates) => "Check for Updates",
        (Language::En, MessageKey::OpenRelease) => "Open Release",
        (Language::En, MessageKey::CurrentVersion) => "Snaploom is up to date",
        (Language::En, MessageKey::UpdateAvailable) => "Update available",
        (Language::En, MessageKey::UpdateNetworkError) => "Could not reach the update service",
        (Language::En, MessageKey::UpdateRateLimited) => "Update checks are rate limited",
        (Language::En, MessageKey::UpdateInvalidResponse) => "The update response is invalid",
        (Language::En, MessageKey::PermissionRequired) => "Screen recording permission is required",
        (Language::En, MessageKey::OpenPermissionSettings) => "Open System Settings",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unsupported_locales_fall_back_to_english() {
        assert_eq!(Language::from_system_locale(Some("fr-FR")), Language::En);
        assert_eq!(Language::from_system_locale(None), Language::En);
        assert_eq!(
            Language::from_system_locale(Some("zh-Hans-CN")),
            Language::ZhCn
        );
        assert_eq!(
            Language::from_system_locale(Some("zh_CN.UTF-8")),
            Language::ZhCn
        );
    }

    #[test]
    fn both_resource_sets_have_exact_key_parity() {
        assert_eq!(resource_keys(Language::ZhCn), resource_keys(Language::En));
        for key in ALL_KEYS {
            assert!(!localize(Language::ZhCn, key).is_empty());
            assert!(!localize(Language::En, key).is_empty());
        }
    }

    #[test]
    fn system_language_uses_the_native_locale_provider() {
        assert_eq!(
            system_language_from(|| Some("zh-Hans-CN".to_owned())),
            Language::ZhCn
        );
        assert_eq!(
            system_language_from(|| Some("fr-FR".to_owned())),
            Language::En
        );
    }
}
