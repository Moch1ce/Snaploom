#!/usr/bin/env bash
# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

set -euo pipefail

directory=""
version=""
expected_signing=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --directory) directory="${2:-}"; shift 2 ;;
    --version) version="${2:-}"; shift 2 ;;
    --expected-signing) expected_signing="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || \
      ( "$expected_signing" != "adhoc" && "$expected_signing" != "stable-unsigned" && \
        "$expected_signing" != "developer-id" ) ]]; then
  echo "--directory, --version X.Y.Z, and --expected-signing are required." >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
metadata="$directory/macos-package-metadata.json"
jq -e --arg version "$version" --arg mode "$expected_signing" '
  .schemaVersion == 1 and .version == $version and .platform == "macos-arm64" and
  .signingMode == $mode and .dmgBytes <= 50000000
' "$metadata" >/dev/null
dmg="$directory/$(jq -r .dmg "$metadata")"
host_zip="$directory/$(jq -r .host "$metadata")"
[[ -s "$dmg" && -s "$host_zip" ]] || { echo "macOS payload is missing." >&2; exit 1; }
[[ "$(shasum -a 256 "$dmg" | awk '{print $1}')" == "$(jq -r .dmgSha256 "$metadata")" ]]
[[ "$(shasum -a 256 "$host_zip" | awk '{print $1}')" == "$(jq -r .hostSha256 "$metadata")" ]]

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/snaploom-macos-verify.XXXXXX")"
mount_path="$temporary_root/mount"
mounted=false
app_process=""
cleanup() {
  if [[ -n "$app_process" ]]; then kill "$app_process" 2>/dev/null || true; fi
  if [[ "$mounted" == true ]]; then hdiutil detach "$mount_path" -force -quiet || true; fi
  rm -rf "$temporary_root"
}
trap cleanup EXIT
mkdir -p "$mount_path" "$temporary_root/host" "$temporary_root/home"
hdiutil attach "$dmg" -readonly -nobrowse -mountpoint "$mount_path" -quiet
mounted=true
[[ -d "$mount_path/Snaploom.app" && -L "$mount_path/Applications" && \
   "$(readlink "$mount_path/Applications")" == "/Applications" ]]
ditto "$mount_path/Snaploom.app" "$temporary_root/Snaploom.app"
python3 -m zipfile -e "$host_zip" "$temporary_root/host"
standalone_app="$(find "$temporary_root/host" -type d -name 'Snaploom Capture Host.app' -print -quit)"
embedded_app="$temporary_root/Snaploom.app/Contents/Resources/Snaploom Capture Host.app"
[[ -n "$standalone_app" && -d "$embedded_app" ]]

for app in "$temporary_root/Snaploom.app" "$embedded_app" "$standalone_app"; do
  info="$app/Contents/Info.plist"
  [[ "$(plutil -extract CFBundleShortVersionString raw -o - "$info")" == "$version" ]]
  [[ "$(plutil -extract LSMinimumSystemVersion raw -o - "$info")" == "14.0" ]]
  codesign --verify --deep --strict "$app"
  if [[ "$expected_signing" == "developer-id" ]]; then
    grep -q '^Authority=Developer ID Application:' <<< "$(codesign -dv --verbose=4 "$app" 2>&1)"
  else
    grep -q '^Signature=adhoc$' <<< "$(codesign -dv --verbose=4 "$app" 2>&1)"
  fi
  while IFS= read -r -d '' file_path; do
    if file -b "$file_path" | grep -q 'Mach-O'; then
      [[ "$(lipo -archs "$file_path")" == "arm64" ]] || {
        echo "Non-arm64 Mach-O: $file_path" >&2
        exit 1
      }
    fi
  done < <(find "$app" -type f -print0)
done
desktop_info="$temporary_root/Snaploom.app/Contents/Info.plist"
[[ "$(plutil -extract CFBundleIdentifier raw -o - "$desktop_info")" == "com.snaploom.app" ]]
[[ -n "$(plutil -extract NSScreenCaptureUsageDescription raw -o - "$desktop_info")" ]]
[[ -f "$temporary_root/Snaploom.app/Contents/Resources/icon.icns" ]]
for required in LICENSES/GPL-3.0-or-later.txt NOTICE THIRD-PARTY-NOTICES.txt sbom.cdx.json; do
  [[ -f "$temporary_root/Snaploom.app/Contents/Resources/$required" ]]
done

[[ "$(node "$repo_root/tools/release/hash-tree.mjs" "$embedded_app")" == \
   "$(node "$repo_root/tools/release/hash-tree.mjs" "$standalone_app")" ]]
codesign --verify "$dmg"
if [[ "$expected_signing" == "developer-id" ]]; then
  grep -q '^Authority=Developer ID Application:' <<< "$(codesign -dv --verbose=4 "$dmg" 2>&1)"
else
  grep -q '^Signature=adhoc$' <<< "$(codesign -dv --verbose=4 "$dmg" 2>&1)"
fi
if [[ "$expected_signing" != "developer-id" ]]; then
  jq -e '.notarized == false' "$metadata" >/dev/null
  if [[ "$expected_signing" == "stable-unsigned" ]]; then
    jq -e '.adHocSignatureVerified == true and .gatekeeperWarning == true' "$metadata" >/dev/null
  fi
else
  jq -e '.notarized == true and (.teamId | length >= 5) and (.hostNotarySubmissionId | length >= 8) and (.dmgNotarySubmissionId | length >= 8)' "$metadata" >/dev/null
  xcrun stapler validate "$standalone_app"
  xcrun stapler validate "$dmg"
  spctl --assess --type execute --verbose=4 "$temporary_root/Snaploom.app"
fi

HOME="$temporary_root/home" "$temporary_root/Snaploom.app/Contents/MacOS/snaploom-desktop" \
  > "$temporary_root/launch.log" 2>&1 &
app_process=$!
sleep 5
kill -0 "$app_process" 2>/dev/null || { echo "Installed Desktop did not remain running." >&2; exit 1; }
kill "$app_process"
wait "$app_process" 2>/dev/null || true
app_process=""

echo "macOS DMG, standalone Host, trust mode, architecture, install, launch, and boundary checks passed."
