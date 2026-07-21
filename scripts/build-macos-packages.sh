#!/usr/bin/env bash
# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

set -euo pipefail

usage() {
  echo "Usage: build-macos-packages.sh --version X.Y.Z [--output DIR] (--adhoc | --developer-id IDENTITY --notarize)" >&2
}

version=""
output_directory=""
signing_mode=""
developer_id=""
notarize=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --output) output_directory="${2:-}"; shift 2 ;;
    --adhoc) signing_mode="adhoc"; shift ;;
    --developer-id) signing_mode="developer-id"; developer_id="${2:-}"; shift 2 ;;
    --notarize) notarize=true; shift ;;
    *) usage; exit 2 ;;
  esac
done

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  usage
  exit 2
fi
if [[ "$signing_mode" == "developer-id" && "$notarize" != true ]]; then
  echo "Developer ID packages require notarization; stable packaging cannot downgrade." >&2
  exit 2
fi
if [[ "$signing_mode" == "adhoc" && "$notarize" == true ]]; then
  echo "Ad hoc packages cannot be notarized." >&2
  exit 2
fi
if [[ -z "$signing_mode" ]]; then
  usage
  exit 2
fi

for command in cargo codesign file hdiutil jq lipo node plutil pnpm shasum xattr xcrun; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command is unavailable: $command" >&2; exit 1; }
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_directory="${output_directory:-$repo_root/artifacts/release/macos-arm64}"
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/snaploom-macos-package.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT

node "$repo_root/tools/release/check-release-version.mjs" --version "$version"
rm -rf "$output_directory"
mkdir -p "$output_directory"

if [[ "$signing_mode" == "developer-id" ]]; then
  : "${APPLE_NOTARY_KEY_PATH:?APPLE_NOTARY_KEY_PATH is required}"
  : "${APPLE_NOTARY_KEY_ID:?APPLE_NOTARY_KEY_ID is required}"
  : "${APPLE_NOTARY_ISSUER_ID:?APPLE_NOTARY_ISSUER_ID is required}"
  [[ -f "$APPLE_NOTARY_KEY_PATH" ]] || { echo "APPLE_NOTARY_KEY_PATH is not a file." >&2; exit 2; }
  security find-identity -v -p codesigning | grep -Fq "\"$developer_id\"" || {
    echo "Developer ID identity is unavailable: $developer_id" >&2
    exit 2
  }
fi

export MACOSX_DEPLOYMENT_TARGET=14.0
build_tauri_app() {
  local app_directory="$1"
  local frontend_directory="$2"

  pnpm --dir "$repo_root/$frontend_directory" build
  (
    cd "$repo_root/$app_directory"
    "$repo_root/node_modules/.bin/tauri" build \
      --config tauri.conf.json \
      --config '{"build":{"beforeBuildCommand":""}}' \
      --bundles app
  )
}

# Tauri discovers the Cargo package from its working directory. Running from the
# workspace root can compile the requested package but bundle the first workspace
# binary, which only appears to work when a stale binary is present.
build_tauri_app "product/apps/capture-host" "web/overlay-editor"
host_build="$repo_root/product/target/release/bundle/macos/Snaploom Capture Host.app"
[[ -d "$host_build" ]] || { echo "Capture Host app bundle was not created." >&2; exit 1; }
host_app="$temporary_root/Snaploom Capture Host.app"
ditto "$host_build" "$host_app"

host_resources="$host_app/Contents/Resources"
mkdir -p "$host_resources/LICENSES"
cp "$repo_root/LICENSES/GPL-3.0-or-later.txt" "$host_resources/LICENSES/"
cp "$repo_root/product/distribution/NOTICE" "$host_resources/NOTICE"
cp "$repo_root/product/distribution/CAPTURE-HOST-README.md" "$host_resources/README.md"
node "$repo_root/tools/compliance/generate-rust-notice.mjs" \
  "$repo_root/product/Cargo.toml" "$host_resources/THIRD-PARTY-NOTICES.txt"
node "$repo_root/tools/release/generate-file-sbom.mjs" \
  --root "$host_app" --output "$host_resources/sbom.cdx.json" \
  --name "Snaploom Capture Host" --version "$version" --license GPL-3.0-or-later
xattr -cr "$host_app"

if [[ "$signing_mode" == "developer-id" ]]; then
  codesign --force --options runtime --timestamp --sign "$developer_id" \
    --entitlements "$repo_root/packaging/macos/Snaploom.entitlements" "$host_app"
else
  codesign --force --options runtime --sign - \
    --entitlements "$repo_root/packaging/macos/Snaploom.entitlements" "$host_app"
fi
codesign --verify --deep --strict "$host_app"

host_notary_id=""
if [[ "$notarize" == true ]]; then
  pre_notary_zip="$temporary_root/capture-host-notary.zip"
  ditto -c -k --keepParent "$host_app" "$pre_notary_zip"
  host_notary_json="$temporary_root/host-notary.json"
  xcrun notarytool submit "$pre_notary_zip" \
    --key "$APPLE_NOTARY_KEY_PATH" --key-id "$APPLE_NOTARY_KEY_ID" \
    --issuer "$APPLE_NOTARY_ISSUER_ID" --wait --output-format json > "$host_notary_json"
  [[ "$(jq -r .status "$host_notary_json")" == "Accepted" ]] || {
    echo "Capture Host notarization was not accepted." >&2
    exit 1
  }
  host_notary_id="$(jq -r .id "$host_notary_json")"
  xcrun stapler staple "$host_app"
  xcrun stapler validate "$host_app"
  spctl --assess --type execute --verbose=4 "$host_app"
fi

host_package_root="$temporary_root/snaploom-capture-host-$version-macos-arm64"
mkdir -p "$host_package_root/LICENSES"
ditto "$host_app" "$host_package_root/Snaploom Capture Host.app"
cp "$repo_root/LICENSES/GPL-3.0-or-later.txt" "$host_package_root/LICENSES/"
cp "$repo_root/product/distribution/NOTICE" "$host_package_root/NOTICE"
cp "$repo_root/product/distribution/CAPTURE-HOST-README.md" "$host_package_root/README.md"
cp "$host_resources/THIRD-PARTY-NOTICES.txt" "$host_package_root/THIRD-PARTY-NOTICES.txt"
cp "$host_resources/sbom.cdx.json" "$host_package_root/sbom.cdx.json"
host_zip="$output_directory/snaploom-capture-host-$version-macos-arm64.zip"
node "$repo_root/tools/release/create-deterministic-archive.mjs" \
  --root "$host_package_root" --output "$host_zip" --format zip

build_tauri_app "product/apps/desktop" "web/desktop-settings"
desktop_build="$repo_root/product/target/release/bundle/macos/Snaploom.app"
[[ -d "$desktop_build" ]] || { echo "Desktop app bundle was not created." >&2; exit 1; }
desktop_app="$temporary_root/Snaploom.app"
ditto "$desktop_build" "$desktop_app"
desktop_resources="$desktop_app/Contents/Resources"
mkdir -p "$desktop_resources/LICENSES"
ditto "$host_app" "$desktop_resources/Snaploom Capture Host.app"
cp "$repo_root/LICENSES/GPL-3.0-or-later.txt" "$desktop_resources/LICENSES/"
cp "$repo_root/product/distribution/NOTICE" "$desktop_resources/NOTICE"
cp "$host_resources/THIRD-PARTY-NOTICES.txt" "$desktop_resources/THIRD-PARTY-NOTICES.txt"
node "$repo_root/tools/release/generate-file-sbom.mjs" \
  --root "$desktop_app" --output "$desktop_resources/sbom.cdx.json" \
  --name "Snaploom Desktop" --version "$version" --license GPL-3.0-or-later
xattr -cr "$desktop_app"

if [[ "$signing_mode" == "developer-id" ]]; then
  codesign --force --options runtime --timestamp --sign "$developer_id" \
    --entitlements "$repo_root/packaging/macos/Snaploom.entitlements" "$desktop_app"
else
  codesign --force --options runtime --sign - \
    --entitlements "$repo_root/packaging/macos/Snaploom.entitlements" "$desktop_app"
fi
codesign --verify --deep --strict "$desktop_app"

embedded_hash="$(node "$repo_root/tools/release/hash-tree.mjs" "$desktop_resources/Snaploom Capture Host.app")"
standalone_hash="$(node "$repo_root/tools/release/hash-tree.mjs" "$host_app")"
[[ "$embedded_hash" == "$standalone_hash" ]] || {
  echo "Desktop and standalone Capture Host bundles differ." >&2
  exit 1
}

volume="$temporary_root/volume"
mkdir -p "$volume"
ditto "$desktop_app" "$volume/Snaploom.app"
ln -s /Applications "$volume/Applications"
dmg_name="snaploom-$version-macos-arm64.dmg"
dmg_path="$output_directory/$dmg_name"
hdiutil create -volname Snaploom -srcfolder "$volume" -format UDZO -ov "$dmg_path" >/dev/null
if [[ "$signing_mode" == "developer-id" ]]; then
  codesign --force --timestamp --sign "$developer_id" "$dmg_path"
else
  codesign --force --sign - "$dmg_path"
fi
codesign --verify "$dmg_path"

dmg_notary_id=""
if [[ "$notarize" == true ]]; then
  dmg_notary_json="$temporary_root/dmg-notary.json"
  xcrun notarytool submit "$dmg_path" \
    --key "$APPLE_NOTARY_KEY_PATH" --key-id "$APPLE_NOTARY_KEY_ID" \
    --issuer "$APPLE_NOTARY_ISSUER_ID" --wait --output-format json > "$dmg_notary_json"
  [[ "$(jq -r .status "$dmg_notary_json")" == "Accepted" ]] || {
    echo "DMG notarization was not accepted." >&2
    exit 1
  }
  dmg_notary_id="$(jq -r .id "$dmg_notary_json")"
  xcrun stapler staple "$dmg_path"
  xcrun stapler validate "$dmg_path"
  spctl --assess --type open --context context:primary-signature --verbose=4 "$dmg_path"
fi

dmg_bytes="$(stat -f%z "$dmg_path")"
[[ "$dmg_bytes" -le 50000000 ]] || { echo "DMG exceeds 50,000,000 bytes: $dmg_bytes" >&2; exit 1; }
team_id=""
if [[ "$signing_mode" == "developer-id" ]]; then
  team_id="$(codesign -dv --verbose=4 "$desktop_app" 2>&1 | sed -n 's/^TeamIdentifier=//p')"
fi
if [[ "$notarize" == true ]]; then
  signing_evidence="$output_directory/signing-evidence"
  mkdir -p "$signing_evidence"
  cp "$host_notary_json" "$signing_evidence/host-notary.json"
  cp "$dmg_notary_json" "$signing_evidence/dmg-notary.json"
  {
    codesign --verify --deep --strict --verbose=4 "$host_app"
    codesign --verify --deep --strict --verbose=4 "$desktop_app"
    codesign --verify --verbose=4 "$dmg_path"
    xcrun stapler validate "$host_app"
    xcrun stapler validate "$dmg_path"
    spctl --assess --type execute --verbose=4 "$host_app"
    spctl --assess --type execute --verbose=4 "$desktop_app"
    spctl --assess --type open --context context:primary-signature --verbose=4 "$dmg_path"
  } > "$signing_evidence/apple-verification.log" 2>&1
fi
jq -n \
  --arg version "$version" --arg signingMode "$signing_mode" \
  --arg dmg "$dmg_name" --argjson dmgBytes "$dmg_bytes" \
  --arg dmgSha256 "$(shasum -a 256 "$dmg_path" | awk '{print $1}')" \
  --arg host "$(basename "$host_zip")" \
  --arg hostSha256 "$(shasum -a 256 "$host_zip" | awk '{print $1}')" \
  --arg hostTreeSha256 "$standalone_hash" --arg teamId "$team_id" \
  --arg hostNotarySubmissionId "$host_notary_id" \
  --arg dmgNotarySubmissionId "$dmg_notary_id" \
  --argjson notarized "$notarize" \
  '{schemaVersion:1,version:$version,platform:"macos-arm64",signingMode:$signingMode,notarized:$notarized,dmg:$dmg,dmgBytes:$dmgBytes,dmgSha256:$dmgSha256,host:$host,hostSha256:$hostSha256,hostTreeSha256:$hostTreeSha256,teamId:$teamId,hostNotarySubmissionId:$hostNotarySubmissionId,dmgNotarySubmissionId:$dmgNotarySubmissionId}' \
  > "$output_directory/macos-package-metadata.json"

echo "Created $dmg_name and $(basename "$host_zip") in $output_directory"
