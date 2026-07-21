use serde::{Deserialize, Serialize};

const RELEASES_API: &str = "https://api.github.com/repos/Moch1ce/Snaploom/releases";
const RELEASE_PREFIX: &str = "https://github.com/Moch1ce/Snaploom/releases/tag/";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HttpReleaseResponse<'a> {
    pub request_url: &'a str,
    pub status: u16,
    pub body: &'a str,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "state", rename_all = "camelCase")]
pub enum UpdateState {
    Current,
    Available {
        version: String,
        release_url: String,
    },
    NetworkError,
    RateLimited,
    InvalidResponse,
}

#[derive(Debug, Deserialize)]
struct Release {
    tag_name: String,
    html_url: String,
    draft: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
struct Version(u64, u64, u64);

#[must_use]
pub fn evaluate_release_response(
    current_version: &str,
    response: Result<HttpReleaseResponse<'_>, ()>,
) -> UpdateState {
    let Ok(response) = response else {
        return UpdateState::NetworkError;
    };
    if response.request_url != RELEASES_API {
        return UpdateState::InvalidResponse;
    }
    if matches!(response.status, 403 | 429) {
        return UpdateState::RateLimited;
    }
    if !(200..300).contains(&response.status) {
        return UpdateState::NetworkError;
    }
    let Some(current) = parse_version(current_version.trim_start_matches('v')) else {
        return UpdateState::InvalidResponse;
    };
    let Ok(releases) = serde_json::from_str::<Vec<Release>>(response.body) else {
        return UpdateState::InvalidResponse;
    };
    let had_published_release = releases.iter().any(|release| !release.draft);
    let mut best: Option<(Version, String, String)> = None;
    for release in releases.into_iter().filter(|release| !release.draft) {
        let Some(version) = release.tag_name.strip_prefix('v').and_then(parse_version) else {
            continue;
        };
        let expected_url = format!("{RELEASE_PREFIX}{}", release.tag_name);
        if release.html_url != expected_url {
            continue;
        }
        if best.as_ref().is_none_or(|candidate| version > candidate.0) {
            best = Some((version, release.tag_name, release.html_url));
        }
    }
    match best {
        Some((version, tag, release_url)) if version > current => UpdateState::Available {
            version: tag,
            release_url,
        },
        Some(_) => UpdateState::Current,
        None if had_published_release => UpdateState::InvalidResponse,
        None => UpdateState::Current,
    }
}

fn parse_version(value: &str) -> Option<Version> {
    let mut parts = value.split('.');
    let version = Version(
        parts.next()?.parse().ok()?,
        parts.next()?.parse().ok()?,
        parts.next()?.parse().ok()?,
    );
    parts.next().is_none().then_some(version)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn response(status: u16, body: &str) -> Result<HttpReleaseResponse<'_>, ()> {
        Ok(HttpReleaseResponse {
            request_url: RELEASES_API,
            status,
            body,
        })
    }

    #[test]
    fn ignores_drafts_and_selects_the_highest_valid_release_including_prereleases() {
        let body = r#"[
          {"tag_name":"v9.0.0","html_url":"https://github.com/Moch1ce/Snaploom/releases/tag/v9.0.0","draft":true,"prerelease":false},
          {"tag_name":"v1.3.0","html_url":"https://github.com/Moch1ce/Snaploom/releases/tag/v1.3.0","draft":false,"prerelease":true},
          {"tag_name":"v1.2.0","html_url":"https://github.com/Moch1ce/Snaploom/releases/tag/v1.2.0","draft":false,"prerelease":false},
          {"tag_name":"release-7","html_url":"https://example.invalid","draft":false,"prerelease":false}
        ]"#;
        assert_eq!(
            evaluate_release_response("0.1.0", response(200, body)),
            UpdateState::Available {
                version: "v1.3.0".into(),
                release_url: "https://github.com/Moch1ce/Snaploom/releases/tag/v1.3.0".into(),
            }
        );
    }

    #[test]
    fn classifies_current_network_rate_limit_and_invalid_responses() {
        assert_eq!(
            evaluate_release_response("1.0.0", response(200, "[]")),
            UpdateState::Current
        );
        assert_eq!(
            evaluate_release_response("1.0.0", Err(())),
            UpdateState::NetworkError
        );
        assert_eq!(
            evaluate_release_response("1.0.0", response(403, "")),
            UpdateState::RateLimited
        );
        assert_eq!(
            evaluate_release_response("1.0.0", response(200, "{}")),
            UpdateState::InvalidResponse
        );
        assert_eq!(
            evaluate_release_response(
                "1.0.0",
                Ok(HttpReleaseResponse {
                    request_url: "http://github.com/elsewhere",
                    status: 200,
                    body: "[]"
                })
            ),
            UpdateState::InvalidResponse
        );
    }
}
